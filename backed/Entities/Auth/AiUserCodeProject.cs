using SqlSugar;

namespace AiAgent.Backend.Entities.Auth;

/// <summary>
/// Explicit project visibility granted to a normal user. Administrators can see all projects.
/// </summary>
[SugarTable("ai_user_code_project")]
public sealed class AiUserCodeProject
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    public long CodeProjectId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
