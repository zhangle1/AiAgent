using SqlSugar;

namespace AiAgent.Backend.Entities.WorkCanvas;

[SugarTable("ai_work_canvas_run_node")]
public sealed class AiWorkCanvasRunNode
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 64, IsNullable = true)] public string? RunId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? NodeId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? SessionId { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? SourceNodeIdsJson { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? Status { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? Agent { get; set; }
    [SugarColumn(Length = 200, IsNullable = true)] public string? ModelId { get; set; }
    [SugarColumn(Length = 2000, IsNullable = true)] public string? Error { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? StartedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? FinishedAt { get; set; }
}
