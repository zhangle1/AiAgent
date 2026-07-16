using AiAgent.Backend.Models.Settings;
using AiAgent.Backend.Services.Settings;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat.Llm;

/// <summary>
/// LLM 聊天客户端，封装 OpenAI-compatible HTTP 调用和模型配置解析。
/// </summary>
public interface ILlmChatClient
{
    /// <summary>
    /// 调用当前配置的 LLM 完成一次聊天。
    /// </summary>
    Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken);

    /// <summary>
    /// 流式调用当前配置的 LLM，逐块返回模型输出。
    /// </summary>
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken);
}

/// <summary>
/// 默认 LLM 聊天客户端。
/// </summary>
public sealed class LlmChatClient : ILlmChatClient
{
    private const string RedactedSecret = "********";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IModelCatalogService _catalogService;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// 初始化 LLM 客户端。
    /// </summary>
    public LlmChatClient(IModelCatalogService catalogService, IHttpClientFactory httpClientFactory)
    {
        _catalogService = catalogService;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 执行一次非流式 OpenAI-compatible chat/completions 调用。
    /// </summary>
    public async Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken)
    {
        var answer = new StringBuilder();
        string? resolvedModelId = null;
        string? resolvedModel = null;
        string? provider = null;
        await foreach (var chunk in StreamAsync(messages, modelId, cancellationToken))
        {
            resolvedModelId ??= chunk.ModelId;
            resolvedModel ??= chunk.Model;
            provider ??= chunk.Provider;
            answer.Append(chunk.Content);
        }

        var text = answer.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("LLM provider returned an empty response.");
        }

        return new LlmChatResult
        {
            Text = text,
            ModelId = resolvedModelId,
            Model = resolvedModel,
            Provider = provider
        };
    }

    /// <summary>
    /// 执行 OpenAI-compatible chat/completions 流式调用。
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        string? modelId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var selection = ResolveLlm(modelId);
        using var httpRequest = BuildRequest(selection, messages, stream: true);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"LLM provider request timed out. Endpoint={httpRequest.RequestUri}, Model={selection.Model.Model}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"LLM provider request failed. Endpoint={httpRequest.RequestUri}, Model={selection.Model.Model}, Provider={selection.Profile.Binding ?? "unknown"}. {ex.Message}",
                ex);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseToDispose = response;
        if (!response.IsSuccessStatusCode)
        {
            using var readerForError = new StreamReader(stream, Encoding.UTF8);
            var responseText = await readerForError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"LLM provider returned HTTP {(int)response.StatusCode}: {TrimForLog(responseText)}");
        }

        yield return new LlmStreamChunk
        {
            ModelId = selection.Model.Id,
            Model = selection.Model.Model,
            Provider = selection.Profile.Binding
        };

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                yield break;
            }

            var chunk = ParseStreamData(data);
            if (chunk is null)
            {
                continue;
            }

            chunk.ModelId = selection.Model.Id;
            chunk.Model = selection.Model.Model;
            chunk.Provider = selection.Profile.Binding;
            yield return chunk;
        }
    }

    private HttpRequestMessage BuildRequest(LlmSelection selection, IReadOnlyList<LlmMessage> messages, bool stream)
    {
        var endpoint = BuildChatCompletionsEndpoint(selection.Profile.BaseUrl);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("LLM endpoint is missing.");
        }

        if (string.IsNullOrWhiteSpace(selection.Model.Model))
        {
            throw new InvalidOperationException("LLM model is missing.");
        }

        if (LlmProviderRequiresApiKey(selection.Profile.Binding)
            && (string.IsNullOrWhiteSpace(selection.Profile.ApiKey) || selection.Profile.ApiKey == RedactedSecret))
        {
            throw new InvalidOperationException("LLM API key is missing.");
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(selection.Profile.ApiKey) && selection.Profile.ApiKey != RedactedSecret)
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", selection.Profile.ApiKey);
        }

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var header in (selection.Profile.ExtraHeaders ?? new Dictionary<string, string>()).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
        {
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var body = new
        {
            model = selection.Model.Model,
            messages = messages.Select(x => new { role = x.Role, content = x.Content }).ToArray(),
            temperature = 0.2,
            max_tokens = 1600,
            stream
        };
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private LlmSelection ResolveLlm(string? modelId)
    {
        var catalog = _catalogService.Load(redactSecrets: false);
        var service = catalog.Services.Llm;
        var profile = service.Profiles.FirstOrDefault(x => x.Id == service.ActiveProfileId)
            ?? service.Profiles.FirstOrDefault();
        if (profile is null)
        {
            throw new InvalidOperationException("LLM profile is not configured.");
        }

        var model = !string.IsNullOrWhiteSpace(modelId)
            ? profile.Models.FirstOrDefault(x => x.Id == modelId)
            : null;
        model ??= profile.Models.FirstOrDefault(x => x.Id == service.ActiveModelId)
            ?? profile.Models.FirstOrDefault();
        if (model is null)
        {
            throw new InvalidOperationException("LLM model is not configured.");
        }

        return new LlmSelection(profile, model);
    }

    private static string BuildChatCompletionsEndpoint(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/chat/completions";
    }

    private static bool LlmProviderRequiresApiKey(string? provider)
    {
        var normalized = (provider ?? string.Empty).Trim().Replace("-", "_").ToLowerInvariant();
        return normalized is not ("ollama" or "lm_studio" or "vllm");
    }

    private static string ExtractChatCompletionText(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(responseText);
        if (document.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
            {
                var visibleContent = content.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(visibleContent))
                {
                    return visibleContent;
                }

                if (message.TryGetProperty("reasoning_content", out var reasoningContent))
                {
                    return reasoningContent.GetString() ?? string.Empty;
                }
            }

            if (first.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static LlmStreamChunk? ParseStreamData(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var first = choices[0];
            var chunk = new LlmStreamChunk
            {
                FinishReason = first.TryGetProperty("finish_reason", out var finishReason) && finishReason.ValueKind == JsonValueKind.String
                    ? finishReason.GetString()
                    : null
            };

            if (first.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    chunk.Content = content.GetString() ?? string.Empty;
                }

                if (delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                {
                    chunk.ReasoningContent = reasoning.GetString() ?? string.Empty;
                }
            }

            return string.IsNullOrEmpty(chunk.Content)
                && string.IsNullOrEmpty(chunk.ReasoningContent)
                && string.IsNullOrEmpty(chunk.FinishReason)
                ? null
                : chunk;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 800 ? value : value[..800] + "...";
    }

    private sealed record LlmSelection(CatalogProfile Profile, CatalogModel Model);
}

/// <summary>
/// 发送给 LLM 的消息。
/// </summary>
public sealed class LlmMessage
{
    /// <summary>
    /// 消息角色，例如 system、user、assistant。
    /// </summary>
    public string Role { get; set; } = "user";

    /// <summary>
    /// 消息内容。
    /// </summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// LLM 返回结果。
/// </summary>
public sealed class LlmChatResult
{
    /// <summary>
    /// 模型生成文本。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 模型配置 Id。
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Provider 模型名称。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Provider 类型。
    /// </summary>
    public string? Provider { get; set; }
}

/// <summary>
/// LLM 流式输出块。
/// </summary>
public sealed class LlmStreamChunk
{
    /// <summary>
    /// 可见回答增量。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 推理增量，部分 provider 会通过 reasoning_content 输出。
    /// </summary>
    public string ReasoningContent { get; set; } = string.Empty;

    /// <summary>
    /// 结束原因。
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// 模型配置 Id。
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Provider 模型名称。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Provider 类型。
    /// </summary>
    public string? Provider { get; set; }
}