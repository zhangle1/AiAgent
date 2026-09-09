using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

[SugarTable("ai_repository_maintenance_plan")]
public sealed class AiRepositoryMaintenancePlan
{
    [SugarColumn(IsPrimaryKey = true)] public long ProjectId { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? UserId { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")] public string? SettingsJson { get; set; }
    [SugarColumn(IsNullable = true)] public bool? Enabled { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? NextRunAt { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? ActiveRunId { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? UpdatedAt { get; set; }
}

[SugarTable("ai_repository_maintenance_run")]
public sealed class AiRepositoryMaintenanceRun
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(IsNullable = true)] public long? ProjectId { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? UserId { get; set; }
    [SugarColumn(IsNullable = true, Length = 32)] public string? Trigger { get; set; }
    [SugarColumn(IsNullable = true, Length = 32)] public string? Status { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")] public string? SettingsJson { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")] public string? Report { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")] public string? Log { get; set; }
    [SugarColumn(IsNullable = true, ColumnDataType = "nvarchar(max)")] public string? WorkspaceJson { get; set; }
    [SugarColumn(IsNullable = true)] public long? ChangeSetId { get; set; }
    [SugarColumn(IsNullable = true)] public bool? CancelRequested { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? StartedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? FinishedAt { get; set; }
}
