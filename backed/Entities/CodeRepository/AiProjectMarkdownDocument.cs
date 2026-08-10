using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

/// <summary>
/// Metadata for a Markdown file held in a project's dedicated AiAgent document directory.
/// The body stays on disk and is never persisted with chat messages.
/// </summary>
[SugarTable("ai_project_markdown_document")]
public sealed class AiProjectMarkdownDocument
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string Id { get; set; } = string.Empty;

    public long ProjectId { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? UploaderId { get; set; }

    [SugarColumn(Length = 256)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Kind { get; set; } = "upload";

    /// <summary>Directory relative to uploads/aiagent-documents. It is never an absolute path.</summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? DirectoryPath { get; set; }

    [SugarColumn(Length = 80)]
    public string StorageName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
