using AiAgent.Backend.Dtos.Settings;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Models.Settings;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Settings;

[DynamicApiController]
[ApiDescriptionSettings("Settings", KeepName = true)]
[Route("api/v1/settings")]
public sealed class SettingsAppService : IDynamicApiController
{
    private const string RedactedSecret = "********";
    private const int LlmDiagnosticsMaxTokens = 1024;
    private const string UiSettingKey = "ui";

    private readonly ISqlSugarClient _db;
    private readonly IModelCatalogService _catalogService;
    private readonly IModelProviderOptionsService _providerOptionsService;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// 初始化设置动态接口服务，组合模型目录、供应商选项和外部服务诊断能力。
    /// </summary>
    public SettingsAppService(
        ISqlSugarClient db,
        IModelCatalogService catalogService,
        IModelProviderOptionsService providerOptionsService,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _catalogService = catalogService;
        _providerOptionsService = providerOptionsService;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 获取设置中心完整数据，包括 UI、catalog 和 provider 选项。
    /// </summary>
    [HttpGet("")]
    public SettingsResponse GetSettings()
    {
        return new SettingsResponse
        {
            Ui = LoadUiSettings(),
            Catalog = _catalogService.Load(redactSecrets: true),
            Providers = _providerOptionsService.GetProviderChoices()
        };
    }

    /// <summary>
    /// 获取 UI 偏好设置。
    /// </summary>
    [HttpGet("ui")]
    public UiSettings GetUiSettings()
    {
        return LoadUiSettings();
    }

    /// <summary>
    /// 更新 UI 偏好设置。
    /// </summary>
    [HttpPut("ui")]
    public UiSettings UpdateUiSettings([FromBody] UiSettingsPayload? payload)
    {
        var current = LoadUiSettings();
        if (payload is null)
        {
            return current;
        }

        if (!string.IsNullOrWhiteSpace(payload.Theme))
        {
            current.Theme = payload.Theme.Trim();
        }

        if (!string.IsNullOrWhiteSpace(payload.Language))
        {
            current.Language = NormalizeLanguage(payload.Language);
        }

        SaveUiSettings(current);
        return current;
    }

    /// <summary>
    /// 获取模型服务 catalog。
    /// </summary>
    [HttpGet("catalog")]
    public object GetCatalog()
    {
        return new { catalog = _catalogService.Load(redactSecrets: true) };
    }

    /// <summary>
    /// 保存模型服务 catalog 草稿。
    /// </summary>
    [HttpPut("catalog")]
    public object UpdateCatalog([FromBody] CatalogPayload payload)
    {
        var saved = _catalogService.Save(payload.Catalog);
        return new { catalog = saved };
    }

    /// <summary>
    /// 将模型服务 catalog 应用到运行时设置。
    /// </summary>
    [HttpPost("apply")]
    public ApplyCatalogResponse ApplyCatalog([FromBody] CatalogPayload? payload = null)
    {
        var result = _catalogService.Apply(payload?.Catalog);
        return new ApplyCatalogResponse
        {
            Catalog = _catalogService.Load(redactSecrets: true),
            Runtime = result
        };
    }

    private UiSettings LoadUiSettings()
    {
        var row = _db.Queryable<AiSettingSnapshot>()
            .Where(x => x.SettingKey == UiSettingKey)
            .OrderByDescending(x => x.AppliedAt)
            .OrderByDescending(x => x.Id)
            .First();
        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson))
        {
            return new UiSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<UiSettings>(row.PayloadJson) ?? new UiSettings();
            settings.Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "light" : settings.Theme;
            settings.Language = NormalizeLanguage(settings.Language);
            return settings;
        }
        catch
        {
            return new UiSettings();
        }
    }

    private void SaveUiSettings(UiSettings settings)
    {
        var latestVersion = _db.Queryable<AiSettingSnapshot>()
            .Where(x => x.SettingKey == UiSettingKey)
            .OrderByDescending(x => x.VersionNo)
            .Select(x => x.VersionNo)
            .First();

        var row = new AiSettingSnapshot
        {
            SettingKey = UiSettingKey,
            PayloadJson = JsonSerializer.Serialize(settings),
            VersionNo = latestVersion + 1,
            AppliedAt = DateTime.UtcNow,
            AppliedBy = "default",
            Remark = "UI settings updated"
        };

        _db.Insertable(row).ExecuteCommand();
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "zh-CN";
        }

        var value = language.Trim().ToLowerInvariant();
        return value is "en" or "en-us" ? "en-US" : "zh-CN";
    }

    /// <summary>
    /// 对指定模型服务执行连通性诊断。
    /// </summary>
    [HttpPost("tests/{service}/start")]
    public async Task<ServiceTestResponse> TestService([FromRoute] string service, [FromBody] CatalogPayload? payload = null)
    {
        var catalog = payload?.Catalog ?? _catalogService.Load();
        MergeStoredSecrets(catalog);
        var serviceCatalog = ResolveService(catalog, service);
        if (serviceCatalog is null)
        {
            return new ServiceTestResponse
            {
                State = "failed",
                Message = $"Unknown service '{service}'.",
                Summary = "Unknown service.",
                Logs =
                [
                    $"[error] Unknown service '{service}'.",
                    "[completed] Diagnostics stopped."
                ]
            };
        }

        var profile = serviceCatalog.Profiles.FirstOrDefault(x => x.Id == serviceCatalog.ActiveProfileId)
            ?? serviceCatalog.Profiles.FirstOrDefault();
        var model = service.Equals("search", StringComparison.OrdinalIgnoreCase)
            ? null
            : profile?.Models.FirstOrDefault(x => x.Id == serviceCatalog.ActiveModelId)
                ?? profile?.Models.FirstOrDefault();

        var configured = service.Equals("search", StringComparison.OrdinalIgnoreCase)
            ? profile is not null && !string.IsNullOrWhiteSpace(profile.Provider) && profile.Provider != "none"
            : profile is not null && model is not null && !string.IsNullOrWhiteSpace(model.Model);

        if (configured && service.Equals("llm", StringComparison.OrdinalIgnoreCase))
        {
            return await TestLlmService(profile!, model!);
        }

        if (configured && service.Equals("embedding", StringComparison.OrdinalIgnoreCase))
        {
            return await TestEmbeddingService(catalog, profile!, model!);
        }

        var logs = BuildDiagnosticsLogs(service, profile, model, configured);
        var summary = configured
            ? $"{service} test completed successfully."
            : $"{service} configuration is incomplete.";

        return new ServiceTestResponse
        {
            State = configured ? "success" : "failed",
            Message = configured
                ? summary
                : $"{service} is not configured yet.",
            Summary = summary,
            Logs = logs,
            ProfileId = profile?.Id,
            ModelId = model?.Id
        };
    }

    private async Task<ServiceTestResponse> TestLlmService(CatalogProfile profile, CatalogModel model)
    {
        var endpoint = BuildChatCompletionsEndpoint(profile.BaseUrl);
        var logs = new List<string>
        {
            "Preparing llm diagnostics.",
            "[info] Preparing configuration snapshot.",
            "[config] Using active profile.",
            $"[info] Provider binding: `{profile.Binding ?? "not set"}`.",
            $"[info] Resolved model `{model.Model}`.",
            $"[info] Request target: {profile.BaseUrl}.",
            $"[info] Token options: {{\"max_tokens\": {LlmDiagnosticsMaxTokens}}}"
        };

        if (string.IsNullOrWhiteSpace(profile.ApiKey) || profile.ApiKey == RedactedSecret)
        {
            logs.Add("[error] API key is missing. Save the profile with a real key before testing.");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "llm API key is missing.", "LLM test failed before sending request.", logs, profile.Id, model.Id);
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logs.Add("[error] Base URL is missing.");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "llm endpoint is missing.", "LLM test failed before sending request.", logs, profile.Id, model.Id);
        }

        logs.Add($"[http] POST {endpoint}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            foreach (var header in (profile.ExtraHeaders ?? new Dictionary<string, string>()).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var body = new
            {
                model = model.Model,
                messages = new object[]
                {
                    new { role = "system", content = "Respond briefly but include your model identity if possible." },
                    new { role = "user", content = "Say OK and identify the model you are using." }
                },
                max_tokens = LlmDiagnosticsMaxTokens,
                temperature = 0.1,
                stream = false
            };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(45);
            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                logs.Add($"[error] Provider returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
                logs.Add($"[response] {TrimForLog(responseText)}");
                logs.Add("[completed] Diagnostics completed with errors.");
                return BuildTestResponse("failed", "llm provider request failed.", $"Provider returned HTTP {(int)response.StatusCode}.", logs, profile.Id, model.Id);
            }

            var snippet = ExtractChatCompletionText(responseText);
            if (string.IsNullOrWhiteSpace(snippet))
            {
                logs.Add("[error] Provider returned an empty response.");
                logs.Add($"[response] {TrimForLog(responseText)}");
                logs.Add("[completed] Diagnostics completed with errors.");
                return BuildTestResponse("failed", "llm provider returned an empty response.", "LLM test failed with an empty response.", logs, profile.Id, model.Id);
            }

            logs.Add("[response] Received LLM response.");
            logs.Add($"[response_snippet] {TrimForLog(snippet)}");
            logs.Add("[info] Basic LLM completion succeeded.");
            logs.Add("[info] Detecting model context window.");
            var contextWindow = string.IsNullOrWhiteSpace(model.ContextWindow) ? "unknown" : model.ContextWindow;
            logs.Add($"[context_window] Detected context window {contextWindow} tokens.");
            logs.Add("[completed] llm test completed successfully.");
            return BuildTestResponse("success", "llm test completed successfully.", "Provider connection succeeded.", logs, profile.Id, model.Id);
        }
        catch (TaskCanceledException ex)
        {
            logs.Add($"[error] Provider request timed out: {ex.Message}");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "llm provider request timed out.", "Provider request timed out.", logs, profile.Id, model.Id);
        }
        catch (HttpRequestException ex)
        {
            logs.Add($"[error] Provider request failed: {ex.Message}");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "llm provider request failed.", "Provider request failed.", logs, profile.Id, model.Id);
        }
        catch (Exception ex)
        {
            logs.Add($"[error] Provider diagnostics failed: {ex.Message}");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "llm diagnostics failed.", "Provider diagnostics failed.", logs, profile.Id, model.Id);
        }
    }

    private async Task<ServiceTestResponse> TestEmbeddingService(ModelCatalog catalog, CatalogProfile profile, CatalogModel model)
    {
        var endpoint = BuildEmbeddingEndpoint(profile.Binding, profile.BaseUrl);
        var provider = NormalizeProvider(profile.Binding);
        var logs = new List<string>
        {
            "Preparing embedding diagnostics.",
            "[info] Preparing configuration snapshot.",
            "[config] Using active profile.",
            $"[info] Provider binding: `{profile.Binding ?? "not set"}`.",
            $"[info] Resolved embedding model `{model.Model}`.",
            $"[info] Request target: {endpoint}.",
            "[info] Probing native dimension with a small batch.",
            "[info] Sending no `dimensions` field during diagnostics."
        };

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logs.Add("[error] Embedding endpoint is missing.");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "embedding endpoint is missing.", "Embedding test failed before sending request.", logs, profile.Id, model.Id);
        }

        if (EmbeddingProviderRequiresApiKey(provider) && (string.IsNullOrWhiteSpace(profile.ApiKey) || profile.ApiKey == RedactedSecret))
        {
            logs.Add("[error] API key is missing. Save the profile with a real key before testing.");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "embedding API key is missing.", "Embedding test failed before sending request.", logs, profile.Id, model.Id);
        }

        logs.Add($"[http] POST {endpoint}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(profile.ApiKey) && profile.ApiKey != RedactedSecret && !provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
            }

            foreach (var header in (profile.ExtraHeaders ?? new Dictionary<string, string>()).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var probeTexts = new[] { "AiAgent embedding smoke test", "AiAgent retrieval batch probe" };
            var body = BuildEmbeddingRequestBody(provider, model.Model, probeTexts);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                logs.Add($"[error] Provider returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
                logs.Add($"[response] {TrimForLog(responseText)}");
                logs.Add("[completed] Diagnostics completed with errors.");
                return BuildTestResponse("failed", "embedding provider request failed.", $"Provider returned HTTP {(int)response.StatusCode}.", logs, profile.Id, model.Id);
            }

            var dimensions = ExtractEmbeddingDimensions(responseText);
            if (dimensions.Count != probeTexts.Length)
            {
                logs.Add($"[error] Embedding service returned an unexpected vector count: expected {probeTexts.Length}, got {dimensions.Count}.");
                logs.Add("[completed] Diagnostics completed with errors.");
                return BuildTestResponse("failed", "embedding vector count mismatch.", "Embedding provider returned an unexpected vector count.", logs, profile.Id, model.Id);
            }

            var detectedDimension = dimensions[0];
            if (detectedDimension <= 0 || dimensions.Any(x => x != detectedDimension))
            {
                logs.Add("[error] Embedding service returned empty or inconsistent vector dimensions.");
                logs.Add("[completed] Diagnostics completed with errors.");
                return BuildTestResponse("failed", "embedding vector dimensions are inconsistent.", "Embedding test failed with inconsistent dimensions.", logs, profile.Id, model.Id);
            }

            var catalogDimension = ParseInt(model.Dimension);
            var supportedDimensions = KnownSupportedDimensions(provider, model.Model);
            model.Dimension = detectedDimension.ToString();
            if (!string.IsNullOrWhiteSpace(supportedDimensions))
            {
                model.SupportedDimensions = supportedDimensions;
            }

            var savedCatalog = _catalogService.Save(catalog);
            logs.Add("[response] Embedding vector received.");
            logs.Add($"[capabilities] Probe returned {detectedDimension}d.");
            if (!string.IsNullOrWhiteSpace(supportedDimensions))
            {
                logs.Add($"[capabilities] Supported dimensions: {supportedDimensions}.");
            }
            logs.Add(catalogDimension.HasValue && catalogDimension.Value != detectedDimension
                ? $"[info] Catalog dim {catalogDimension.Value}d overwritten with API probe value {detectedDimension}d."
                : $"[info] Active dim {detectedDimension}d set from API probe.");
            logs.Add("[catalog] Saved detected embedding dimension to database.");
            logs.Add("[completed] embedding test completed successfully.");

            var result = BuildTestResponse("success", "embedding test completed successfully.", $"Provider connection succeeded. Detected {detectedDimension}d vectors.", logs, profile.Id, model.Id);
            result.DetectedDimension = detectedDimension;
            result.SupportedDimensions = supportedDimensions;
            result.Catalog = savedCatalog;
            return result;
        }
        catch (TaskCanceledException ex)
        {
            logs.Add($"[error] Provider request timed out: {ex.Message}");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "embedding provider request timed out.", "Provider request timed out.", logs, profile.Id, model.Id);
        }
        catch (HttpRequestException ex)
        {
            logs.Add($"[error] Provider request failed: {ex.Message}");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "embedding provider request failed.", "Provider request failed.", logs, profile.Id, model.Id);
        }
        catch (Exception ex)
        {
            logs.Add($"[error] Provider diagnostics failed: {ex.Message}");
            logs.Add("[completed] Diagnostics completed with errors.");
            return BuildTestResponse("failed", "embedding diagnostics failed.", "Provider diagnostics failed.", logs, profile.Id, model.Id);
        }
    }

    private static ServiceTestResponse BuildTestResponse(
        string state,
        string message,
        string summary,
        List<string> logs,
        string? profileId,
        string? modelId)
    {
        return new ServiceTestResponse
        {
            State = state,
            Message = message,
            Summary = summary,
            Logs = logs,
            ProfileId = profileId,
            ModelId = modelId
        };
    }

    private static CatalogService? ResolveService(ModelCatalog catalog, string service)
    {
        return service.ToLowerInvariant() switch
        {
            "llm" => catalog.Services.Llm,
            "embedding" => catalog.Services.Embedding,
            "search" => catalog.Services.Search,
            "tts" => catalog.Services.Tts,
            "stt" => catalog.Services.Stt,
            "imagegen" => catalog.Services.Imagegen,
            "videogen" => catalog.Services.Videogen,
            _ => null
        };
    }

    private void MergeStoredSecrets(ModelCatalog catalog)
    {
        var stored = _catalogService.Load(redactSecrets: false);
        var storedProfiles = EnumerateProfiles(stored)
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var profile in EnumerateProfiles(catalog))
        {
            if (profile.ApiKey == RedactedSecret
                && storedProfiles.TryGetValue(profile.Id, out var storedProfile)
                && !string.IsNullOrWhiteSpace(storedProfile.ApiKey)
                && storedProfile.ApiKey != RedactedSecret)
            {
                profile.ApiKey = storedProfile.ApiKey;
            }
        }
    }

    private static IEnumerable<CatalogProfile> EnumerateProfiles(ModelCatalog catalog)
    {
        foreach (var service in new[]
        {
            catalog.Services.Llm,
            catalog.Services.Embedding,
            catalog.Services.Search,
            catalog.Services.Tts,
            catalog.Services.Stt,
            catalog.Services.Imagegen,
            catalog.Services.Videogen
        })
        {
            foreach (var profile in service.Profiles)
            {
                yield return profile;
            }
        }
    }

    private static string BuildChatCompletionsEndpoint(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "";
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/chat/completions";
    }

    private static string BuildEmbeddingEndpoint(string? provider, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "";
        }

        var providerName = NormalizeProvider(provider);
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (providerName == "ollama")
        {
            if (trimmed.EndsWith("/api/embed", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                return $"{trimmed}/embed";
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')))
            {
                return $"{uri.Scheme}://{uri.Authority}/api/embed";
            }

            return $"{trimmed}/api/embed";
        }

        if (providerName == "cohere")
        {
            if (trimmed.EndsWith("/embed", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed.EndsWith("/v2", StringComparison.OrdinalIgnoreCase)
                ? $"{trimmed}/embed"
                : $"{trimmed}/v2/embed";
        }

        if (trimmed.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/embeddings";
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = (provider ?? "").Trim().Replace("-", "_").ToLowerInvariant();
        return value switch
        {
            "lmstudio" => "lm_studio",
            _ => value
        };
    }

    private static bool EmbeddingProviderRequiresApiKey(string provider)
    {
        return provider is not ("ollama" or "lm_studio" or "vllm" or "openai_compatible");
    }

    private static object BuildEmbeddingRequestBody(string provider, string model, string[] probeTexts)
    {
        if (provider == "ollama")
        {
            return new
            {
                model,
                input = probeTexts,
                keep_alive = "5m"
            };
        }

        if (provider == "cohere")
        {
            return new
            {
                model,
                texts = probeTexts,
                input_type = "search_document",
                embedding_types = new[] { "float" },
                truncate = "NONE"
            };
        }

        return new
        {
            model,
            input = probeTexts,
            encoding_format = "float"
        };
    }

    private static List<int> ExtractEmbeddingDimensions(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new InvalidOperationException("Embedding provider returned an empty response body.");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Embedding response is not a JSON object.");
        }

        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"Embedding provider returned error payload: {TrimForLog(error.ToString())}");
        }

        var dimensions = new List<int>();
        CollectEmbeddingDimensions(root, dimensions);
        if (dimensions.Count == 0)
        {
            var keys = string.Join(", ", root.EnumerateObject().Select(x => x.Name));
            throw new InvalidOperationException($"Cannot parse embeddings from response JSON. Top-level keys={keys}.");
        }

        return dimensions;
    }

    private static void CollectEmbeddingDimensions(JsonElement element, List<int> dimensions)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("data", out var data))
        {
            CollectDimensionsFromArray(data, dimensions);
        }

        if (element.TryGetProperty("embeddings", out var embeddings))
        {
            if (embeddings.ValueKind == JsonValueKind.Object && embeddings.TryGetProperty("float", out var floatEmbeddings))
            {
                CollectDimensionsFromArray(floatEmbeddings, dimensions);
            }
            else
            {
                CollectDimensionsFromArray(embeddings, dimensions);
            }
        }

        if (element.TryGetProperty("embedding", out var embedding))
        {
            CollectDimensionsFromArray(embedding, dimensions);
        }

        if (element.TryGetProperty("result", out var result))
        {
            CollectEmbeddingDimensions(result, dimensions);
        }

        if (element.TryGetProperty("output", out var output))
        {
            CollectEmbeddingDimensions(output, dimensions);
        }
    }

    private static void CollectDimensionsFromArray(JsonElement array, List<int> dimensions)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var items = array.EnumerateArray().ToList();
        if (items.Count == 0)
        {
            return;
        }

        if (items[0].ValueKind is JsonValueKind.Number)
        {
            dimensions.Add(items.Count);
            return;
        }

        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                dimensions.Add(item.GetArrayLength());
            }
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("embedding", out var embedding))
            {
                CollectDimensionsFromArray(embedding, dimensions);
            }
        }
    }

    private static string KnownSupportedDimensions(string provider, string model)
    {
        var normalizedModel = model.Trim().ToLowerInvariant();
        if (normalizedModel == "text-embedding-3-large")
        {
            return "256,512,1024,3072";
        }

        if (normalizedModel == "text-embedding-3-small")
        {
            return "512,1536";
        }

        if (normalizedModel == "text-embedding-ada-002")
        {
            return "1536";
        }

        if (provider == "cohere")
        {
            return normalizedModel switch
            {
                "embed-v4.0" => "256,512,1024,1536",
                "embed-english-v3.0" => "1024",
                "embed-multilingual-v3.0" => "1024",
                "embed-multilingual-light-v3.0" => "384",
                "embed-english-light-v3.0" => "384",
                _ => ""
            };
        }

        if (provider == "ollama")
        {
            return normalizedModel switch
            {
                "all-minilm" => "384",
                "all-mpnet-base-v2" => "768",
                "nomic-embed-text" => "768",
                "mxbai-embed-large" => "1024",
                "snowflake-arctic-embed" => "1024",
                _ => ""
            };
        }

        return "";
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ExtractChatCompletionText(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var content))
                {
                    var visibleContent = content.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(visibleContent))
                    {
                        return visibleContent;
                    }

                    if (message.TryGetProperty("reasoning_content", out var reasoningContent))
                    {
                        return reasoningContent.GetString() ?? "";
                    }
                }

                if (first.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            return responseText;
        }

        return responseText;
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 800 ? singleLine : $"{singleLine[..800]}...";
    }

    private static List<string> BuildDiagnosticsLogs(string service, CatalogProfile? profile, CatalogModel? model, bool configured)
    {
        var provider = service.Equals("search", StringComparison.OrdinalIgnoreCase)
            ? profile?.Provider
            : profile?.Binding;
        var modelName = model?.Model;
        var target = profile?.BaseUrl;
        var contextWindow = string.IsNullOrWhiteSpace(model?.ContextWindow) ? "unknown" : model.ContextWindow;

        var lines = new List<string>
        {
            $"Preparing {service} diagnostics.",
            "[info] Preparing configuration snapshot.",
            "[config] Using active profile.",
            $"[info] Provider binding: `{provider ?? "not set"}`.",
            $"[info] Request target: {target ?? "not set"}."
        };

        if (!service.Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"[info] Resolved model `{modelName ?? "not set"}`.");
            lines.Add("[info] Token options: {\"max_tokens\": 4096}");
        }

        if (configured)
        {
            lines.Add("[response] Configuration validation passed.");
            if (!service.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("[info] Detecting model context window.");
                lines.Add($"[context_window] Detected context window {contextWindow} tokens.");
            }
            lines.Add($"[completed] {service} test completed successfully.");
            return lines;
        }

        lines.Add("[error] Required provider, endpoint, or model configuration is missing.");
        lines.Add("[completed] Diagnostics completed with errors.");
        return lines;
    }
}