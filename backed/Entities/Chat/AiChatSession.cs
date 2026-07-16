using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_chat_session")]
public sealed class AiChatSession
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(Length = 160)]
    public string Title { get; set; } = "新会话";

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? PreferencesJson { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? CodeProjectId { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? SortOrder { get; set; }

    [SugarColumn(Length = 16, IsNullable = true)]
    public string? Priority { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? IsPinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
