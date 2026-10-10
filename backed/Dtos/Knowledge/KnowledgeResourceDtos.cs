using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class KnowledgeResourceNodeDto
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
    [JsonPropertyName("parent_uri")] public string? ParentUri { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "directory";
    [JsonPropertyName("status")] public string Status { get; set; } = "ready";
    [JsonPropertyName("document_id")] public long? DocumentId { get; set; }
    [JsonPropertyName("knowledge_base_name")] public string? KnowledgeBaseName { get; set; }
    [JsonPropertyName("extension")] public string? Extension { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
}

public sealed class KnowledgeResourceReadDto
{
    [JsonPropertyName("abstract_content")] public string? AbstractContent { get; set; }
    [JsonPropertyName("overview_content")] public string? OverviewContent { get; set; }
    [JsonPropertyName("semantic_generated_at")] public DateTime? SemanticGeneratedAt { get; set; }
    [JsonPropertyName("semantic_status")] public string SemanticStatus { get; set; } = "missing_or_stale";
    [JsonPropertyName("node")] public KnowledgeResourceNodeDto Node { get; set; } = new();
    [JsonPropertyName("source_text")] public string? SourceText { get; set; }
    [JsonPropertyName("parsed_content")] public string? ParsedContent { get; set; }
    [JsonPropertyName("semantic_content")] public string? SemanticContent { get; set; }
    [JsonPropertyName("parser")] public string? Parser { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("children")] public List<KnowledgeResourceNodeDto> Children { get; set; } = [];
}

public sealed class KnowledgeResourceProcessingDto
{
    [JsonPropertyName("node")] public KnowledgeResourceNodeDto Node { get; set; } = new();
    [JsonPropertyName("task_id")] public long? TaskId { get; set; }
    [JsonPropertyName("task_status")] public string? TaskStatus { get; set; }
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("progress")] public int? Progress { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}

public sealed class KnowledgeDirectoryRequest
{
    [JsonPropertyName("parent_uri")] public string ParentUri { get; set; } = "viking://resources/";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}
public sealed class KnowledgeResourceRequest
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "viking://resources/";
    [JsonPropertyName("question")] public string? Question { get; set; }
}
public sealed class KnowledgeResourceUploadRequest
{
    public string Uri { get; set; } = "viking://resources/";
    public bool Process { get; set; } = true;
    public List<IFormFile> Files { get; set; } = [];
}
public sealed class KnowledgeResourceImportDto
{
    [JsonPropertyName("items")] public List<KnowledgeDocumentImportItemDto> Items { get; set; } = [];
    [JsonPropertyName("tasks")] public List<KnowledgeCompilationJobDto> Tasks { get; set; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
}
public sealed class KnowledgeResourceAnswerDto
{
    [JsonPropertyName("answer")] public string Answer { get; set; } = "";
    [JsonPropertyName("sources")] public List<string> Sources { get; set; } = [];
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
}
