using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_chat_debug_trace")]
public sealed class AiChatDebugTrace
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string SessionId { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string TraceId { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string Provider { get; set; } = "unknown";

    [SugarColumn(Length = 32)]
    public string Transport { get; set; } = "unknown";

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string EventsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
