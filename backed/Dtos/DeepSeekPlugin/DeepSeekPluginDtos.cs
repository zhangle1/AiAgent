using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.DeepSeekPlugin;

public sealed class PluginChatCompletionRequest
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("messages")] public List<PluginChatMessage> Messages { get; set; } = [];
    [JsonPropertyName("tools")] public List<JsonElement> Tools { get; set; } = [];
    [JsonPropertyName("stream")] public bool Stream { get; set; } = true;
}
public sealed class PluginChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public JsonElement Content { get; set; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    [JsonPropertyName("tool_calls")] public List<PluginToolCall> ToolCalls { get; set; } = [];
}
public sealed class PluginToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("function")] public PluginToolFunction Function { get; set; } = new();
}
public sealed class PluginToolFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
}
public sealed class PluginCodexDelegateRequest
{
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
    [JsonPropertyName("context")] public string? Context { get; set; }
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }
    [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; set; }
}
