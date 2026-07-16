using AiAgent.Backend.Services.Chat.Llm;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat.Agentic;

/// <summary>
/// 单次模型调用，对齐 DeepTutor 的 labeled_step.py。第一版先承载一次非流式 LLM 调用，后续可扩展标签协议和工具调用解析。
/// </summary>
public interface ILabeledStepRunner
{
    /// <summary>
    /// 运行一次 LLM step。
    /// </summary>
    Task<LabeledStepResult> RunAsync(AgentContext context, IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// 运行一次流式 LLM step，并根据 label 决定内容输出通道。
    /// </summary>
    Task<LabeledStepResult> RunStreamingAsync(
        AgentContext context,
        IReadOnlyList<LlmMessage> messages,
        AgentStreamEventHandler? onEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// 默认单步模型调用器。
/// </summary>
public sealed class LabeledStepRunner : ILabeledStepRunner
{
    private const int LabelProbeMaxChars = 1024;

    private readonly ILlmChatClient _llmChatClient;

    /// <summary>
    /// 初始化单步模型调用器。
    /// </summary>
    public LabeledStepRunner(ILlmChatClient llmChatClient)
    {
        _llmChatClient = llmChatClient;
    }

    /// <summary>
    /// 调用 LLM 并将结果标记为 FINISH。
    /// </summary>
    public async Task<LabeledStepResult> RunAsync(AgentContext context, IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken)
    {
        var result = await _llmChatClient.CompleteAsync(messages, context.ModelId, cancellationToken);
        return new LabeledStepResult
        {
            Label = AgentLabels.Finish,
            Text = result.Text,
            ModelId = result.ModelId,
            Model = result.Model
        };
    }

    /// <summary>
    /// 流式调用 LLM，解析开头 label，并把 FINISH 输出到 content，THINK 输出到 thinking。
    /// </summary>
    public async Task<LabeledStepResult> RunStreamingAsync(
        AgentContext context,
        IReadOnlyList<LlmMessage> messages,
        AgentStreamEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        context.Stats.LlmCalls++;
        context.Stats.PromptTokens += EstimateTokens(string.Join("\n", messages.Select(x => x.Content)));
        var label = string.Empty;
        var labelBuffer = string.Empty;
        var content = new StringBuilder();
        string? modelId = null;
        string? model = null;

        await foreach (var chunk in _llmChatClient.StreamAsync(messages, context.ModelId, cancellationToken))
        {
            modelId ??= chunk.ModelId;
            model ??= chunk.Model;

            if (!string.IsNullOrWhiteSpace(chunk.ReasoningContent))
            {
                context.Stats.CompletionTokens += EstimateTokens(chunk.ReasoningContent);
                await EmitAsync(onEvent, new AgentStreamEvent
                {
                    Type = "thinking",
                    Label = AgentLabels.Think,
                    Content = chunk.ReasoningContent,
                    ModelId = modelId,
                    Model = model,
                    Metadata = context.Stats.ToMetadata()
                }, cancellationToken);
            }

            if (string.IsNullOrEmpty(chunk.Content))
            {
                continue;
            }

            var visible = chunk.Content;
            if (string.IsNullOrWhiteSpace(label))
            {
                labelBuffer += visible;
                var resolved = TryResolveLabel(labelBuffer);
                if (resolved is null)
                {
                    if (labelBuffer.Length < LabelProbeMaxChars)
                    {
                        continue;
                    }

                    label = AgentLabels.Think;
                    await EmitLabelAsync(onEvent, label, cancellationToken);
                    visible = labelBuffer;
                    labelBuffer = string.Empty;
                }
                else
                {
                    label = resolved.Value.Label;
                    if (!string.IsNullOrWhiteSpace(resolved.Value.Preamble))
                    {
                        await EmitAsync(onEvent, new AgentStreamEvent
                        {
                            Type = "thinking",
                            Label = AgentLabels.Think,
                            Content = resolved.Value.Preamble,
                            ModelId = modelId,
                            Model = model,
                            Metadata = context.Stats.ToMetadata()
                        }, cancellationToken);
                    }

                    await EmitLabelAsync(onEvent, label, cancellationToken);
                    visible = resolved.Value.Remainder;
                    labelBuffer = string.Empty;
                }
            }

            if (string.IsNullOrEmpty(visible))
            {
                continue;
            }

            content.Append(visible);
            context.Stats.CompletionTokens += EstimateTokens(visible);
            await EmitAsync(onEvent, new AgentStreamEvent
            {
                Type = OutputTypeForLabel(label),
                Label = label,
                Content = visible,
                ModelId = modelId,
                Model = model,
                Metadata = context.Stats.ToMetadata()
            }, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = AgentLabels.Think;
            await EmitLabelAsync(onEvent, label, cancellationToken);
            if (!string.IsNullOrEmpty(labelBuffer))
            {
                content.Append(labelBuffer);
                await EmitAsync(onEvent, new AgentStreamEvent
                {
                    Type = "thinking",
                    Label = label,
                    Content = labelBuffer,
                    ModelId = modelId,
                    Model = model,
                    Metadata = context.Stats.ToMetadata()
                }, cancellationToken);
            }
        }

        return new LabeledStepResult
        {
            Label = label,
            Text = content.ToString().Trim(),
            ModelId = modelId,
            Model = model,
            ToolCalls = label == AgentLabels.Tool ? ParseToolCalls(content.ToString()) : []
        };
    }

    private static async Task EmitLabelAsync(AgentStreamEventHandler? onEvent, string label, CancellationToken cancellationToken)
    {
        await EmitAsync(onEvent, new AgentStreamEvent
        {
            Type = "label",
            Label = label,
            Content = label
        }, cancellationToken);
    }

    private static Task EmitAsync(AgentStreamEventHandler? onEvent, AgentStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        return onEvent is null ? Task.CompletedTask : onEvent(streamEvent, cancellationToken);
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var chars = text.Trim().Length;
        return Math.Max(1, (int)Math.Ceiling(chars / 3.6d));
    }

    private static string OutputTypeForLabel(string label)
    {
        return label switch
        {
            AgentLabels.Think => "thinking",
            AgentLabels.Tool => "tool_request",
            _ => "content"
        };
    }

    private static (string Label, string Remainder, string Preamble)? TryResolveLabel(string buffer)
    {
        return TryResolveLabelAt(buffer, 0) ?? TryResolveLabelOnLaterLine(buffer);
    }

    private static (string Label, string Remainder, string Preamble)? TryResolveLabelAt(string buffer, int start)
    {
        var segment = buffer[start..];
        var trimmedStart = segment.TrimStart();
        var leadingWhitespace = segment.Length - trimmedStart.Length;
        foreach (var label in new[] { AgentLabels.Finish, AgentLabels.Tool, AgentLabels.Think })
        {
            foreach (var prefix in new[] { $"```{label}```", $"`{label}`", label })
            {
                if (!trimmedStart.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var consumed = start + leadingWhitespace + prefix.Length;
                var remainder = buffer.Length > consumed ? buffer[consumed..] : string.Empty;
                remainder = remainder.TrimStart('\r', '\n', ' ', ':');
                var preamble = start > 0 ? buffer[..start].Trim() : string.Empty;
                return (label, remainder, preamble);
            }
        }

        return null;
    }

    private static (string Label, string Remainder, string Preamble)? TryResolveLabelOnLaterLine(string buffer)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != '\n')
            {
                continue;
            }

            var lineStart = index + 1;
            if (lineStart >= buffer.Length)
            {
                continue;
            }

            var resolved = TryResolveLabelAt(buffer, lineStart);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static List<ToolCall> ParseToolCalls(string text)
    {
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tool_calls", out var toolCalls))
            {
                root = toolCalls;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                return ParseToolCallObject(root) is { } call ? [call] : [];
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var calls = new List<ToolCall>();
            foreach (var item in root.EnumerateArray())
            {
                var call = ParseToolCallObject(item);
                if (call is not null)
                {
                    calls.Add(call);
                }
            }

            return calls;
        }
        catch
        {
            return [];
        }
    }

    private static ToolCall? ParseToolCallObject(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = JsonString(item, "name") ?? JsonString(item, "tool") ?? JsonString(item, "tool_name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("arguments", out var args) || item.TryGetProperty("input", out args))
        {
            arguments = JsonObject(args);
        }

        return new ToolCall
        {
            Name = name.Trim(),
            Arguments = arguments
        };
    }

    private static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = trimmed.IndexOf('\n', fenceStart);
            var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (contentStart >= 0 && fenceEnd > contentStart)
            {
                trimmed = trimmed[(contentStart + 1)..fenceEnd].Trim();
            }
        }

        var arrayStart = trimmed.IndexOf('[', StringComparison.Ordinal);
        var objectStart = trimmed.IndexOf('{', StringComparison.Ordinal);
        var start = arrayStart >= 0 && objectStart >= 0 ? Math.Min(arrayStart, objectStart) : Math.Max(arrayStart, objectStart);
        if (start > 0)
        {
            trimmed = trimmed[start..];
        }

        return trimmed;
    }

    private static string? JsonString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static Dictionary<string, object?> JsonObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = JsonValue(property.Value);
        }

        return values;
    }

    private static object? JsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => JsonObject(value),
            JsonValueKind.Array => value.EnumerateArray().Select(JsonValue).ToList(),
            _ => null
        };
    }
}

/// <summary>
/// Labeled step 返回值。
/// </summary>
public sealed class LabeledStepResult
{
    /// <summary>
    /// 本轮模型输出标签，第一版固定为 FINISH。
    /// </summary>
    public string Label { get; set; } = AgentLabels.Finish;

    /// <summary>
    /// 模型输出文本。
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
    /// 预留：模型主动发起的工具调用。
    /// </summary>
    public List<ToolCall> ToolCalls { get; set; } = [];
}

/// <summary>
/// Agent 标签常量，后续接入 THINK、TOOL、FINISH 标签协议。
/// </summary>
public static class AgentLabels
{
    /// <summary>
    /// 最终回答标签。
    /// </summary>
    public const string Finish = "FINISH";

    /// <summary>
    /// 工具调用标签。
    /// </summary>
    public const string Tool = "TOOL";

    /// <summary>
    /// 思考标签。
    /// </summary>
    public const string Think = "THINK";
}