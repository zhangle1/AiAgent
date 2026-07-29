using SqlSugar;

namespace AiAgent.Backend.Entities.Memory;

/// <summary>
/// 由原始会话观察提炼出的待审核记忆。候选在人工确认前绝不会参与提示词注入。
/// </summary>
[SugarTable("ai_memory_candidate")]
public sealed class AiMemoryCandidate
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public long? CodeProjectId { get; set; }

    [SugarColumn(Length = 32)]
    public string ScopeType { get; set; } = "project_user";

    [SugarColumn(Length = 16)]
    public string Tier { get; set; } = "semantic";

    [SugarColumn(Length = 16)]
    public string Kind { get; set; } = "fact";

    [SugarColumn(Length = 256)]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string EvidenceJson { get; set; } = "{}";

    public int Confidence { get; set; }

    [SugarColumn(Length = 16)]
    public string Status { get; set; } = "pending";

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SourceSessionId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ApprovedMemoryId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ContentHash { get; set; }

    [SugarColumn(Length = 512, IsNullable = true)]
    public string? ReviewNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public bool IsDeleted { get; set; }
}
