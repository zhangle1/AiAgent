using SqlSugar;

namespace AiAgent.Backend.Entities.WorkCanvas;

[SugarTable("ai_work_canvas_edge")]
public sealed class AiWorkCanvasEdge
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 64)] public string CanvasId { get; set; } = string.Empty;
    [SugarColumn(Length = 64)] public string SourceNodeId { get; set; } = string.Empty;
    [SugarColumn(Length = 64)] public string TargetNodeId { get; set; } = string.Empty;
    [SugarColumn(Length = 32)] public string RelationType { get; set; } = "related_to";
    [SugarColumn(Length = 160, IsNullable = true)] public string? Label { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
