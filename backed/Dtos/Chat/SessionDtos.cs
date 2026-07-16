using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Chat;

public class ChatSessionSummaryDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }
    [JsonPropertyName("message_count")] public int MessageCount { get; set; }
    [JsonPropertyName("last_message")] public string LastMessage { get; set; } = string.Empty;
}

public sealed class ChatSessionDetailDto : ChatSessionSummaryDto
{
    [JsonPropertyName("preferences")] public Dictionary<string, object?> Preferences { get; set; } = [];
    [JsonPropertyName("messages")] public List<ChatSessionMessageDto> Messages { get; set; } = [];
}

public sealed class ChatSessionMessageDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("thinking")] public string? Thinking { get; set; }
    [JsonPropertyName("citations")] public object? Citations { get; set; }
    [JsonPropertyName("metadata")] public object? Metadata { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
}

public sealed class RenameChatSessionRequest
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
}
