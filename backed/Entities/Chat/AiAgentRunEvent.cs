using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_agent_run_event")]
public sealed class AiAgentRunEvent
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? RunId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? Sequence { get; set; }

    [SugarColumn(Length = 48, IsNullable = true)]
    public string? EventType { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ItemId { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? PayloadJson { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }
}

