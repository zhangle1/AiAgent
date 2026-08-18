using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Task;

public sealed class ProjectTaskDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("project_name")] public string? ProjectName { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "local";
    [JsonPropertyName("external_id")] public string? ExternalId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("assignee")] public string? Assignee { get; set; }
    [JsonPropertyName("external_url")] public string? ExternalUrl { get; set; }
    [JsonPropertyName("external_updated_at")] public DateTime? ExternalUpdatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}
public sealed class CreateProjectTaskRequest
{
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
}
