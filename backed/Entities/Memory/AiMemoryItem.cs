using SqlSugar;

namespace AiAgent.Backend.Entities.Memory;

/// <summary>
/// 用户可审阅的长期记忆。M1 仅支持个人全局和项目个人范围；Git 同步与共享范围后续扩展。
/// </summary>
[SugarTable("ai_memory_item")]
public sealed class AiMemoryItem
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

    [SugarColumn(Length = 16)]
    public string Status { get; set; } = "active";

    public bool IsPinned { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SourceSessionId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? SupersedesMemoryId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ContentHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public bool IsDeleted { get; set; }
}
