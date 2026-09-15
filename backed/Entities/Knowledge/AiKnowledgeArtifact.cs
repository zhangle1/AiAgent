using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_artifact")]
public sealed class AiKnowledgeArtifact
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? KnowledgeBaseId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DocumentId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ParsedDocumentId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ArtifactType { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? GeneratorType { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? Provider { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? Model { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? PromptVersion { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? Content { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? ReviewStatus { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? EvidenceJson { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }
}
