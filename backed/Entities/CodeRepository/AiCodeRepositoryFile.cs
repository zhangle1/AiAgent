using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

/// <summary>Persisted searchable source file for one registered code repository.</summary>
[SugarTable("ai_code_repository_file")]
public sealed class AiCodeRepositoryFile
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public long CodeRepositoryId { get; set; }
    [SugarColumn(Length = 1024)] public string RelativePath { get; set; } = string.Empty;
    [SugarColumn(Length = 32)] public string Extension { get; set; } = string.Empty;
    [SugarColumn(ColumnDataType = "nvarchar(max)")] public string Content { get; set; } = string.Empty;
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? SymbolsJson { get; set; }
    [SugarColumn(Length = 64)] public string ContentHash { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
}