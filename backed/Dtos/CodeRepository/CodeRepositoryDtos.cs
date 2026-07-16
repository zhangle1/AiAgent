using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.CodeRepository;

/// <summary>
/// Code repository summary returned to the frontend.
/// </summary>
public sealed class CodeRepositoryDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "local_directory";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "configured";

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = [];

    [JsonPropertyName("build_systems")]
    public List<string> BuildSystems { get; set; } = [];

    [JsonPropertyName("is_git_repository")]
    public bool IsGitRepository { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("last_scanned_at")]
    public DateTime? LastScannedAt { get; set; }

    [JsonPropertyName("last_indexed_at")]
    public DateTime? LastIndexedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Local directory registration request.
/// </summary>
public sealed class CodeRepositorySaveRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Directory inspection request.
/// </summary>
public sealed class CodeRepositoryPathRequest
{
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Starts an authenticated HTTPS clone into an allowed local directory.
/// </summary>
public sealed class CodeRepositoryCloneRequest
{
    [JsonPropertyName("repository_url")]
    public string RepositoryUrl { get; set; } = string.Empty;

    [JsonPropertyName("destination_parent_path")]
    public string DestinationParentPath { get; set; } = string.Empty;

    [JsonPropertyName("git_account_id")]
    public long GitAccountId { get; set; }
}

/// <summary>
/// Lightweight project inspection result.
/// </summary>
public sealed class CodeRepositoryInspectionDto
{
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("suggested_name")]
    public string SuggestedName { get; set; } = string.Empty;

    [JsonPropertyName("suggested_display_name")]
    public string SuggestedDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = [];

    [JsonPropertyName("build_systems")]
    public List<string> BuildSystems { get; set; } = [];

    [JsonPropertyName("is_git_repository")]
    public bool IsGitRepository { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("marker_files")]
    public List<string> MarkerFiles { get; set; } = [];
}

/// <summary>
/// Directory browser response restricted to configured repository roots.
/// </summary>
public sealed class CodeRepositoryDirectoryBrowserDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("parent_path")]
    public string? ParentPath { get; set; }

    [JsonPropertyName("allowed_roots")]
    public List<string> AllowedRoots { get; set; } = [];

    [JsonPropertyName("directories")]
    public List<string> Directories { get; set; } = [];
}
