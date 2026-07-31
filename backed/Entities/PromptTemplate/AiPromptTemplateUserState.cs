using SqlSugar;

namespace AiAgent.Backend.Entities.PromptTemplate;

[SugarTable("ai_prompt_template_user_state")]
public sealed class AiPromptTemplateUserState
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    public long TemplateId { get; set; }
    public bool IsLiked { get; set; }
    public bool IsFavorited { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
