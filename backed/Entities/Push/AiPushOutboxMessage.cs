using SqlSugar;

namespace AiAgent.Backend.Entities.Push;

[SugarTable("ai_push_outbox_message")]
public sealed class AiPushOutboxMessage
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? CorrelationId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ProjectId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? PushChannelId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ProjectPushBindingId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? EventType { get; set; }

    [SugarColumn(Length = 160, IsNullable = true)]
    public string? DedupKey { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? PayloadJson { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Status { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? AttemptCount { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? NextAttemptAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? SendingAt { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? LastErrorCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? SentAt { get; set; }
}
