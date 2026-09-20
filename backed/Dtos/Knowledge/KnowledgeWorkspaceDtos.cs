using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class KnowledgeOfficePreviewDto
{
    [JsonPropertyName("sections")] public List<KnowledgeOfficePreviewSectionDto> Sections { get; set; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
}
public sealed class KnowledgeOfficePreviewSectionDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("html")] public string Html { get; set; } = "";
}

public sealed class KnowledgeCompilerSettingsDto
{
    [JsonPropertyName("retrieval_mode")] public string RetrievalMode { get; set; } = "wiki";
    [JsonPropertyName("generator")] public string Generator { get; set; } = "llm_api";
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }
    [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; set; }
    [JsonPropertyName("max_steps")] public int MaxSteps { get; set; } = 48;
    [JsonPropertyName("timeout_minutes")] public int TimeoutMinutes { get; set; } = 20;
}

public sealed class KnowledgeChainCheckStepDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "success";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
}

public sealed class KnowledgeChainCheckResultDto
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("steps")] public List<KnowledgeChainCheckStepDto> Steps { get; set; } = [];
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
    [JsonPropertyName("knowledge_base_name")] public string KnowledgeBaseName { get; set; } = "";
    [JsonPropertyName("document_name")] public string? DocumentName { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("started_at")] public DateTime? StartedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTime? FinishedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("document_id")] public long? DocumentId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "queued";
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
