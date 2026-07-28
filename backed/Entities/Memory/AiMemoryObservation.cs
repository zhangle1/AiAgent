using SqlSugar;

namespace AiAgent.Backend.Entities.Memory;

/// <summary>
/// 由聊天链路捕获的有限原始观察，用于后续会话摘要和人工追溯，不直接作为长期提示词全文。
/// </summary>
[SugarTable("ai_memory_observation")]
public sealed class AiMemoryObservation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public long? CodeProjectId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SessionId { get; set; }

    [SugarColumn(Length = 32)]
    public string Kind { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    public int Importance { get; set; } = 5;
    public bool IsProcessed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
