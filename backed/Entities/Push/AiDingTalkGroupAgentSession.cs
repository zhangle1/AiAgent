using SqlSugar;

namespace AiAgent.Backend.Entities.Push;

[SugarTable("ai_dingtalk_group_agent_session")]
public sealed class AiDingTalkGroupAgentSession
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? SourceMessageId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? PushChannelId { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? ConversationId { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? SenderId { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? SenderName { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? SessionWebhookProtected { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? Question { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? CodeProjectId { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Status { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? LastErrorCode { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? Answer { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ProcessingStartedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAt { get; set; }
}
