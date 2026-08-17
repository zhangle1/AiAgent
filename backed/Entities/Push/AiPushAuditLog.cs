using SqlSugar;

namespace AiAgent.Backend.Entities.Push;

[SugarTable("ai_push_audit_log")]
public sealed class AiPushAuditLog
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? CorrelationId { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? ActorType { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ActorId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Action { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ProjectId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? PushChannelId { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Outcome { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? MetadataJson { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? OccurredAt { get; set; }
}
