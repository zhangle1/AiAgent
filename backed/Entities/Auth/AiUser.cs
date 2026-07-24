using SqlSugar;

namespace AiAgent.Backend.Entities.Auth;

[SugarTable("ai_user")]
public sealed class AiUser
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 64)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 512)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(Length = 256)]
    public string PasswordSalt { get; set; } = string.Empty;

    [SugarColumn(Length = 16, IsNullable = true)]
    public string Role { get; set; } = "user";

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Alias { get; set; }

    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
