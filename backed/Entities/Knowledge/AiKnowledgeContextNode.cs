using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

/// <summary>OpenViking-style URI tree node for a knowledge base context.</summary>
[SugarTable("ai_knowledge_context_node")]
public sealed class AiKnowledgeContextNode
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public long KnowledgeBaseId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ParentId { get; set; }

    [SugarColumn(Length = 1024)]
    public string Uri { get; set; } = string.Empty;

    [SugarColumn(Length = 512)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string NodeType { get; set; } = "directory";

    public int Layer { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? SourceDocumentId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ArtifactId { get; set; }

    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? ContentPath { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ContentHash { get; set; }

    [SugarColumn(Length = 32)]
    public string Status { get; set; } = "ready";

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
