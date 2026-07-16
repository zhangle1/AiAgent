using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.CodeRepository;

/// <summary>
/// Code repository summary returned to the frontend.
/// </summary>
public sealed class CodeRepositoryDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("project_name")]
    public string? ProjectName { get; set; }

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

    [JsonPropertyName("solution_files")]
    public List<string> SolutionFiles { get; set; } = [];

    [JsonPropertyName("configuration_files")]
    public List<string> ConfigurationFiles { get; set; } = [];

    [JsonPropertyName("publish_target")]
    public string? PublishTarget { get; set; }

    [JsonPropertyName("publish_configuration")]
    public string PublishConfiguration { get; set; } = "Release";

    [JsonPropertyName("publish_runtime")]
    public string? PublishRuntime { get; set; }

    [JsonPropertyName("publish_output_path")]
    public string PublishOutputPath { get; set; } = "artifacts/publish";

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

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("solution_files")]
    public List<string>? SolutionFiles { get; set; }

    [JsonPropertyName("configuration_files")]
    public List<string>? ConfigurationFiles { get; set; }

    [JsonPropertyName("publish_target")]
    public string? PublishTarget { get; set; }

    [JsonPropertyName("publish_configuration")]
    public string? PublishConfiguration { get; set; }

    [JsonPropertyName("publish_runtime")]
    public string? PublishRuntime { get; set; }

    [JsonPropertyName("publish_output_path")]
    public string? PublishOutputPath { get; set; }
}

public sealed class CodeProjectSaveRequest
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

public sealed class CodeProjectDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("repositories")]
    public List<CodeRepositoryDto> Repositories { get; set; } = [];

    [JsonPropertyName("repository_count")]
    public int RepositoryCount => Repositories.Count;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Directory inspection request.
/// </summary>
public sealed class CodeRepositoryPathRequest
{
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;
}

public sealed class CodeRepositoryGitPushRequest
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Starts an authenticated HTTPS clone into an allowed local directory.
/// </summary>
public sealed class CodeRepositoryCloneRequest
{
    [JsonPropertyName("project_id")]
    public long ProjectId { get; set; }

    [JsonPropertyName("repository_url")]
    public string RepositoryUrl { get; set; } = string.Empty;

    [JsonPropertyName("destination_parent_path")]
    public string DestinationParentPath { get; set; } = string.Empty;

    [JsonPropertyName("git_account_id")]
    public long GitAccountId { get; set; }
}

public sealed class CodeRepositoryFileWriteRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("expected_sha256")]
    public string? ExpectedSha256 { get; set; }
}

public sealed class CodeRepositoryHealthDto
{
    [JsonPropertyName("root_exists")]
    public bool RootExists { get; set; }

    [JsonPropertyName("project_match")]
    public bool ProjectMatch { get; set; }

    [JsonPropertyName("is_git_repository")]
    public bool IsGitRepository { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("solution_files")]
    public List<CodeRepositoryFileHealth> SolutionFiles { get; set; } = [];

    [JsonPropertyName("configuration_files")]
    public List<CodeRepositoryFileHealth> ConfigurationFiles { get; set; } = [];

    [JsonPropertyName("messages")]
    public List<string> Messages { get; set; } = [];
}

public sealed class CodeRepositoryFileHealth
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }
}

/// <summary>
/// Starts a server-side publish process using the repository's saved publish settings.
/// </summary>
public sealed class CodeRepositoryPackageRequest
{
    [JsonPropertyName("repository_name")]
    public string RepositoryName { get; set; } = string.Empty;
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

    [JsonPropertyName("solution_files")]
    public List<string> SolutionFiles { get; set; } = [];

    [JsonPropertyName("configuration_files")]
    public List<string> ConfigurationFiles { get; set; } = [];
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
