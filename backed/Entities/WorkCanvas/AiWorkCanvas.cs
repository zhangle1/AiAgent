using SqlSugar;

namespace AiAgent.Backend.Entities.WorkCanvas;

[SugarTable("ai_work_canvas")]
public sealed class AiWorkCanvas
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 64)] public string UserId { get; set; } = string.Empty;
    [SugarColumn(Length = 160)] public string Name { get; set; } = string.Empty;
    [SugarColumn(IsNullable = true)] public long? ScopeProjectId { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? ViewportJson { get; set; }
    [SugarColumn(IsNullable = true)] public int? Version { get; set; }
    [SugarColumn(IsNullable = true)] public bool? IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
