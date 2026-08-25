using SqlSugar;

namespace AiAgent.Backend.Entities.WorkCanvas;

[SugarTable("ai_work_canvas_run")]
public sealed class AiWorkCanvasRun
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 64, IsNullable = true)] public string? CanvasId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? UserId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? RootNodeId { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? Status { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? StartedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? FinishedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? UpdatedAt { get; set; }
}
