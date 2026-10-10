using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class KnowledgeAgentHistoryDto
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public sealed class KnowledgeAgentChatRequest
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "viking://resources/";
    [JsonPropertyName("model_id")] public string ModelId { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("history")] public List<KnowledgeAgentHistoryDto> History { get; set; } = [];
}

public sealed class KnowledgeAgentModelDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    [JsonPropertyName("context_window")] public int ContextWindow { get; set; }
    [JsonPropertyName("native_tools")] public bool NativeTools { get; set; }
    [JsonPropertyName("is_default")] public bool IsDefault { get; set; }
}

public sealed class KnowledgeAgentCommandRequest
{
    [JsonPropertyName("command")] public string Command { get; set; } = "ls";
    [JsonPropertyName("uri")] public string Uri { get; set; } = "viking://resources/";
    [JsonPropertyName("target_uri")] public string? TargetUri { get; set; }
    [JsonPropertyName("query")] public string? Query { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; } = 40;
}

public sealed class KnowledgeAgentCompileRequest
{
    [JsonPropertyName("uris")] public List<string> Uris { get; set; } = [];
    [JsonPropertyName("configuration")] public KnowledgeCompilerSettingsDto Configuration { get; set; } = new();
}
