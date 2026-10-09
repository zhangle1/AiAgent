using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

/// <summary>A logical resource directory; never a server filesystem path or an authorization boundary.</summary>
[SugarTable("ai_knowledge_directory")]
public sealed class AiKnowledgeDirectory
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(Length = 1024, IsNullable = true)] public string? Uri { get; set; }
    [SugarColumn(Length = 1024, IsNullable = true)] public string? ParentUri { get; set; }
    [SugarColumn(Length = 180, IsNullable = true)] public string? Name { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? CreatedAt { get; set; }
}
