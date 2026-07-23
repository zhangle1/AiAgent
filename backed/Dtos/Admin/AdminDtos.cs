using System.Text.Json.Serialization;
using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Dtos.Admin;

public sealed class AdminCreateUserRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
}

public sealed class AdminUpdateUserProjectsRequest
{
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
}

public sealed class AdminUserDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("is_disabled")] public bool IsDisabled { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
}

public sealed class AdminSessionSummaryDto : ChatSessionSummaryDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
}

public sealed class AdminUsageBucketDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
}

public sealed class AdminUsageUserDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("prompt_tokens")] public long PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public long CompletionTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
}

public sealed class AdminUsageReportDto
{
    [JsonPropertyName("period")] public string Period { get; set; } = "day";
    [JsonPropertyName("from")] public DateTime From { get; set; }
    [JsonPropertyName("to")] public DateTime To { get; set; }
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
    [JsonPropertyName("buckets")] public List<AdminUsageBucketDto> Buckets { get; set; } = [];
    [JsonPropertyName("users")] public List<AdminUsageUserDto> Users { get; set; } = [];
}
