using AiAgent.Backend.Models.Settings;
using AiAgent.Backend.Services.Chat.Agentic;
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
    /// <summary>Returns the safe provider-neutral limits used to construct an agent context.</summary>
    LlmModelCapabilities GetCapabilities(string? modelId) => LlmModelCapabilities.Default;

    /// <summary>
    /// 调用当前配置的 LLM 完成一次聊天。
    /// </summary>
    Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken);

    /// <summary>
    /// 流式调用当前配置的 LLM，逐块返回模型输出。
    /// </summary>
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Streams an OpenAI-compatible native tool-calling response. Implementations
    /// that do not support provider-native tools can keep the default behaviour;
    /// the runtime will not silently fall back to text protocol on this path.
    /// </summary>
    IAsyncEnumerable<LlmStreamChunk> StreamWithToolsAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? modelId,
        CancellationToken cancellationToken) => throw new NotSupportedException("The configured LLM client does not support native tool calling.");
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

    public LlmModelCapabilities GetCapabilities(string? modelId)
    {
        var selection = ResolveLlm(modelId);
        return new LlmModelCapabilities(
            selection.Model.Id,
            selection.Model.Model,
            ParseContextWindow(selection.Model.ContextWindow),
            1600,
            selection.Model.SupportsNativeToolCalling == true);
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
    public IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        string? modelId,
        CancellationToken cancellationToken) => StreamCoreAsync(messages, modelId, null, cancellationToken);

    /// <summary>Streams a provider-native tool-call response using the same model selection and HTTP safeguards.</summary>
    public IAsyncEnumerable<LlmStreamChunk> StreamWithToolsAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? modelId,
        CancellationToken cancellationToken) => StreamCoreAsync(messages, modelId, tools, cancellationToken);

    private async IAsyncEnumerable<LlmStreamChunk> StreamCoreAsync(
        IReadOnlyList<LlmMessage> messages,
        string? modelId,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var selection = ResolveLlm(modelId);
        using var httpRequest = BuildRequest(selection, messages, tools, stream: true);
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

    private HttpRequestMessage BuildRequest(LlmSelection selection, IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition>? tools, bool stream)
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

        var body = new Dictionary<string, object?>
        {
            ["model"] = selection.Model.Model,
            // OpenAI-compatible providers reject an orphaned role=tool message
            // (and assistant tool_calls without every corresponding result). Keep
            // this invariant at the provider boundary so compaction, cancellation,
            // and a future persisted history format cannot poison the next step.
            ["messages"] = NormalizeToolMessageSequence(messages).Select(ToWireMessage).ToArray(),
            ["temperature"] = 0.2,
            ["max_tokens"] = 1600,
            ["stream"] = stream
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools.Select(ToWireTool).ToArray();
            body["tool_choice"] = "auto";
        }
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static Dictionary<string, object?> ToWireMessage(LlmMessage message)
    {
        var wire = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.ToolCalls.Count > 0 && string.IsNullOrEmpty(message.Content) ? null : message.Content
        };
        if (!string.IsNullOrWhiteSpace(message.ToolCallId)) wire["tool_call_id"] = message.ToolCallId;
        if (message.ToolCalls.Count > 0)
        {
            wire["tool_calls"] = message.ToolCalls.Select(call => new
            {
                id = call.Id,
                type = "function",
                function = new { name = call.Name, arguments = call.ArgumentsJson }
            }).ToArray();
        }
        return wire;
    }

    /// <summary>
    /// A provider can only receive complete assistant-tool-call/tool-result groups.
    /// It is intentionally non-mutating: the runtime ledger remains the source of
    /// truth, while this method merely makes an outbound provider payload valid.
    /// </summary>
    internal static IReadOnlyList<LlmMessage> NormalizeToolMessageSequence(IReadOnlyList<LlmMessage> messages)
    {
        var normalized = new List<LlmMessage>();
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                // A tool result may only follow a retained assistant tool-call group.
                continue;
            }

            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) || message.ToolCalls.Count == 0)
            {
                normalized.Add(message);
                continue;
            }

            var expectedIds = message.ToolCalls.Select(call => call.Id).ToList();
            var cursor = index + 1;
            var resultByCallId = new Dictionary<string, LlmMessage>(StringComparer.Ordinal);
            while (cursor < messages.Count && string.Equals(messages[cursor].Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var toolResult = messages[cursor];
                if (!string.IsNullOrWhiteSpace(toolResult.ToolCallId) && !resultByCallId.ContainsKey(toolResult.ToolCallId))
                    resultByCallId[toolResult.ToolCallId] = toolResult;
                cursor++;
            }

            var hasCompletePair = expectedIds.Count > 0
                && expectedIds.All(id => !string.IsNullOrWhiteSpace(id))
                && expectedIds.Distinct(StringComparer.Ordinal).Count() == expectedIds.Count
                && expectedIds.All(resultByCallId.ContainsKey);
            if (hasCompletePair)
            {
                normalized.Add(message);
                foreach (var callId in expectedIds)
                    normalized.Add(resultByCallId[callId]);
            }

            // Whether retained or discarded, this assistant block owns all of its
            // immediately following tool messages. Never let a partial remainder
            // become an orphan later in the wire transcript.
            index = cursor - 1;
        }
        return normalized;
    }

    private static object ToWireTool(ToolDefinition tool)
    {
        var properties = tool.Parameters.ToDictionary(parameter => parameter.Name, parameter => (object)new
        {
            type = parameter.Type,
            description = parameter.Description
        });
        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = new
                {
                    type = "object",
                    properties,
                    required = tool.Parameters.Where(parameter => parameter.Required).Select(parameter => parameter.Name).ToArray(),
                    additionalProperties = false
                }
            }
        };
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

    private static int ParseContextWindow(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return LlmModelCapabilities.Default.ContextWindowTokens;
        var normalized = value.Trim().ToLowerInvariant().Replace(",", string.Empty);
        var multiplier = normalized.EndsWith("k", StringComparison.Ordinal) ? 1000 : 1;
        if (multiplier > 1) normalized = normalized[..^1];
        return double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp((int)Math.Round(parsed * multiplier), 2048, 2_000_000)
            : LlmModelCapabilities.Default.ContextWindowTokens;
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

    internal static LlmStreamChunk? ParseStreamData(string data)
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

                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.TryGetProperty("function", out var functionValue) ? functionValue : default;
                        chunk.ToolCallDeltas.Add(new LlmToolCallDelta
                        {
                            Index = toolCall.TryGetProperty("index", out var index) && index.TryGetInt32(out var parsedIndex) ? parsedIndex : 0,
                            Id = toolCall.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                            Name = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
                            ArgumentsDelta = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String ? arguments.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }
            }

            return string.IsNullOrEmpty(chunk.Content)
                && string.IsNullOrEmpty(chunk.ReasoningContent)
                && chunk.ToolCallDeltas.Count == 0
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

    /// <summary>Assistant tool calls to be paired with following tool-result messages.</summary>
    public List<LlmToolCall> ToolCalls { get; set; } = [];

    /// <summary>Required when Role is tool; references the provider call id.</summary>
    public string? ToolCallId { get; set; }
}

/// <summary>Model-window data that is safe to use for runtime budgeting.</summary>
public sealed record LlmModelCapabilities(
    string? ModelId,
    string? Model,
    int ContextWindowTokens,
    int OutputReserveTokens,
    bool SupportsNativeToolCalling)
{
    public static readonly LlmModelCapabilities Default = new(null, null, 16_000, 1_600, false);
}

public sealed class LlmToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
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

    /// <summary>Incremental native function-call fields, keyed by provider stream index.</summary>
    public List<LlmToolCallDelta> ToolCallDeltas { get; set; } = [];
}

public sealed class LlmToolCallDelta
{
    public int Index { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string ArgumentsDelta { get; set; } = string.Empty;
}
