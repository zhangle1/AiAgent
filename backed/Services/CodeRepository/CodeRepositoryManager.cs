using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Entities.CodeRepository;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

/// <summary>
/// Manages registered local source-code repositories and safe directory inspection.
/// </summary>
public interface ICodeRepositoryManager
{
    List<CodeRepositoryDto> List();

    CodeRepositoryDirectoryBrowserDto Browse(string? path);

    CodeRepositoryInspectionDto Inspect(string rootPath);

    CodeRepositoryDto Create(CodeRepositorySaveRequest request);

    CodeRepositoryDto Update(string name, CodeRepositorySaveRequest request);

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
        return _db.Queryable<AiCodeRepository>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt)
            .OrderByDescending(x => x.CreatedAt)
            .ToList()
            .Select(ToDto)
            .ToList();
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
        if (_db.Queryable<AiCodeRepository>().Any(x => x.Name == name && !x.IsDeleted))
        {
            throw new InvalidOperationException($"Code repository '{name}' already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new AiCodeRepository
        {
            Name = name,
            DisplayName = FirstNonEmpty(request.DisplayName, inspection.SuggestedDisplayName, name),
            RootPath = inspection.RootPath,
            Description = NormalizeOptional(request.Description),
            Status = "configured",
            TechStackJson = JsonSerializer.Serialize(new CodeRepositoryMetadata
            {
                Languages = inspection.Languages,
                BuildSystems = inspection.BuildSystems,
                IsGitRepository = inspection.IsGitRepository,
                Branch = inspection.Branch,
                MarkerFiles = inspection.MarkerFiles
            }, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            LastScannedAt = now
        };

        entity.Id = _db.Insertable(entity).ExecuteReturnIdentity();
        return ToDto(entity);
    }

    /// <summary>
    /// Updates repository settings and refreshes the detected metadata for its selected directory.
    /// </summary>
    public CodeRepositoryDto Update(string name, CodeRepositorySaveRequest request)
    {
        var entity = Find(name);
        var inspection = Inspect(request.RootPath);
        var now = DateTime.UtcNow;
        entity.DisplayName = FirstNonEmpty(request.DisplayName, entity.DisplayName, inspection.SuggestedDisplayName, entity.Name);
        entity.RootPath = inspection.RootPath;
        entity.Description = NormalizeOptional(request.Description);
        entity.Status = "configured";
        entity.TechStackJson = JsonSerializer.Serialize(new CodeRepositoryMetadata
        {
            Languages = inspection.Languages,
            BuildSystems = inspection.BuildSystems,
            IsGitRepository = inspection.IsGitRepository,
            Branch = inspection.Branch,
            MarkerFiles = inspection.MarkerFiles
        }, JsonOptions);
        entity.UpdatedAt = now;
        entity.LastScannedAt = now;

        _db.Updateable(entity).ExecuteCommand();
        return ToDto(entity);
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

    private AiCodeRepository Find(string name)
    {
        var normalized = NormalizeName(name, string.Empty);
        var entity = _db.Queryable<AiCodeRepository>()
            .Where(x => x.Name == normalized && !x.IsDeleted)
            .First();
        return entity ?? throw new InvalidOperationException($"Code repository '{name}' does not exist.");
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
            MarkerFiles = markerFiles
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

    private static CodeRepositoryDto ToDto(AiCodeRepository entity)
    {
        var metadata = string.IsNullOrWhiteSpace(entity.TechStackJson)
            ? new CodeRepositoryMetadata()
            : JsonSerializer.Deserialize<CodeRepositoryMetadata>(entity.TechStackJson, JsonOptions) ?? new CodeRepositoryMetadata();
        return new CodeRepositoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            RootPath = entity.RootPath,
            SourceType = entity.SourceType,
            Description = entity.Description,
            Status = entity.Status,
            Languages = metadata.Languages,
            BuildSystems = metadata.BuildSystems,
            IsGitRepository = metadata.IsGitRepository,
            Branch = metadata.Branch,
            LastScannedAt = entity.LastScannedAt,
            LastIndexedAt = entity.LastIndexedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
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
    }
}