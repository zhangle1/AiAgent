using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

/// <summary>
/// Stores one locally registered source-code repository.
/// </summary>
[SugarTable("ai_code_repository")]
public sealed class AiCodeRepository
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ProjectId { get; set; }

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 256)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(Length = 512)]
    public string RootPath { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string SourceType { get; set; } = "local_directory";

    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? Description { get; set; }

    [SugarColumn(Length = 64)]
    public string Status { get; set; } = "configured";

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? TechStackJson { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? MetadataJson { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastScannedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastIndexedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeletedAt { get; set; }
}
