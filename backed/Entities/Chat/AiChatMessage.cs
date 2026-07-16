using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_chat_message")]
public sealed class AiChatMessage
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string SessionId { get; set; } = string.Empty;

    [SugarColumn(Length = 16)]
    public string Role { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? Thinking { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? CitationsJson { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
