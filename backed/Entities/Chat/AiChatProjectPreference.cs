using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_chat_proj_pref")]
public sealed class AiChatProjectPreference
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = true)]

    public long CodeProjectId { get; set; }
    [SugarColumn(IsNullable = true)]

    public bool IsPinned { get; set; }

    [SugarColumn(Length = 16)]
    public string SortMode { get; set; } = "updated";
    [SugarColumn(IsNullable = true)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
