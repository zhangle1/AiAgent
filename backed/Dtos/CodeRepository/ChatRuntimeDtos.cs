using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.CodeRepository;

public sealed class ChatRuntimeSelection
{
    [JsonPropertyName("repository_name")] public string RepositoryName { get; set; } = "";
    [JsonPropertyName("entry_paths")] public List<string> EntryPaths { get; set; } = [];
}

public sealed class ChatRuntimePrepareRequest
{
    [JsonPropertyName("selections")] public List<ChatRuntimeSelection> Selections { get; set; } = [];
    [JsonPropertyName("idle_minutes")] public int IdleMinutes { get; set; } = 30;
}

public sealed class ChatRuntimeTarget
{
    [JsonPropertyName("repository_name")] public string RepositoryName { get; set; } = "";
    [JsonPropertyName("entry_path")] public string EntryPath { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "frontend";
    [JsonPropertyName("run_script")] public string? RunScript { get; set; }
    [JsonPropertyName("preferred_port")] public int? PreferredPort { get; set; }
    [JsonPropertyName("health_path")] public string HealthPath { get; set; } = "/";
    [JsonPropertyName("page_path")] public string PagePath { get; set; } = "/";
    [JsonPropertyName("environment")] public Dictionary<string, string> Environment { get; set; } = [];
}

public sealed class ChatRuntimeManifest
{
    [JsonPropertyName("targets")] public List<ChatRuntimeTarget> Targets { get; set; } = [];
}

public sealed class ChatRuntimeJobDto
{
    [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("manifest_repository")] public string ManifestRepository { get; set; } = "";
    [JsonPropertyName("manifest_path")] public string ManifestPath { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "waiting";
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("idle_minutes")] public int IdleMinutes { get; set; }
    [JsonPropertyName("last_visit_at")] public DateTime LastVisitAt { get; set; }
    [JsonPropertyName("runs")] public List<CodeRuntimeRunDto> Runs { get; set; } = [];
    [JsonPropertyName("targets")] public List<ChatRuntimeTarget> Targets { get; set; } = [];
}
