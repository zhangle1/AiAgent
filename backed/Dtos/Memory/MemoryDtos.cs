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

public sealed class GenerateMemoryCandidatesRequest
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;
}

public sealed class ApproveMemoryCandidateRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("scope_type")]
    public string? ScopeType { get; set; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("pinned")]
    public bool? IsPinned { get; set; }

    [JsonPropertyName("existing_memory_id")]
    public long? ExistingMemoryId { get; set; }
}

public sealed class RejectMemoryCandidateRequest
{
    [JsonPropertyName("review_note")]
    public string? ReviewNote { get; set; }
}

public sealed class MemoryCandidateDto
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

    [JsonPropertyName("evidence")]
    public object? Evidence { get; set; }

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("source_session_id")]
    public string? SourceSessionId { get; set; }

    [JsonPropertyName("approved_memory_id")]
    public long? ApprovedMemoryId { get; set; }

    [JsonPropertyName("review_note")]
    public string? ReviewNote { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("reviewed_at")]
    public DateTime? ReviewedAt { get; set; }
}

public sealed class MemoryCandidateGenerationResult
{
    [JsonPropertyName("created_count")]
    public int CreatedCount { get; set; }

    [JsonPropertyName("processed_observation_count")]
    public int ProcessedObservationCount { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
