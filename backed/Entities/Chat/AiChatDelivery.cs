using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_chat_delivery")]
[SugarIndex("ix_chat_delivery_session", nameof(UserId), OrderByType.Asc, nameof(SessionId), OrderByType.Asc, nameof(Position), OrderByType.Asc)]
public sealed class AiChatDelivery
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = "";
    [SugarColumn(Length = 64)] public string UserId { get; set; } = "";
    [SugarColumn(Length = 64)] public string SessionId { get; set; } = "";
    [SugarColumn(Length = 128)] public string CommandId { get; set; } = "";
    [SugarColumn(Length = 64)] public string Fingerprint { get; set; } = "";
    [SugarColumn(Length = 64)] public string InstanceId { get; set; } = "";
    [SugarColumn(ColumnDataType = "nvarchar(max)")] public string RequestJson { get; set; } = "";
    [SugarColumn(Length = 32)] public string Status { get; set; } = "queued";
    public bool Held { get; set; }
    public long Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true, Length = 128)] public string? NativeThreadId { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? NativeBinding { get; set; }
}
