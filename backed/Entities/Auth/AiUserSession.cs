using SqlSugar;

namespace AiAgent.Backend.Entities.Auth;

[SugarTable("ai_user_session")]
public sealed class AiUserSession
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? RevokedAt { get; set; }
}
