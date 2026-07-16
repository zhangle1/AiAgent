using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Git;

public sealed class GitAccountPayload
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = "gitee";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
}

public sealed class GitAccountDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("provider")] public string Provider { get; set; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("token_configured")] public bool TokenConfigured { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}

public sealed class GitAccountTestResult
{
    [JsonPropertyName("status")] public string Status { get; set; } = "failed";
    [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("detail")] public string Detail { get; set; } = string.Empty;
    [JsonPropertyName("tested_at")] public DateTime TestedAt { get; set; }
}
