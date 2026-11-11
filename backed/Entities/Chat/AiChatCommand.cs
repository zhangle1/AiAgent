using SqlSugar;
namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_chat_command")]
public sealed class AiChatCommand
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = "";
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? SessionId { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Fingerprint { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? InstanceId { get; set; }
    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Status { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")]
    public string? ProjectIdsJson { get; set; }
}

[SugarTable("ai_chat_command_event")]
[SugarIndex("ix_chat_command_event_cursor", nameof(CommandId), OrderByType.Asc, nameof(Id), OrderByType.Asc)]
public sealed class AiChatCommandEvent
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? CommandId { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")]
    public string? PayloadJson { get; set; }
}
