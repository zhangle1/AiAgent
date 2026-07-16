using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.DashboardApp;

public sealed class DashboardApplicationCreateRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("repository_name")] public string? RepositoryName { get; set; }
    [JsonPropertyName("template_id")] public string? TemplateId { get; set; }
}

public sealed class DashboardApplicationRepositoryBindRequest
{
    [JsonPropertyName("repository_name")] public string RepositoryName { get; set; } = string.Empty;
}

public sealed class DashboardFileWriteRequest
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}

public sealed class DashboardFilePatchRequest
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("expected_sha256")] public string ExpectedSha256 { get; set; } = string.Empty;
    [JsonPropertyName("search")] public string Search { get; set; } = string.Empty;
    [JsonPropertyName("replace")] public string Replace { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string? Content { get; set; }
}

public sealed class DashboardChangeValidationRequest
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("expected_contains")] public string? ExpectedContains { get; set; }
}

public sealed class DashboardGitPushRequest
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}
