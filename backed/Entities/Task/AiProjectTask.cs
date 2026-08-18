using SqlSugar;

namespace AiAgent.Backend.Entities.Task;

[SugarTable("ai_project_task")]
public sealed class AiProjectTask
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string UserId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = true)] public long? CodeProjectId { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string Source { get; set; } = "local";
    [SugarColumn(Length = 128, IsNullable = true)] public string? ExternalId { get; set; }
    [SugarColumn(Length = 512, IsNullable = true)] public string Title { get; set; } = string.Empty;
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? Description { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? Status { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? Assignee { get; set; }
    [SugarColumn(Length = 1024, IsNullable = true)] public string? ExternalUrl { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? ExternalUpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)] public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public bool IsDeleted { get; set; }
}
