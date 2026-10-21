using AiAgent.Backend.Dtos.Settings;
using AiAgent.Backend.Models.Settings;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiAgent.Backend.Services.Settings;

public sealed class ModelDiscoveryService(IHttpClientFactory clients, IModelCatalogService catalog, ModelMetadataStore metadata)
{
    public async Task<IReadOnlyList<DiscoveredModelDto>> DiscoverAsync(ModelDiscoveryRequest input, CancellationToken ct)
    {
        if (input.Service is not ("llm" or "embedding")) throw new ArgumentException("Model discovery supports LLM and embedding services.");
        var uri = ValidateEndpoint(input.BaseUrl);
        var key = input.ApiKey ?? "";
        if (key is "********" or "")
        {
            var saved = input.Service == "llm" ? catalog.Load().Services.Llm : catalog.Load().Services.Embedding;
            var savedProfile = saved.Profiles.FirstOrDefault(profile => profile.Id == input.ProfileId);
            if (savedProfile is not null && !string.Equals(savedProfile.BaseUrl.TrimEnd('/'), input.BaseUrl.TrimEnd('/'), StringComparison.Ordinal))
                throw new ArgumentException("Enter the API key again after changing the endpoint.");
            key = savedProfile?.ApiKey ?? "";
        }
        var provider = input.Binding.ToLowerInvariant();
        var style = input.ApiStyle switch { "anthropic_messages" => "anthropic", "google_generative_ai" => "google", _ => provider switch { "google" or "gemini" => "google", "anthropic" or "claude" => "anthropic", "ollama" => "ollama", _ => "openai" } };
        var root = uri.AbsoluteUri.TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/messages", "/responses" })
            if (root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { root = root[..^suffix.Length]; break; }
        var candidates = style switch
        {
            "google" => new[] { root.EndsWith("/v1beta") || root.EndsWith("/v1") ? root + "/models" : root + "/v1beta/models" },
            "ollama" => new[] { root.EndsWith("/api") ? root + "/tags" : root + "/api/tags" },
            "anthropic" => new[] { root.EndsWith("/v1") ? root + "/models" : root + "/v1/models" },
            _ => root.EndsWith("/v1") ? new[] { root + "/models" } : new[] { root + "/models", root + "/v1/models" }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = clients.CreateClient("model-discovery");
        foreach (var endpoint in candidates.Distinct())
        {
            var pageUrl = endpoint;
            var found = new Dictionary<string, DiscoveredModelDto>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 0; page < 20; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                ApplyHeaders(request, style, key, input.ExtraHeaders);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (page == 0 && response.StatusCode == HttpStatusCode.NotFound && endpoint != candidates.Last()) break;
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Model discovery returned HTTP {(int)response.StatusCode}. Check the URL, API key and provider permissions.");
                var body = await ReadBoundedAsync(response.Content, 8 * 1024 * 1024, timeout.Token);
                using var document = JsonDocument.Parse(body);
                var rootNode = document.RootElement;
                foreach (var model in ParseModels(rootNode, provider, metadata)) found.TryAdd(model.Model, model);
                if (found.Count > 2000) throw new InvalidDataException("The service returned more than 2000 models. Narrow the model endpoint before importing.");
                string? cursor = null;
                var parameter = "";
                if (rootNode.ValueKind == JsonValueKind.Object)
                {
                    if (style == "google" && rootNode.TryGetProperty("nextPageToken", out var next) && next.ValueKind == JsonValueKind.String)
                    { cursor = next.GetString(); parameter = "pageToken"; }
                    else if (rootNode.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True && rootNode.TryGetProperty("last_id", out var last) && last.ValueKind == JsonValueKind.String)
                    { cursor = last.GetString(); parameter = "after_id"; }
                }
                if (string.IsNullOrEmpty(cursor)) return found.Values.OrderBy(model => model.Model, StringComparer.Ordinal).ToList();
                if (cursor.Length > 2048 || !cursors.Add(cursor) || page == 19) throw new InvalidDataException("The service returned an incomplete or repeating model pagination cursor.");
                pageUrl = endpoint + "?" + parameter + "=" + Uri.EscapeDataString(cursor);
            }
        }
        return [];
    }

    internal static Uri ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP(S) endpoint without credentials, query parameters or fragments.");
        return uri;
    }

    internal static void ApplyHeaders(HttpRequestMessage request, string style, string key, Dictionary<string, string>? extra)
    {
        if (key.Length > 8192 || key.Any(char.IsWhiteSpace)) throw new ArgumentException("The API key contains invalid characters.");
        if (style == "anthropic") request.Headers.Add("anthropic-version", "2023-06-01");
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (style == "anthropic") request.Headers.Add("x-api-key", key);
            else if (style == "google") request.Headers.Add("x-goog-api-key", key);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        foreach (var header in extra ?? [])
        {
            if (header.Key.Contains('\r') || header.Key.Contains('\n') || header.Value.Contains('\r') || header.Value.Contains('\n')) continue;
            if (new[] { "authorization", "proxy-authorization", "host", "cookie", "content-length", "connection", "x-api-key", "api-key", "x-goog-api-key" }.Contains(header.Key.ToLowerInvariant())) continue;
            request.Headers.Remove(header.Key);
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken ct)
    {
        using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + read > maximum) throw new InvalidDataException("The model service response exceeded the size limit.");
            await buffer.WriteAsync(bytes.AsMemory(0, read), ct);
        }
        return buffer.ToArray();
    }

    internal static IReadOnlyList<DiscoveredModelDto> ParseModels(JsonElement root, string provider, ModelMetadataStore metadata)
    {
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("data", out var data) ? data : root.TryGetProperty("models", out var models) ? models : default;
        if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The endpoint did not return a model list.");
        if (rows.GetArrayLength() > 2000) throw new InvalidDataException("The service returned more than 2000 models in one response.");
        var result = new Dictionary<string, DiscoveredModelDto>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray().Take(2000))
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string? Text(params string[] keys) => keys.Select(key => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            int? Number(params string[] keys)
            {
                foreach (var key in keys) if (row.TryGetProperty(key, out var value) && int.TryParse(value.ToString(), out var parsed) && parsed > 0) return parsed;
                return null;
            }
            bool? Flag(params string[] keys)
            {
                foreach (var key in keys) if (row.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
                return null;
            }
            var id = Text("id", "model", "name", "slug");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256) continue;
            if (id.StartsWith("models/")) id = id[7..];
            var info = metadata.Find(provider, id);
            var endpointContext = Number("context_window", "context_length", "inputTokenLimit", "contextWindow");
            var context = endpointContext ?? Positive(info?["limit"]?["context"]);
            var endpointOutput = Number("max_output_tokens", "max_tokens", "outputTokenLimit");
            var output = endpointOutput ?? Positive(info?["limit"]?["output"]);
            var name = Text("display_name", "displayName") ?? info?["name"]?.GetValue<string>() ?? id;
            result.TryAdd(id, new DiscoveredModelDto
            {
                Model = id, Name = name.Length <= 128 ? name : name[..128],
                ContextWindow = context,
                MaxOutputTokens = output,
                MaxOutputTokensSource = endpointOutput.HasValue ? "endpoint" : output.HasValue ? "model.json" : null,
                SupportsImages = Flag("supports_images", "supports_image_input", "supports_vision") ?? (info?["modalities"]?["input"] is JsonArray inputs ? inputs.Any(item => item?.ToString() == "image") : null),
                SupportsTools = Flag("tool_call", "supports_tools", "supports_tool_calling") ?? info?["tool_call"]?.GetValue<bool>(),
                ContextWindowSource = endpointContext.HasValue ? "endpoint" : context.HasValue ? "model.json" : null
            });
        }
        return result.Values.OrderBy(item => item.Model, StringComparer.Ordinal).ToList();
    }

    private static int? Positive(JsonNode? node) => int.TryParse(node?.ToString(), out var number) && number > 0 ? number : null;
}
