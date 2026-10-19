using System.Text.Json.Serialization;
using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Dtos.Admin;

public sealed class AdminCreateUserRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
    [JsonPropertyName("can_commit_code")] public bool CanCommitCode { get; set; }
}

public sealed class AdminUpdateUserAliasRequest
{
    [JsonPropertyName("alias")] public string? Alias { get; set; }
}

public sealed class AdminResetUserPasswordRequest
{
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public sealed class AdminUpdateUserProjectsRequest
{
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
}

public sealed class AdminUpdateUserCodeCommitPermissionRequest
{
    [JsonPropertyName("can_commit_code")] public bool CanCommitCode { get; set; }
}

public sealed class AdminUpdateUserStatusRequest
{
    [JsonPropertyName("is_disabled")] public bool IsDisabled { get; set; }
}

public sealed class AdminUserImportResultDto
{
    [JsonPropertyName("created_count")] public int CreatedCount { get; set; }
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = [];
}

public sealed class AdminUserDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("is_disabled")] public bool IsDisabled { get; set; }
    [JsonPropertyName("can_commit_code")] public bool CanCommitCode { get; set; }
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
    [JsonPropertyName("metrics")] public AdminUsageMetricsDto Metrics { get; set; } = new();
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
    [JsonPropertyName("metrics")] public AdminUsageMetricsDto Metrics { get; set; } = new();
    [JsonPropertyName("projects")] public List<AdminUsageProjectDto> Projects { get; set; } = [];
    [JsonPropertyName("user_projects")] public List<AdminUsageUserProjectDto> UserProjects { get; set; } = [];
    [JsonPropertyName("period")] public string Period { get; set; } = "day";
    [JsonPropertyName("from")] public DateTime From { get; set; }
    [JsonPropertyName("to")] public DateTime To { get; set; }
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
    [JsonPropertyName("buckets")] public List<AdminUsageBucketDto> Buckets { get; set; } = [];
    [JsonPropertyName("users")] public List<AdminUsageUserDto> Users { get; set; } = [];
}

public sealed class AdminUsageMetricsDto
{
    [JsonPropertyName("session_count")] public int SessionCount { get; set; }
    [JsonPropertyName("active_days")] public int ActiveDays { get; set; }
    [JsonPropertyName("daily_turns")] public double DailyTurns { get; set; }
}
public sealed class AdminUsageProjectDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}
public sealed class AdminUsageUserProjectDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("project_name")] public string ProjectName { get; set; } = "";
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
    [JsonPropertyName("metrics")] public AdminUsageMetricsDto Metrics { get; set; } = new();
}
