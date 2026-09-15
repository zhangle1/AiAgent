using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_parsed_document")]
public sealed class AiKnowledgeParsedDocument
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? KnowledgeBaseId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DocumentId { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? SourceHash { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Parser { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? ParserVersion { get; set; }

    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? ContentPath { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? LocatorJson { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? CharacterCount { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }
}
