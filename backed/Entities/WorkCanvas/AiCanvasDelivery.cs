using SqlSugar;

namespace AiAgent.Backend.Entities.WorkCanvas;

[SugarTable("ai_canvas_delivery")]
public sealed class AiCanvasDelivery
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 64, IsNullable = true)] public string? LinkId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? SourceSessionId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? TargetSessionId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? UserId { get; set; }
    [SugarColumn(Length = 500, IsNullable = true)] public string? SenderNote { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? SelectedContentJson { get; set; }
    [SugarColumn(IsNullable = true)] public bool? RunSuggested { get; set; }
    [SugarColumn(Length = 24, IsNullable = true)] public string? Status { get; set; } = "sent";
    [SugarColumn(Length = 24, IsNullable = true)] public string? Decision { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? DecidedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
}
