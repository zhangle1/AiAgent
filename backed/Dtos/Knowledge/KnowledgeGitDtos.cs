using System.Text.Json.Serialization;
using AiAgent.Backend.Services.Git;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class KnowledgeGitConfig
{
    [JsonPropertyName("remote_url")] public string RemoteUrl { get; set; } = "";
    [JsonPropertyName("branch")] public string Branch { get; set; } = "main";
}
public sealed class KnowledgeGitState
{
    [JsonPropertyName("root_path")] public string RootPath { get; set; } = "";
    [JsonPropertyName("workspace_path")] public string WorkspacePath { get; set; } = "";
    [JsonPropertyName("config")] public KnowledgeGitConfig Config { get; set; } = new();
    [JsonPropertyName("status")] public GitWorkspaceStatus? Status { get; set; }
    [JsonPropertyName("session_options")] public List<KnowledgeResourceNodeDto> SessionOptions { get; set; } = [];
}
public sealed class KnowledgeGitRefresh
{
    [JsonPropertyName("session_uris")] public List<string> SessionUris { get; set; } = [];
    [JsonPropertyName("resource_uris")] public List<string>? ResourceUris { get; set; }
    [JsonPropertyName("force")] public bool Force { get; set; } = true;
}
public sealed class KnowledgeGitPush
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("snapshot")] public string Snapshot { get; set; } = "";
    [JsonPropertyName("force")] public bool Force { get; set; } = true;
}
