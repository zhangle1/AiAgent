using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

/// <summary>
/// A server-side project folder that owns one or more registered code repositories.
/// </summary>
[SugarTable("ai_code_project")]
public sealed class AiCodeProject
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 256)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(Length = 512)]
    public string RootPath { get; set; } = string.Empty;

    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? Description { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeletedAt { get; set; }
}
