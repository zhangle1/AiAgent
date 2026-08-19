using SqlSugar;

namespace AiAgent.Backend.Entities.WorkCanvas;

[SugarTable("ai_work_canvas_node")]
public sealed class AiWorkCanvasNode
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 64)] public string CanvasId { get; set; } = string.Empty;
    [SugarColumn(Length = 32)] public string NodeType { get; set; } = "session";
    [SugarColumn(Length = 64, IsNullable = true)] public string? ChatSessionId { get; set; }
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    [SugarColumn(IsNullable = true)] public decimal? Width { get; set; }
    [SugarColumn(IsNullable = true)] public decimal? Height { get; set; }
    [SugarColumn(IsNullable = true)] public int? ZIndex { get; set; }
    [SugarColumn(Length = 160, IsNullable = true)] public string? TitleOverride { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? DataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
