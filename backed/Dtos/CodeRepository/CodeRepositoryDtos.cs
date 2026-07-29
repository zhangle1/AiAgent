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

    [JsonPropertyName("chat_editable_configuration_files")]
    public List<string> ChatEditableConfigurationFiles { get; set; } = [];

    [JsonPropertyName("publish_target")]
    public string? PublishTarget { get; set; }

    [JsonPropertyName("publish_configuration")]
    public string PublishConfiguration { get; set; } = "Release";

    [JsonPropertyName("publish_runtime")]
    public string? PublishRuntime { get; set; }

    [JsonPropertyName("publish_output_path")]
    public string PublishOutputPath { get; set; } = "artifacts/publish";

    [JsonPropertyName("publish_command")]
    public string? PublishCommand { get; set; }

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

    [JsonPropertyName("chat_editable_configuration_files")]
    public List<string>? ChatEditableConfigurationFiles { get; set; }

    [JsonPropertyName("publish_target")]
    public string? PublishTarget { get; set; }

    [JsonPropertyName("publish_configuration")]
    public string? PublishConfiguration { get; set; }

    [JsonPropertyName("publish_runtime")]
    public string? PublishRuntime { get; set; }

    [JsonPropertyName("publish_output_path")]
    public string? PublishOutputPath { get; set; }

    [JsonPropertyName("publish_command")]
    public string? PublishCommand { get; set; }
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

public sealed class CodeRuntimeProfileSaveRequest
{
    [JsonPropertyName("repository_name")]
    public string RepositoryName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("entry_path")]
    public string? EntryPath { get; set; }

    [JsonPropertyName("run_script")]
    public string? RunScript { get; set; }

    [JsonPropertyName("test_script")]
    public string? TestScript { get; set; }

    [JsonPropertyName("preferred_port")]
    public int? PreferredPort { get; set; }

    [JsonPropertyName("health_path")]
    public string? HealthPath { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("is_preview_enabled")]
    public bool IsPreviewEnabled { get; set; }
}

public sealed class CodeRuntimeProfileDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("project_id")]
    public long ProjectId { get; set; }

    [JsonPropertyName("repository_id")]
    public long RepositoryId { get; set; }

    [JsonPropertyName("repository_name")]
    public string? RepositoryName { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("entry_path")]
    public string? EntryPath { get; set; }

    [JsonPropertyName("run_script")]
    public string? RunScript { get; set; }

    [JsonPropertyName("test_script")]
    public string? TestScript { get; set; }

    [JsonPropertyName("preferred_port")]
    public int? PreferredPort { get; set; }

    [JsonPropertyName("health_path")]
    public string? HealthPath { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("is_preview_enabled")]
    public bool IsPreviewEnabled { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CodeRuntimeStartRequest
{
    [JsonPropertyName("profile_ids")]
    public List<long>? ProfileIds { get; set; }
}

public sealed class CodeRuntimeRunDto
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public long ProjectId { get; set; }

    [JsonPropertyName("profile_id")]
    public long ProfileId { get; set; }

    [JsonPropertyName("repository_id")]
    public long RepositoryId { get; set; }

    [JsonPropertyName("repository_name")]
    public string RepositoryName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("access_urls")]
    public List<string> AccessUrls { get; set; } = [];

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }
}

public sealed class CodeRuntimeLogDto
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("stream")]
    public string Stream { get; set; } = string.Empty;

    [JsonPropertyName("line")]
    public string Line { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public sealed class CodeProjectRuntimeDto
{
    [JsonPropertyName("project_id")]
    public long ProjectId { get; set; }

    [JsonPropertyName("profiles")]
    public List<CodeRuntimeProfileDto> Profiles { get; set; } = [];

    [JsonPropertyName("runs")]
    public List<CodeRuntimeRunDto> Runs { get; set; } = [];
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

public sealed class CodeRepositoryGitCheckoutRequest
{
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;
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

/// <summary>
/// An untrusted file reference emitted by a chat agent.
/// </summary>
public sealed class CodeRepositoryFileReferenceResolveRequest
{
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

/// <summary>
/// A file reference resolved within one registered code repository.
/// </summary>
public sealed class CodeRepositoryFileReferenceDto
{
    [JsonPropertyName("repository_name")]
    public string RepositoryName { get; set; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("line")]
    public int? Line { get; set; }
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

    /// <summary>
    /// Directory metadata for clients that need filtering and sorting. The legacy
    /// <see cref="Directories"/> path list remains available for compatibility.
    /// </summary>
    [JsonPropertyName("directory_entries")]
    public List<CodeRepositoryDirectoryEntryDto> DirectoryEntries { get; set; } = [];

    [JsonPropertyName("files")]
    public List<CodeRepositoryBrowserFileDto> Files { get; set; } = [];
}

public sealed class CodeRepositoryDirectoryEntryDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("modified_at")]
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Creates one empty child directory inside an allowed server-side project root.
/// </summary>
public sealed class CodeRepositoryDirectoryCreateRequest
{
    [JsonPropertyName("parent_path")]
    public string ParentPath { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class CodeRepositoryBrowserFileDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
