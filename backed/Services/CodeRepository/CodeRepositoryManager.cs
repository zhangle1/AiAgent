using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Entities.CodeRepository;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

/// <summary>
/// Manages registered local source-code repositories and safe directory inspection.
/// </summary>
public interface ICodeRepositoryManager
{
    List<CodeRepositoryDto> List();

    List<CodeProjectDto> ListProjects();

    CodeProjectDto GetProject(long projectId);

    CodeProjectDto CreateProject(CodeProjectSaveRequest request);

    CodeProjectDto UpdateProject(long projectId, CodeProjectSaveRequest request);

    void DeleteProject(long projectId);

    CodeRepositoryDirectoryBrowserDto Browse(string? path);

    CodeRepositoryDirectoryBrowserDto BrowseFiles(string rootPath, string? path, string kind);

    CodeRepositoryInspectionDto Inspect(string rootPath);

    CodeRepositoryDto Create(CodeRepositorySaveRequest request);

    CodeRepositoryDto Get(string name);

    CodeRepositoryDto Update(string name, CodeRepositorySaveRequest request);

    CodeRepositoryHealthDto CheckHealth(string name);

    object ReadConfiguredFile(string name, string path);

    object WriteConfiguredFile(string name, CodeRepositoryFileWriteRequest request);

    (string FilePath, string DownloadName) GetPackageArchive(string name, string archiveName);

    void Delete(string name);
}

/// <summary>
/// Default local code repository manager.
/// </summary>
public sealed class CodeRepositoryManager : ICodeRepositoryManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISqlSugarClient _db;
    private readonly List<string> _allowedRoots;

    /// <summary>
    /// Initializes the manager and resolves the allowed local roots.
    /// </summary>
    public CodeRepositoryManager(ISqlSugarClient db, IConfiguration configuration)
    {
        _db = db;
        _allowedRoots = ResolveAllowedRoots(configuration);
    }

    /// <summary>
    /// Lists registered repositories with their last detected project metadata.
    /// </summary>
    public List<CodeRepositoryDto> List()
    {
        var projects = _db.Queryable<AiCodeProject>()
            .Where(x => !x.IsDeleted)
            .ToList()
            .ToDictionary(x => x.Id);
        return _db.Queryable<AiCodeRepository>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt)
            .OrderByDescending(x => x.CreatedAt)
            .ToList()
            .Select(item => ToDto(item, projects.GetValueOrDefault(item.ProjectId ?? 0)))
            .ToList();
    }

    public List<CodeProjectDto> ListProjects()
    {
        var repositories = List().Where(x => x.ProjectId.HasValue).GroupBy(x => x.ProjectId!.Value).ToDictionary(x => x.Key, x => x.ToList());
        return _db.Queryable<AiCodeProject>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt)
            .OrderByDescending(x => x.CreatedAt)
            .ToList()
            .Select(project => ToProjectDto(project, repositories.GetValueOrDefault(project.Id) ?? []))
            .ToList();
    }

    public CodeProjectDto GetProject(long projectId)
    {
        var project = FindProject(projectId);
        var repositories = List().Where(item => item.ProjectId == projectId).ToList();
        return ToProjectDto(project, repositories);
    }

    public CodeProjectDto CreateProject(CodeProjectSaveRequest request)
    {
        var rootPath = NormalizeAndValidatePath(request.RootPath);
        var name = NormalizeName(request.Name, Path.GetFileName(rootPath));
        if (_db.Queryable<AiCodeProject>().Any(x => x.Name == name && !x.IsDeleted))
        {
            throw new InvalidOperationException($"Code project '{name}' already exists.");
        }
        if (_db.Queryable<AiCodeProject>().Any(x => x.RootPath == rootPath && !x.IsDeleted))
        {
            throw new InvalidOperationException("This project folder is already registered.");
        }
        var now = DateTime.UtcNow;
        var project = new AiCodeProject
        {
            Name = name,
            DisplayName = FirstNonEmpty(request.DisplayName, Path.GetFileName(rootPath), name),
            RootPath = rootPath,
            Description = NormalizeOptional(request.Description),
            CreatedAt = now,
            UpdatedAt = now
        };
        project.Id = _db.Insertable(project).ExecuteReturnIdentity();
        return ToProjectDto(project, []);
    }

    public CodeProjectDto UpdateProject(long projectId, CodeProjectSaveRequest request)
    {
        var project = FindProject(projectId);
        var rootPath = NormalizeAndValidatePath(request.RootPath);
        if (_db.Queryable<AiCodeProject>().Any(x => x.Id != projectId && x.RootPath == rootPath && !x.IsDeleted))
        {
            throw new InvalidOperationException("This project folder is already registered.");
        }
        var attached = _db.Queryable<AiCodeRepository>().Where(x => x.ProjectId == projectId && !x.IsDeleted).ToList();
        if (attached.Any(repository => !IsPathWithin(rootPath, repository.RootPath)))
        {
            throw new InvalidOperationException("The new project folder must include every attached code repository.");
        }
        project.DisplayName = FirstNonEmpty(request.DisplayName, project.DisplayName, Path.GetFileName(rootPath));
        project.RootPath = rootPath;
        project.Description = NormalizeOptional(request.Description);
        project.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(project).ExecuteCommand();
        return ToProjectDto(project, attached.Select(repository => ToDto(repository, project)).ToList());
    }

    public void DeleteProject(long projectId)
    {
        var project = FindProject(projectId);
        if (_db.Queryable<AiCodeRepository>().Any(x => x.ProjectId == projectId && !x.IsDeleted))
        {
            throw new InvalidOperationException("Remove or move the project's code repositories before deleting the project.");
        }
        _db.Updateable<AiCodeProject>()
            .SetColumns(x => new AiCodeProject { IsDeleted = true, DeletedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow })
            .Where(x => x.Id == project.Id)
            .ExecuteCommand();
    }

    /// <summary>
    /// Browses immediate child directories without allowing traversal outside configured roots.
    /// </summary>
    public CodeRepositoryDirectoryBrowserDto Browse(string? path)
    {
        var currentPath = string.IsNullOrWhiteSpace(path) ? _allowedRoots[0] : NormalizeAndValidatePath(path);
        var parent = Directory.GetParent(currentPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) && !IsAllowedPath(parent))
        {
            parent = null;
        }

        var directories = Directory.EnumerateDirectories(currentPath)
            .Select(Path.GetFullPath)
            .Where(IsAllowedPath)
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        return new CodeRepositoryDirectoryBrowserDto
        {
            Path = currentPath,
            ParentPath = parent,
            AllowedRoots = _allowedRoots,
            Directories = directories
        };
    }

    /// <summary>
    /// Browses a repository's directories and selectable files without allowing traversal outside that repository.
    /// </summary>
    public CodeRepositoryDirectoryBrowserDto BrowseFiles(string rootPath, string? path, string kind)
    {
        var root = NormalizeAndValidatePath(rootPath);
        var currentPath = string.IsNullOrWhiteSpace(path) ? root : Path.GetFullPath(path);
        if (!Directory.Exists(currentPath) || !IsPathWithin(root, currentPath))
        {
            throw new InvalidOperationException("The selected directory is outside the code repository.");
        }

        var parent = Directory.GetParent(currentPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) && !IsPathWithin(root, parent))
        {
            parent = null;
        }

        var directories = Directory.EnumerateDirectories(currentPath)
            .Where(path => !IsIgnoredDirectory(path))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
        var files = Directory.EnumerateFiles(currentPath)
            .Where(path => IsSelectableFile(path, kind))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .Select(path => new CodeRepositoryBrowserFileDto
            {
                Name = Path.GetFileName(path),
                Path = Path.GetRelativePath(root, path).Replace('\\', '/')
            })
            .ToList();

        return new CodeRepositoryDirectoryBrowserDto
        {
            Path = currentPath,
            ParentPath = parent,
            AllowedRoots = [root],
            Directories = directories,
            Files = files
        };
    }

    /// <summary>
    /// Checks a directory and detects its basic repository metadata without indexing source files.
    /// </summary>
    public CodeRepositoryInspectionDto Inspect(string rootPath)
    {
        var path = NormalizeAndValidatePath(rootPath);
        return InspectCore(path);
    }

    /// <summary>
    /// Registers a new local source-code repository.
    /// </summary>
    public CodeRepositoryDto Create(CodeRepositorySaveRequest request)
    {
        var inspection = Inspect(request.RootPath);
        var name = NormalizeName(request.Name, inspection.SuggestedName);
        var project = request.ProjectId.HasValue ? FindProject(request.ProjectId.Value) : null;
        EnsureProjectContainsRepository(project, inspection.RootPath);
        if (_db.Queryable<AiCodeRepository>().Any(x => x.Name == name && !x.IsDeleted))
        {
            throw new InvalidOperationException($"Code repository '{name}' already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new AiCodeRepository
        {
            ProjectId = project?.Id,
            Name = name,
            DisplayName = FirstNonEmpty(request.DisplayName, inspection.SuggestedDisplayName, name),
            RootPath = inspection.RootPath,
            Description = NormalizeOptional(request.Description),
            Status = "configured",
            TechStackJson = JsonSerializer.Serialize(CreateMetadata(request, inspection), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            LastScannedAt = now
        };

        entity.Id = _db.Insertable(entity).ExecuteReturnIdentity();
        return ToDto(entity, project);
    }

    public CodeRepositoryDto Get(string name)
    {
        var entity = Find(name);
        var project = entity.ProjectId.HasValue ? FindProject(entity.ProjectId.Value) : null;
        return ToDto(entity, project);
    }

    /// <summary>
    /// Updates repository settings and refreshes the detected metadata for its selected directory.
    /// </summary>
    public CodeRepositoryDto Update(string name, CodeRepositorySaveRequest request)
    {
        var entity = Find(name);
        var inspection = Inspect(request.RootPath);
        var project = request.ProjectId.HasValue ? FindProject(request.ProjectId.Value) : entity.ProjectId.HasValue ? FindProject(entity.ProjectId.Value) : null;
        EnsureProjectContainsRepository(project, inspection.RootPath);
        var now = DateTime.UtcNow;
        entity.DisplayName = FirstNonEmpty(request.DisplayName, entity.DisplayName, inspection.SuggestedDisplayName, entity.Name);
        entity.ProjectId = project?.Id;
        entity.RootPath = inspection.RootPath;
        entity.Description = NormalizeOptional(request.Description);
        entity.Status = "configured";
        entity.TechStackJson = JsonSerializer.Serialize(CreateMetadata(request, inspection, ReadMetadata(entity)), JsonOptions);
        entity.UpdatedAt = now;
        entity.LastScannedAt = now;

        _db.Updateable(entity).ExecuteCommand();
        return ToDto(entity, project);
    }

    /// <summary>
    /// Soft deletes repository metadata only; source files are never touched.
    /// </summary>
    public void Delete(string name)
    {
        var entity = Find(name);
        _db.Updateable<AiCodeRepository>()
            .SetColumns(x => new AiCodeRepository
            {
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == entity.Id)
            .ExecuteCommand();
    }

    public CodeRepositoryHealthDto CheckHealth(string name)
    {
        var entity = Find(name);
        var metadata = ReadMetadata(entity);
        var rootExists = Directory.Exists(entity.RootPath);
        var project = entity.ProjectId.HasValue ? FindProject(entity.ProjectId.Value) : null;
        var projectMatch = project is not null && rootExists && IsPathWithin(project.RootPath, entity.RootPath);
        var health = new CodeRepositoryHealthDto
        {
            RootExists = rootExists,
            ProjectMatch = projectMatch,
            IsGitRepository = rootExists && Directory.Exists(Path.Combine(entity.RootPath, ".git")),
            Branch = rootExists && Directory.Exists(Path.Combine(entity.RootPath, ".git")) ? ReadGitBranch(entity.RootPath) : null,
            SolutionFiles = CheckFiles(entity.RootPath, metadata.SolutionFiles),
            ConfigurationFiles = CheckFiles(entity.RootPath, metadata.ConfigurationFiles)
        };
        if (!rootExists) health.Messages.Add("代码库目录不存在或服务器无法访问。");
        if (project is null) health.Messages.Add("代码库尚未挂载到项目文件夹。");
        else if (!projectMatch) health.Messages.Add("代码库目录不在所属项目文件夹内。");
        if (!health.IsGitRepository) health.Messages.Add("未检测到 .git 目录。");
        if (health.SolutionFiles.Any(item => !item.Exists)) health.Messages.Add("部分已选解决方案或工程文件不存在。");
        if (health.ConfigurationFiles.Any(item => !item.Exists)) health.Messages.Add("部分已选配置文件不存在。");
        if (health.Messages.Count == 0) health.Messages.Add("目录、Git 和已选文件检查通过。");
        return health;
    }

    public object ReadConfiguredFile(string name, string path)
    {
        var entity = Find(name);
        var metadata = ReadMetadata(entity);
        var normalized = NormalizeConfiguredPath(entity.RootPath, path, metadata.ConfigurationFiles);
        var fullPath = Path.Combine(entity.RootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The configured file does not exist.");
        if (info.Length > 1024 * 1024) throw new InvalidOperationException("Only configured text files up to 1 MB can be edited.");
        var content = File.ReadAllText(fullPath, new UTF8Encoding(false));
        return new { path = normalized, content, sha256 = ComputeSha256(content), updated_at = info.LastWriteTimeUtc };
    }

    public object WriteConfiguredFile(string name, CodeRepositoryFileWriteRequest request)
    {
        var entity = Find(name);
        var metadata = ReadMetadata(entity);
        var normalized = NormalizeConfiguredPath(entity.RootPath, request.Path, metadata.ConfigurationFiles);
        if (request.Content.Length > 1024 * 1024) throw new InvalidOperationException("Configured file content is limited to 1 MB.");
        var fullPath = Path.Combine(entity.RootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
        var existing = File.ReadAllText(fullPath, new UTF8Encoding(false));
        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256) && !string.Equals(request.ExpectedSha256, ComputeSha256(existing), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured file changed on disk. Reload it before saving.");
        var temporary = fullPath + ".aiagent.tmp";
        File.WriteAllText(temporary, request.Content, new UTF8Encoding(false));
        File.Move(temporary, fullPath, true);
        entity.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(entity).UpdateColumns(item => item.UpdatedAt).ExecuteCommand();
        return new { ok = true, path = normalized, sha256 = ComputeSha256(request.Content) };
    }

    public (string FilePath, string DownloadName) GetPackageArchive(string name, string archiveName)
    {
        var repository = Find(name);
        var fileName = Path.GetFileName(archiveName);
        if (!fileName.Equals(archiveName, StringComparison.Ordinal) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The package archive name is invalid.");
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, "App_Data", "code-packages", repository.Name, fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The package archive does not exist.");
        }

        return (filePath, fileName);
    }

    private AiCodeRepository Find(string name)
    {
        var normalized = NormalizeName(name, string.Empty);
        var entity = _db.Queryable<AiCodeRepository>()
            .Where(x => x.Name == normalized && !x.IsDeleted)
            .First();
        return entity ?? throw new InvalidOperationException($"Code repository '{name}' does not exist.");
    }

    private AiCodeProject FindProject(long projectId)
    {
        var project = _db.Queryable<AiCodeProject>().Where(x => x.Id == projectId && !x.IsDeleted).First();
        return project ?? throw new InvalidOperationException("The selected code project does not exist.");
    }

    private CodeRepositoryInspectionDto InspectCore(string rootPath)
    {
        var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var markerFiles = new List<string>();
        var languages = new List<string>();
        var buildSystems = new List<string>();

        AddMarker(files, markerFiles, "package.json");
        AddMarker(files, markerFiles, "pnpm-workspace.yaml");
        AddMarker(files, markerFiles, "Cargo.toml");
        AddMarker(files, markerFiles, "pyproject.toml");
        AddMarker(files, markerFiles, "BUILD.bazel");
        AddMarker(files, markerFiles, "MODULE.bazel");
        AddMarker(files, markerFiles, "Dockerfile");
        AddMarker(directories, markerFiles, ".git");

        if (files.Contains("package.json") || files.Contains("pnpm-workspace.yaml"))
        {
            languages.Add("TypeScript/JavaScript");
            buildSystems.Add(files.Contains("pnpm-workspace.yaml") ? "pnpm" : "npm");
        }

        if (files.Contains("Cargo.toml"))
        {
            languages.Add("Rust");
            buildSystems.Add("Cargo");
        }

        if (files.Contains("pyproject.toml") || files.Contains("requirements.txt"))
        {
            languages.Add("Python");
            buildSystems.Add("Python");
        }

        if (files.Contains("BUILD.bazel") || files.Contains("MODULE.bazel"))
        {
            buildSystems.Add("Bazel");
        }

        if (Directory.EnumerateFiles(rootPath, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories).Take(1).Any())
        {
            languages.Add("C#");
            buildSystems.Add("dotnet");
        }

        var isGitRepository = directories.Contains(".git");
        var branch = isGitRepository ? ReadGitBranch(rootPath) : null;
        return new CodeRepositoryInspectionDto
        {
            RootPath = rootPath,
            SuggestedName = NormalizeName(Path.GetFileName(rootPath), "repository"),
            SuggestedDisplayName = Path.GetFileName(rootPath),
            Languages = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            BuildSystems = buildSystems.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsGitRepository = isGitRepository,
            Branch = branch,
            MarkerFiles = markerFiles,
            SolutionFiles = FindFiles(rootPath, ["*.sln", "*.csproj", "*.slnf"]),
            ConfigurationFiles = FindFiles(rootPath, ["appsettings*.json", "*.config", "*.pubxml", ".env*", "package.json", "docker-compose*.yml", "docker-compose*.yaml"])
        };
    }

    private string NormalizeAndValidatePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Code repository path is required.", nameof(value));
        }

        var fullPath = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Code repository directory does not exist: {fullPath}");
        }

        if (!IsAllowedPath(fullPath))
        {
            throw new InvalidOperationException("The selected directory is outside the allowed code repository roots.");
        }

        return fullPath;
    }

    private bool IsAllowedPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return _allowedRoots.Any(root => fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ResolveAllowedRoots(IConfiguration configuration)
    {
        var configured = configuration.GetSection("CodeRepository:AllowedRoots").Get<string[]>() ?? [];
        var roots = configured
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roots.Count > 0)
        {
            return roots;
        }

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        var workspace = current.Parent?.Parent?.FullName ?? current.FullName;
        return [Path.GetFullPath(workspace)];
    }

    private static void AddMarker(HashSet<string> source, List<string> markers, string marker)
    {
        if (source.Contains(marker))
        {
            markers.Add(marker);
        }
    }

    private static string? ReadGitBranch(string rootPath)
    {
        var headPath = Path.Combine(rootPath, ".git", "HEAD");
        if (!File.Exists(headPath))
        {
            return null;
        }

        var head = File.ReadAllText(headPath).Trim();
        const string prefix = "ref: refs/heads/";
        return head.StartsWith(prefix, StringComparison.Ordinal) ? head[prefix.Length..] : null;
    }

    private static CodeRepositoryDto ToDto(AiCodeRepository entity, AiCodeProject? project = null)
    {
        var metadata = ReadMetadata(entity);
        return new CodeRepositoryDto
        {
            Id = entity.Id,
            ProjectId = entity.ProjectId,
            ProjectName = project?.DisplayName,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            RootPath = entity.RootPath,
            SourceType = entity.SourceType,
            Description = entity.Description,
            Status = entity.Status,
            Languages = metadata.Languages,
            BuildSystems = metadata.BuildSystems,
            SolutionFiles = metadata.SolutionFiles,
            ConfigurationFiles = metadata.ConfigurationFiles,
            PublishTarget = metadata.PublishTarget,
            PublishConfiguration = metadata.PublishConfiguration,
            PublishRuntime = metadata.PublishRuntime,
            PublishOutputPath = metadata.PublishOutputPath,
            PublishCommand = metadata.PublishCommand,
            IsGitRepository = metadata.IsGitRepository,
            Branch = metadata.Branch,
            LastScannedAt = entity.LastScannedAt,
            LastIndexedAt = entity.LastIndexedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static CodeRepositoryMetadata ReadMetadata(AiCodeRepository entity) => string.IsNullOrWhiteSpace(entity.TechStackJson)
        ? new CodeRepositoryMetadata()
        : JsonSerializer.Deserialize<CodeRepositoryMetadata>(entity.TechStackJson, JsonOptions) ?? new CodeRepositoryMetadata();

    private static CodeRepositoryMetadata CreateMetadata(CodeRepositorySaveRequest request, CodeRepositoryInspectionDto inspection, CodeRepositoryMetadata? existing = null)
    {
        var solutionFiles = NormalizeSelectedFiles(inspection.RootPath, request.SolutionFiles);
        var configurationFiles = NormalizeSelectedFiles(inspection.RootPath, request.ConfigurationFiles);
        var languages = NormalizeSelection(request.Languages, inspection.Languages);
        var isNpmProject = languages.Contains("TypeScript/JavaScript", StringComparer.OrdinalIgnoreCase) || languages.Contains("React", StringComparer.OrdinalIgnoreCase);
        var publishFiles = isNpmProject
            ? configurationFiles.Where(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase)).ToList()
            : solutionFiles;
        var publishTarget = NormalizePublishTarget(request.PublishTarget, publishFiles, existing?.PublishTarget);
        return new CodeRepositoryMetadata
        {
            Languages = languages,
            BuildSystems = inspection.BuildSystems,
            IsGitRepository = inspection.IsGitRepository,
            Branch = inspection.Branch,
            MarkerFiles = inspection.MarkerFiles,
            SolutionFiles = solutionFiles,
            ConfigurationFiles = configurationFiles,
            PublishTarget = publishTarget,
            PublishConfiguration = NormalizeBuildConfiguration(request.PublishConfiguration, existing?.PublishConfiguration),
            PublishRuntime = NormalizeOptional(request.PublishRuntime) ?? existing?.PublishRuntime,
            PublishOutputPath = NormalizePublishOutputPath(request.PublishOutputPath, existing?.PublishOutputPath),
            PublishCommand = isNpmProject ? NormalizeNpmBuildCommand(request.PublishCommand, existing?.PublishCommand) : null
        };
    }

    private static List<CodeRepositoryFileHealth> CheckFiles(string rootPath, IEnumerable<string> paths) => paths.Select(path => new CodeRepositoryFileHealth
    {
        Path = path,
        Exists = File.Exists(Path.Combine(rootPath, path.Replace('/', Path.DirectorySeparatorChar)))
    }).ToList();

    private static string NormalizeConfiguredPath(string rootPath, string value, IReadOnlyCollection<string> configured)
    {
        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (!configured.Contains(normalized, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Only a selected configuration file can be edited.");
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
        if (!IsPathWithin(rootPath, fullPath) || !File.Exists(fullPath)) throw new FileNotFoundException("The configured file does not exist.");
        return normalized;
    }

    private static string ComputeSha256(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static CodeProjectDto ToProjectDto(AiCodeProject project, List<CodeRepositoryDto> repositories) => new()
    {
        Id = project.Id,
        Name = project.Name,
        DisplayName = project.DisplayName,
        RootPath = project.RootPath,
        Description = project.Description,
        Repositories = repositories,
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt
    };

    private static void EnsureProjectContainsRepository(AiCodeProject? project, string repositoryPath)
    {
        if (project is not null && !IsPathWithin(project.RootPath, repositoryPath))
        {
            throw new InvalidOperationException("A code repository must be located inside its selected project folder.");
        }
    }

    private static bool IsPathWithin(string parentPath, string childPath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeSelection(List<string>? requested, List<string> detected)
    {
        var source = requested is { Count: > 0 } ? requested : detected;
        return source.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    private static List<string> NormalizeSelectedFiles(string rootPath, List<string>? requested)
    {
        var source = requested ?? [];
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var result = new List<string>();
        foreach (var value in source.Where(value => !string.IsNullOrWhiteSpace(value)).Take(50))
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, value.Trim()));
            if (!IsPathWithin(root, fullPath) || !File.Exists(fullPath)) continue;
            result.Add(Path.GetRelativePath(root, fullPath).Replace('\\', '/'));
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? NormalizePublishTarget(string? requested, IReadOnlyCollection<string> selectedFiles, string? existing)
    {
        var candidate = NormalizeOptional(requested) ?? NormalizeOptional(existing);
        if (!string.IsNullOrWhiteSpace(candidate) && selectedFiles.Contains(candidate.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
            return candidate.Replace('\\', '/');
        return selectedFiles.FirstOrDefault(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            ?? selectedFiles.FirstOrDefault();
    }

    private static string NormalizeBuildConfiguration(string? requested, string? existing)
    {
        var value = NormalizeOptional(requested) ?? NormalizeOptional(existing) ?? "Release";
        if (!value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
            throw new InvalidOperationException("Publish configuration may only contain letters, numbers, '-' and '_'.");
        return value[..Math.Min(value.Length, 64)];
    }

    private static string NormalizeNpmBuildCommand(string? requested, string? existing)
    {
        var value = NormalizeOptional(requested) ?? NormalizeOptional(existing) ?? "npm run build";
        var arguments = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (arguments.Length < 3 || !arguments[0].Equals("npm", StringComparison.OrdinalIgnoreCase) || !arguments[1].Equals("run", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("React build command must use the format: npm run <script>.");
        if (arguments.Length > 20 || arguments.Any(argument => argument.Length > 128 || argument.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '.' or '/' or '='))))
            throw new InvalidOperationException("React build command contains unsupported characters.");
        return string.Join(' ', arguments);
    }

    private static string NormalizePublishOutputPath(string? requested, string? existing)
    {
        var value = (NormalizeOptional(requested) ?? NormalizeOptional(existing) ?? "artifacts/publish").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(value) || value.Split('/').Any(part => part is "." or ".."))
            throw new InvalidOperationException("Publish output path must be a safe repository-relative path.");
        return value;
    }

    private static List<string> FindFiles(string rootPath, IReadOnlyList<string> patterns)
    {
        var result = new List<string>();
        foreach (var pattern in patterns)
        {
            try
            {
                result.AddRange(Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
                    .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is ".git" or "node_modules" or "bin" or "obj"))
                    .Select(path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'))
                    .Take(60));
            }
            catch (UnauthorizedAccessException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
    }

    private static bool IsIgnoredDirectory(string path) => Path.GetFileName(path) is ".git" or "node_modules" or "bin" or "obj";

    private static bool IsSelectableFile(string path, string kind)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(kind, "package", StringComparison.OrdinalIgnoreCase))
            return name.Equals("package.json", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(kind, "solution", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(name);
            return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnf", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(kind, "configuration", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Unsupported file selection kind.", nameof(kind));
        }

        return name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pubxml", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeName(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var chars = source.ToLowerInvariant()
            .Select(x => char.IsLetterOrDigit(x) ? x : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? "repository" : normalized[..Math.Min(normalized.Length, 128)];
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
    }

    private sealed class CodeRepositoryMetadata
    {
        public List<string> Languages { get; set; } = [];
        public List<string> BuildSystems { get; set; } = [];
        public bool IsGitRepository { get; set; }
        public string? Branch { get; set; }
        public List<string> MarkerFiles { get; set; } = [];
        public List<string> SolutionFiles { get; set; } = [];
        public List<string> ConfigurationFiles { get; set; } = [];
        public string? PublishTarget { get; set; }
        public string PublishConfiguration { get; set; } = "Release";
        public string? PublishRuntime { get; set; }
        public string PublishOutputPath { get; set; } = "artifacts/publish";
        public string? PublishCommand { get; set; }
    }
}
