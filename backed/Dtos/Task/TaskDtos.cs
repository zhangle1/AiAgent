using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Task;

public sealed class ProjectTaskDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("project_name")] public string? ProjectName { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "local";
    [JsonPropertyName("external_id")] public string? ExternalId { get; set; }
    [JsonPropertyName("work_item_id")] public string? WorkItemId { get; set; }
    [JsonPropertyName("work_item_type")] public string? WorkItemType { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("creator")] public string? Creator { get; set; }
    [JsonPropertyName("assignee")] public string? Assignee { get; set; }
    [JsonPropertyName("collaborators")] public string? Collaborators { get; set; }
    [JsonPropertyName("priority")] public string? Priority { get; set; }
    [JsonPropertyName("labels")] public string? Labels { get; set; }
    [JsonPropertyName("external_url")] public string? ExternalUrl { get; set; }
    [JsonPropertyName("external_created_at")] public DateTime? ExternalCreatedAt { get; set; }
    [JsonPropertyName("external_updated_at")] public DateTime? ExternalUpdatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}
public sealed class CreateProjectTaskRequest
{
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("gitee_issue")] public GiteeIssueLinkRequest? GiteeIssue { get; set; }
}

public sealed class UpdateProjectTaskStatusRequest
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
}

/// <summary>用户从受控 Gitee 查询结果中主动选择的一条关联。</summary>
public sealed class GiteeIssueLinkRequest
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("assignee")] public string? Assignee { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}
