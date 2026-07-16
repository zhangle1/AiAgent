using SqlSugar;

namespace AiAgent.Backend.Entities.Git;

[SugarTable("ai_git_account")]
public sealed class AiGitAccount
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string Provider { get; set; } = "gitee";

    [SugarColumn(Length = 128)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? Email { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? AccessTokenProtected { get; set; }

    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
