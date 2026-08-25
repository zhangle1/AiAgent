using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.CodeDelivery;

public sealed class CreateCodeChangeSetRequest
{
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("task_id")] public long? TaskId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("commit_message")] public string? CommitMessage { get; set; }
}
public sealed class CodeChangeSetApprovalRequest
{
    [JsonPropertyName("approved")] public bool Approved { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
}
public sealed class CodeChangeSetDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("task_id")] public long? TaskId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("commit_message")] public string? CommitMessage { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("error_summary")] public string? ErrorSummary { get; set; }
    [JsonPropertyName("approved_by")] public string? ApprovedBy { get; set; }
    [JsonPropertyName("approval_comment")] public string? ApprovalComment { get; set; }
    [JsonPropertyName("approved_at")] public DateTime? ApprovedAt { get; set; }
    [JsonPropertyName("validated_at")] public DateTime? ValidatedAt { get; set; }
    [JsonPropertyName("delivered_at")] public DateTime? DeliveredAt { get; set; }
    [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("repositories")] public List<CodeChangeSetRepositoryDto> Repositories { get; set; } = [];
}
public sealed class CodeChangeSetRepositoryDto
{
    [JsonPropertyName("repository_id")] public long? RepositoryId { get; set; }
    [JsonPropertyName("repository_name")] public string? RepositoryName { get; set; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("branch")] public string? Branch { get; set; }
    [JsonPropertyName("snapshot_sha256")] public string? SnapshotSha256 { get; set; }
    [JsonPropertyName("files")] public List<string> Files { get; set; } = [];
    [JsonPropertyName("checks")] public List<CodeDeliveryCheckDto> Checks { get; set; } = [];
    [JsonPropertyName("validation_status")] public string? ValidationStatus { get; set; }
    [JsonPropertyName("delivery_status")] public string? DeliveryStatus { get; set; }
    [JsonPropertyName("commit_sha")] public string? CommitSha { get; set; }
    [JsonPropertyName("safe_output")] public string? SafeOutput { get; set; }
}
public sealed record CodeDeliveryCheckDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("message")] string Message);
