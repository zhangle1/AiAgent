using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Memory;

public sealed class CreateMemoryItemRequest
{
    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("scope_type")]
    public string ScopeType { get; set; } = "project_user";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "semantic";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "fact";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("pinned")]
    public bool IsPinned { get; set; }

    [JsonPropertyName("source_session_id")]
    public string? SourceSessionId { get; set; }
}

public sealed class MemoryItemDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("scope_type")]
    public string ScopeType { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("pinned")]
    public bool IsPinned { get; set; }

    [JsonPropertyName("source_session_id")]
    public string? SourceSessionId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
