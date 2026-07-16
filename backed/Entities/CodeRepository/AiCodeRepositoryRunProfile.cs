using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

/// <summary>
/// A verified development run configuration for one repository in a code project.
/// </summary>
[SugarTable("ai_code_repo_run")]
public sealed class AiCodeRepositoryRunProfile
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long RepositoryId { get; set; }

    [SugarColumn(Length = 16)]
    public string Role { get; set; } = "backend";

    [SugarColumn(Length = 512, IsNullable = true)]
    public string? EntryPath { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? RunScript { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? HealthPath { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsPreviewEnabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
