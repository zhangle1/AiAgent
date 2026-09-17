using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class KnowledgeCompilerSettingsDto
{
    [JsonPropertyName("generator")] public string Generator { get; set; } = "codex";
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }
    [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; set; }
    [JsonPropertyName("max_steps")] public int MaxSteps { get; set; } = 48;
    [JsonPropertyName("timeout_minutes")] public int TimeoutMinutes { get; set; } = 20;
}

public sealed class KnowledgeOrganizationDto
{
    [JsonPropertyName("company")] public string? Company { get; set; }
    [JsonPropertyName("project")] public string? Project { get; set; }
}

public sealed class KnowledgePageDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("page_index")] public int PageIndex { get; set; }
    [JsonPropertyName("document_id")] public long? DocumentId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("source_name")] public string? SourceName { get; set; }
    [JsonPropertyName("review_status")] public string? ReviewStatus { get; set; }
    [JsonPropertyName("generator")] public string? Generator { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; set; }
}

public sealed class KnowledgeCompilationJobDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("document_id")] public long? DocumentId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "queued";
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
