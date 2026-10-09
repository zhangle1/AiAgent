using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.TaskCenter;

public sealed class TaskCenterTaskDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("task_type")] public string TaskType { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("resource_id")] public string? ResourceId { get; set; }
    [JsonPropertyName("resource_uri")] public string? ResourceUri { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("started_at")] public DateTime? StartedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTime? FinishedAt { get; set; }
    [JsonPropertyName("cancellable")] public bool Cancellable { get; set; }
    [JsonPropertyName("retryable")] public bool Retryable { get; set; }
}

public sealed class TaskCenterSummaryDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("queued")] public int Queued { get; set; }
    [JsonPropertyName("processing")] public int Processing { get; set; }
    [JsonPropertyName("completed")] public int Completed { get; set; }
    [JsonPropertyName("failed")] public int Failed { get; set; }
    [JsonPropertyName("cancelled")] public int Cancelled { get; set; }
}

public sealed class TaskCenterResponseDto
{
    [JsonPropertyName("tasks")] public List<TaskCenterTaskDto> Tasks { get; set; } = [];
    [JsonPropertyName("summary")] public TaskCenterSummaryDto Summary { get; set; } = new();
}
