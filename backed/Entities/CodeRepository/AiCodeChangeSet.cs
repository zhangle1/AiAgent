using SqlSugar;

namespace AiAgent.Backend.Entities.CodeRepository;

[SugarTable("ai_code_change_set")]
public sealed class AiCodeChangeSet
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = true)] public long? ProjectId { get; set; }
    [SugarColumn(IsNullable = true)] public long? TaskId { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? UserId { get; set; }
    [SugarColumn(Length = 256, IsNullable = true)] public string? Title { get; set; }
    [SugarColumn(Length = 200, IsNullable = true)] public string? CommitMessage { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? Status { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? Summary { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? ErrorSummary { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? ApprovedBy { get; set; }
    [SugarColumn(Length = 1000, IsNullable = true)] public string? ApprovalComment { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? ApprovedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? ValidatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? DeliveredAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)] public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public bool? IsDeleted { get; set; }
}

[SugarTable("ai_code_change_set_repository")]
public sealed class AiCodeChangeSetRepository
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = true)] public long? ChangeSetId { get; set; }
    [SugarColumn(IsNullable = true)] public long? RepositoryId { get; set; }
    [SugarColumn(Length = 128, IsNullable = true)] public string? RepositoryName { get; set; }
    [SugarColumn(Length = 128, IsNullable = true)] public string? DisplayName { get; set; }
    [SugarColumn(Length = 256, IsNullable = true)] public string? Branch { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? SnapshotSha256 { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? FilesJson { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? ValidationJson { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? ValidationStatus { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? DeliveryStatus { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? CommitSha { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? SafeOutput { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? UpdatedAt { get; set; }
}
