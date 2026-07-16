using AiAgent.Backend.Dtos.DashboardApp;
using AiAgent.Backend.Entities.CodeRepository;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.DashboardApp;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/dashboard-applications")]
public sealed class DashboardApplicationAppService : IDynamicApiController
{
    private readonly IDashboardApplicationWorkspace _workspace;

    public DashboardApplicationAppService(IDashboardApplicationWorkspace workspace) => _workspace = workspace;

    [HttpGet("list")] public Task<IReadOnlyList<DashboardApplicationDto>> List(CancellationToken cancellationToken) => _workspace.ListAsync(cancellationToken);
    [HttpGet("repositories")] public Task<IReadOnlyList<object>> Repositories(CancellationToken cancellationToken) => _workspace.RepositoriesAsync(cancellationToken);
    [HttpGet("templates")] public Task<IReadOnlyList<object>> Templates(CancellationToken cancellationToken) => _workspace.TemplatesAsync(cancellationToken);
    [HttpPost("")] public Task<DashboardApplicationDto> Create([FromBody] DashboardApplicationCreateRequest request, CancellationToken cancellationToken) => _workspace.CreateAsync(request, cancellationToken);
    [HttpPost("{id}/repository")] public Task<DashboardApplicationDto> BindRepository([FromRoute] string id, [FromBody] DashboardApplicationRepositoryBindRequest request, CancellationToken cancellationToken) => _workspace.BindRepositoryAsync(id, request, cancellationToken);
    [HttpDelete("{id}")] public Task<object> Delete([FromRoute] string id, CancellationToken cancellationToken) => _workspace.DeleteAsync(id, cancellationToken);
    [HttpGet("{id}")] public Task<DashboardApplicationDto> Get([FromRoute] string id, CancellationToken cancellationToken) => _workspace.GetAsync(id, cancellationToken);
    [HttpGet("{id}/tree")] public Task<object> Tree([FromRoute] string id, [FromQuery] string? path, CancellationToken cancellationToken) => _workspace.TreeAsync(id, path, cancellationToken);
    [HttpGet("{id}/inspect")] public Task<object> Inspect([FromRoute] string id, CancellationToken cancellationToken) => _workspace.InspectAsync(id, cancellationToken);
    [HttpGet("{id}/search")] public Task<object> Search([FromRoute] string id, [FromQuery] string query, CancellationToken cancellationToken) => _workspace.SearchAsync(id, query, cancellationToken);
    [HttpGet("{id}/file")] public Task<object> File([FromRoute] string id, [FromQuery] string path, CancellationToken cancellationToken) => _workspace.ReadFileAsync(id, path, cancellationToken);
    [HttpPut("{id}/file")] public Task<object> WriteFile([FromRoute] string id, [FromBody] DashboardFileWriteRequest request, CancellationToken cancellationToken) => _workspace.WriteFileAsync(id, request, cancellationToken);
    [HttpPost("{id}/file/patch")] public Task<object> PatchFile([FromRoute] string id, [FromBody] DashboardFilePatchRequest request, CancellationToken cancellationToken) => _workspace.ApplyPatchAsync(id, request, cancellationToken);
    [HttpPost("{id}/file/validate")] public Task<object> ValidateFile([FromRoute] string id, [FromBody] DashboardChangeValidationRequest request, CancellationToken cancellationToken) => _workspace.ValidateChangeAsync(id, request, cancellationToken);
    [HttpPost("{id}/runtime/start")] public Task<object> StartRuntime([FromRoute] string id, [FromServices] IDashboardRuntimeService runtime, CancellationToken cancellationToken) => runtime.StartAsync(id, cancellationToken);
    [HttpPost("{id}/runtime/stop")] public Task<object> StopRuntime([FromRoute] string id, [FromServices] IDashboardRuntimeService runtime, CancellationToken cancellationToken) => runtime.StopAsync(id, cancellationToken);
    [HttpGet("{id}/runtime")] public Task<object> Runtime([FromRoute] string id, [FromServices] IDashboardRuntimeService runtime, CancellationToken cancellationToken) => runtime.StatusAsync(id, cancellationToken);
    [HttpGet("{id}/git/status")] public Task<object> GitStatus([FromRoute] string id, [FromServices] IDashboardGitService git, CancellationToken cancellationToken) => git.StatusAsync(id, cancellationToken);
    [HttpPost("{id}/git/pull")] public Task<object> GitPull([FromRoute] string id, [FromServices] IDashboardGitService git, CancellationToken cancellationToken) => git.PullAsync(id, cancellationToken);
    [HttpPost("{id}/git/push")] public Task<object> GitPush([FromRoute] string id, [FromBody] DashboardGitPushRequest request, [FromServices] IDashboardGitService git, CancellationToken cancellationToken) => git.CommitAndPushAsync(id, request, cancellationToken);
}

public sealed class DashboardApplicationDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("root_path")] public string RootPath { get; set; } = string.Empty;
    [JsonPropertyName("repository_name")] public string? RepositoryName { get; set; }
    [JsonPropertyName("template_id")] public string? TemplateId { get; set; }
    [JsonPropertyName("is_case_library")] public bool IsCaseLibrary { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }
}

public interface IDashboardApplicationWorkspace
{
    Task<IReadOnlyList<DashboardApplicationDto>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<object>> RepositoriesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<object>> TemplatesAsync(CancellationToken cancellationToken);
    Task<DashboardApplicationDto> CreateAsync(DashboardApplicationCreateRequest request, CancellationToken cancellationToken);
    Task<DashboardApplicationDto> BindRepositoryAsync(string id, DashboardApplicationRepositoryBindRequest request, CancellationToken cancellationToken);
    Task<object> DeleteAsync(string id, CancellationToken cancellationToken);
    Task<DashboardApplicationDto> GetAsync(string id, CancellationToken cancellationToken);
    Task<object> TreeAsync(string id, string? path, CancellationToken cancellationToken);
    Task<object> InspectAsync(string id, CancellationToken cancellationToken);
    Task<object> SearchAsync(string id, string query, CancellationToken cancellationToken);
    Task<object> ReadFileAsync(string id, string path, CancellationToken cancellationToken);
    Task<object> WriteFileAsync(string id, DashboardFileWriteRequest request, CancellationToken cancellationToken);
    Task<object> ApplyPatchAsync(string id, DashboardFilePatchRequest request, CancellationToken cancellationToken);
    Task<object> ValidateChangeAsync(string id, DashboardChangeValidationRequest request, CancellationToken cancellationToken);
}

public sealed class DashboardWorkspaceSnapshot
{
    public string ApplicationId { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string Framework { get; set; } = "unknown";
    public List<string> EntryPoints { get; set; } = [];
    public List<string> SourceFiles { get; set; } = [];
    public List<string> StyleFiles { get; set; } = [];
    public List<WorkspaceFileEntry> Files { get; set; } = [];
    public Dictionary<string, List<string>> Imports { get; set; } = [];
    public List<DashboardVisualTarget> VisualTargets { get; set; } = [];
}

public sealed record WorkspaceFileEntry(string Path, long Size, DateTime UpdatedAt);
public sealed record DashboardVisualTarget(string File, string Role, string Detail);

/// <summary>Owns app-to-repository bindings and prevents editor requests from escaping their selected server workspace.</summary>
public sealed class DashboardApplicationWorkspace : IDashboardApplicationWorkspace
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", "dist", "build", ".next" };
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".html", ".css", ".js", ".jsx", ".ts", ".tsx", ".json", ".md", ".yml", ".yaml", ".cs", ".csproj" };
    private readonly ISqlSugarClient _db;
    private readonly string _dataPath;
    private readonly string _storePath;
    private readonly SemaphoreSlim _storeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DashboardApplicationWorkspace(ISqlSugarClient db, IConfiguration configuration)
    {
        _db = db;
        var configured = configuration["DataPath"] ?? "data";
        _dataPath = Path.GetFullPath(configured);
        _storePath = Path.Combine(_dataPath, "dashboard-applications.json");
    }

    public async Task<IReadOnlyList<DashboardApplicationDto>> ListAsync(CancellationToken cancellationToken)
    {
        return (await ReadStoreAsync(cancellationToken)).OrderByDescending(x => x.UpdatedAt).ToList();
    }

    public Task<IReadOnlyList<object>> RepositoriesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<object> rows = _db.Queryable<AiCodeRepository>().Where(x => !x.IsDeleted).OrderByDescending(x => x.UpdatedAt).ToList()
            .Select(x => (object)new { name = x.Name, display_name = x.DisplayName, root_path = x.RootPath, status = x.Status }).ToList();
        return Task.FromResult(rows);
    }

    public Task<IReadOnlyList<object>> TemplatesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<object> templates = GetTemplates().Select(template => (object)new { id = template.Id, name = template.Name, description = template.Description, technology = template.Technology }).ToList();
        return Task.FromResult(templates);
    }

    public async Task<DashboardApplicationDto> CreateAsync(DashboardApplicationCreateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Application name is required.");
        var template = GetTemplates().FirstOrDefault(item => string.Equals(item.Id, request.TemplateId?.Trim(), StringComparison.OrdinalIgnoreCase));
        var repositoryName = request.RepositoryName?.Trim();
        AiCodeRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            repository = _db.Queryable<AiCodeRepository>().First(x => x.Name == repositoryName && !x.IsDeleted)
                ?? throw new InvalidOperationException("The selected code repository was not found.");
        }
        else if (template is null) throw new ArgumentException("Choose a template or code repository before creating an application.");
        var appId = Guid.NewGuid().ToString("N");
        var rootPath = template is null
            ? Path.GetFullPath(repository!.RootPath)
            : await CreateTemplateWorkspaceAsync(template, appId, repository?.RootPath, cancellationToken);
        var app = new DashboardApplicationDto
        {
            Id = appId, Name = request.Name.Trim()[..Math.Min(128, request.Name.Trim().Length)],
            Description = template is null ? $"Bound to {repository!.DisplayName}" : template.Description, RootPath = rootPath, RepositoryName = repository?.Name, TemplateId = template?.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var apps = (await ReadStoreAsync(cancellationToken)).ToList();
        apps.Add(app);
        await WriteStoreAsync(apps, cancellationToken);
        return app;
    }

    public async Task<DashboardApplicationDto> BindRepositoryAsync(string id, DashboardApplicationRepositoryBindRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RepositoryName)) throw new ArgumentException("A code repository is required.");
        var repository = _db.Queryable<AiCodeRepository>().First(x => x.Name == request.RepositoryName.Trim() && !x.IsDeleted)
            ?? throw new InvalidOperationException("The selected code repository was not found.");
        var apps = (await ReadStoreAsync(cancellationToken)).ToList();
        var index = apps.FindIndex(x => x.Id == id);
        if (index < 0) throw new FileNotFoundException("Dashboard application was not found.");
        var app = apps[index];
        var sourceRoot = Path.GetFullPath(app.RootPath);
        var targetRoot = GetRepositoryWorkspaceRoot(repository.RootPath, app.Id);
        if (sourceRoot.Equals(targetRoot, StringComparison.OrdinalIgnoreCase)) return app;
        if (Directory.Exists(targetRoot)) throw new InvalidOperationException("The target dashboard workspace already exists in the selected repository.");
        try
        {
            await CopyWorkspaceAsync(sourceRoot, targetRoot, cancellationToken);
            app.RootPath = targetRoot;
            app.RepositoryName = repository.Name;
            app.Description = $"Bound to {repository.DisplayName}";
            app.UpdatedAt = DateTime.UtcNow;
            apps[index] = app;
            await WriteStoreAsync(apps, cancellationToken);
            return app;
        }
        catch
        {
            if (Directory.Exists(targetRoot)) Directory.Delete(targetRoot, true);
            throw;
        }
    }

    public async Task<DashboardApplicationDto> GetAsync(string id, CancellationToken cancellationToken) => await FindAsync(id, cancellationToken);

    public async Task<object> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var apps = (await ReadStoreAsync(cancellationToken)).ToList();
        var index = apps.FindIndex(item => item.Id == id);
        if (index < 0) throw new FileNotFoundException("Dashboard application was not found.");
        var app = apps[index];
        var rootPath = Path.GetFullPath(app.RootPath);
        if (!string.IsNullOrWhiteSpace(app.TemplateId) && IsManagedWorkspace(rootPath) && Directory.Exists(rootPath)) Directory.Delete(rootPath, true);
        apps.RemoveAt(index);
        await WriteStoreAsync(apps, cancellationToken);
        return new { ok = true, id };
    }

    public async Task<object> TreeAsync(string id, string? path, CancellationToken cancellationToken)
    {
        var app = await FindAsync(id, cancellationToken);
        var directory = ResolvePath(app, path, true);
        return new
        {
            path = Path.GetRelativePath(app.RootPath, directory),
            directories = Directory.EnumerateDirectories(directory).Where(x => !IgnoredDirectories.Contains(Path.GetFileName(x))).OrderBy(x => x).Take(200)
                .Select(x => new { name = Path.GetFileName(x), path = Path.GetRelativePath(app.RootPath, x) }).ToList(),
            files = Directory.EnumerateFiles(directory).Where(x => !x.EndsWith(".aiagent.tmp", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x).Take(500)
                .Select(x => new { name = Path.GetFileName(x), path = Path.GetRelativePath(app.RootPath, x), extension = Path.GetExtension(x), size = new FileInfo(x).Length, editable = EditableExtensions.Contains(Path.GetExtension(x)) }).ToList()
        };
    }

    public async Task<object> InspectAsync(string id, CancellationToken cancellationToken)
    {
        var app = await FindAsync(id, cancellationToken);
        return await BuildWorkspaceSnapshotAsync(app, cancellationToken);
    }

    public async Task<object> SearchAsync(string id, string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A workspace search query is required.");
        var app = await FindAsync(id, cancellationToken);
        var normalizedQuery = query.Trim()[..Math.Min(256, query.Trim().Length)];
        var hits = new List<object>();
        foreach (var filePath in EnumerateWorkspaceFiles(app.RootPath).Take(800))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(filePath);
            if (info.Length > 1024 * 1024 || !EditableExtensions.Contains(info.Extension)) continue;
            var content = await File.ReadAllTextAsync(filePath, DetectTextEncoding(filePath), cancellationToken);
            var offset = content.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            if (offset < 0) continue;
            var line = content.Take(offset).Count(character => character == '\n') + 1;
            var start = Math.Max(0, offset - 180);
            var length = Math.Min(content.Length - start, normalizedQuery.Length + 360);
            hits.Add(new
            {
                path = NormalizeRelativePath(Path.GetRelativePath(app.RootPath, filePath)),
                line,
                snippet = content.Substring(start, length),
                sha256 = ComputeSha256(content)
            });
            if (hits.Count >= 30) break;
        }
        return new { query = normalizedQuery, workspace_revision = await ComputeWorkspaceRevisionAsync(app.RootPath, cancellationToken), hits };
    }

    public async Task<object> ReadFileAsync(string id, string path, CancellationToken cancellationToken)
    {
        var app = await FindAsync(id, cancellationToken);
        var filePath = ResolvePath(app, path, false);
        var info = new FileInfo(filePath);
        if (info.Length > 1024 * 1024 || !EditableExtensions.Contains(info.Extension)) throw new InvalidOperationException("Only supported text files up to 1 MB can be opened.");
        var encoding = DetectTextEncoding(filePath);
        var content = await File.ReadAllTextAsync(filePath, encoding, cancellationToken);
        return new { path = NormalizeRelativePath(Path.GetRelativePath(app.RootPath, filePath)), extension = info.Extension, content, line_count = content.Count(x => x == '\n') + 1, sha256 = ComputeSha256(content), updated_at = info.LastWriteTimeUtc };
    }

    public async Task<object> WriteFileAsync(string id, DashboardFileWriteRequest request, CancellationToken cancellationToken)
    {
        var app = await FindAsync(id, cancellationToken);
        var filePath = ResolveWritePath(app, request.Path);
        if (!EditableExtensions.Contains(Path.GetExtension(filePath))) throw new InvalidOperationException("This file type cannot be edited in the workspace.");
        if (Encoding.UTF8.GetByteCount(request.Content) > 1024 * 1024) throw new InvalidOperationException("Edited file content must not exceed 1 MB.");
        var encoding = File.Exists(filePath) ? DetectTextEncoding(filePath) : new UTF8Encoding(false);
        var temporary = filePath + ".aiagent.tmp";
        await File.WriteAllTextAsync(temporary, request.Content, encoding, cancellationToken);
        File.Move(temporary, filePath, true);
        app.UpdatedAt = DateTime.UtcNow;
        var apps = (await ReadStoreAsync(cancellationToken)).ToList();
        var index = apps.FindIndex(x => x.Id == app.Id);
        if (index >= 0) { apps[index] = app; await WriteStoreAsync(apps, cancellationToken); }
        return new { ok = true, path = NormalizeRelativePath(request.Path), sha256 = ComputeSha256(request.Content), updated_at = app.UpdatedAt };
    }

    public async Task<object> ApplyPatchAsync(string id, DashboardFilePatchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("A patch path is required.");
        if (request.Content is null && string.IsNullOrEmpty(request.Search)) throw new ArgumentException("Patch search text or complete replacement content is required.");
        var app = await FindAsync(id, cancellationToken);
        var filePath = ResolvePath(app, request.Path, false);
        if (!EditableExtensions.Contains(Path.GetExtension(filePath))) throw new InvalidOperationException("This file type cannot be edited in the workspace.");
        var encoding = DetectTextEncoding(filePath);
        var original = await File.ReadAllTextAsync(filePath, encoding, cancellationToken);
        var actualHash = ComputeSha256(original);
        if (!string.Equals(actualHash, request.ExpectedSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The file changed after it was read. Read it again before applying a patch.");
        string updated;
        if (request.Content is null)
        {
            var matches = CountOccurrences(original, request.Search);
            if (matches != 1) throw new InvalidOperationException($"Patch search text must match exactly one location; found {matches} matches.");
            updated = original.Replace(request.Search, request.Replace ?? string.Empty, StringComparison.Ordinal);
        }
        else updated = request.Content;
        if (Encoding.UTF8.GetByteCount(updated) > 1024 * 1024) throw new InvalidOperationException("Edited file content must not exceed 1 MB.");
        await WriteFileAsync(id, new DashboardFileWriteRequest { Path = request.Path, Content = updated }, cancellationToken);
        return new
        {
            ok = true,
            path = NormalizeRelativePath(request.Path),
            previous_sha256 = actualHash,
            sha256 = ComputeSha256(updated),
            added_characters = Math.Max(0, updated.Length - original.Length),
            removed_characters = Math.Max(0, original.Length - updated.Length),
            workspace_revision = await ComputeWorkspaceRevisionAsync(app.RootPath, cancellationToken)
        };
    }

    public async Task<object> ValidateChangeAsync(string id, DashboardChangeValidationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("A validation path is required.");
        var app = await FindAsync(id, cancellationToken);
        var filePath = ResolvePath(app, request.Path, false);
        var content = await File.ReadAllTextAsync(filePath, DetectTextEncoding(filePath), cancellationToken);
        var snapshot = await BuildWorkspaceSnapshotAsync(app, cancellationToken);
        var normalizedPath = NormalizeRelativePath(Path.GetRelativePath(app.RootPath, filePath));
        var expectedFound = string.IsNullOrWhiteSpace(request.ExpectedContains) || content.Contains(request.ExpectedContains, StringComparison.Ordinal);
        var isKnownSource = snapshot.SourceFiles.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase)
            || snapshot.StyleFiles.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase)
            || snapshot.EntryPoints.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase);
        var importProblems = FindMissingLocalImports(app.RootPath, normalizedPath, content).ToList();
        return new
        {
            ok = expectedFound && isKnownSource && importProblems.Count == 0,
            path = normalizedPath,
            sha256 = ComputeSha256(content),
            expected_contains = request.ExpectedContains,
            expected_found = expectedFound,
            is_known_source = isKnownSource,
            import_problems = importProblems,
            workspace_revision = snapshot.Revision
        };
    }

    private async Task EnsureCaseLibraryAsync(CancellationToken cancellationToken)
    {
        if (_db.Queryable<AiCodeRepository>().Any(x => !x.IsDeleted)) return;
        var apps = (await ReadStoreAsync(cancellationToken)).ToList();
        if (apps.Any(x => x.IsCaseLibrary)) return;
        var root = Path.Combine(_dataPath, "dashboard-samples", "operations-board");
        Directory.CreateDirectory(root);
        var index = Path.Combine(root, "index.html");
        if (!File.Exists(index)) await File.WriteAllTextAsync(index, CaseIndexHtml.Replace("\\\"", "\"", StringComparison.Ordinal), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# 生产运营看板案例\n\n这是未检测到代码库时自动加载的可编辑案例。", new UTF8Encoding(false), cancellationToken);
        apps.Add(new DashboardApplicationDto { Id = "case-operations-board", Name = "生产运营看板案例", Description = "可编辑的静态 React 风格运营看板案例", RootPath = root, IsCaseLibrary = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await WriteStoreAsync(apps, cancellationToken);
    }

    private async Task<DashboardApplicationDto> FindAsync(string id, CancellationToken cancellationToken)
        => (await ReadStoreAsync(cancellationToken)).FirstOrDefault(x => x.Id == id) ?? throw new FileNotFoundException("Dashboard application was not found.");

    private async Task<List<DashboardApplicationDto>> ReadStoreAsync(CancellationToken cancellationToken)
    {
         await _storeLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_storePath)) return [];
            await using var stream = File.OpenRead(_storePath);
            return await JsonSerializer.DeserializeAsync<List<DashboardApplicationDto>>(stream, _jsonOptions, cancellationToken) ?? [];
        }
        finally { _storeLock.Release(); }
    }

    private async Task WriteStoreAsync(List<DashboardApplicationDto> apps, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataPath);
        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            var temporary = _storePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(apps, _jsonOptions), new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, _storePath, true);
        }
        finally { _storeLock.Release(); }
    }

    private static string ResolvePath(DashboardApplicationDto app, string? relativePath, bool directory)
    {
        var root = Path.GetFullPath(app.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) && !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path is outside the dashboard application workspace.");
        if (directory ? !Directory.Exists(fullPath) : !File.Exists(fullPath)) throw new FileNotFoundException("Workspace file or directory does not exist.");
        return fullPath;
    }

    private static string ResolveWritePath(DashboardApplicationDto app, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A target file path is required.");
        var fullPath = ResolvePathInsideRoot(app, relativePath);
        if (IgnoredDirectories.Overlaps(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\')))
            throw new InvalidOperationException("Generated files cannot be written into ignored directories.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return fullPath;
    }

    private static string ResolvePathInsideRoot(DashboardApplicationDto app, string relativePath)
    {
        var root = Path.GetFullPath(app.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path is outside the dashboard application workspace.");
        return fullPath;
    }

    private async Task<DashboardWorkspaceSnapshot> BuildWorkspaceSnapshotAsync(DashboardApplicationDto app, CancellationToken cancellationToken)
    {
        var files = EnumerateWorkspaceFiles(app.RootPath)
            .Select(path => new WorkspaceFileEntry(NormalizeRelativePath(Path.GetRelativePath(app.RootPath, path)), new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)))
            .Take(800)
            .ToList();
        var sourceFiles = files.Where(file => IsSourceExtension(Path.GetExtension(file.Path))).Select(file => file.Path).ToList();
        var styleFiles = files.Where(file => string.Equals(Path.GetExtension(file.Path), ".css", StringComparison.OrdinalIgnoreCase)).Select(file => file.Path).ToList();
        var entrypoints = new List<string>();
        if (files.Any(file => string.Equals(file.Path, "index.html", StringComparison.OrdinalIgnoreCase))) entrypoints.Add("index.html");
        foreach (var source in sourceFiles)
        {
            var path = Path.Combine(app.RootPath, source);
            if (Path.GetFileNameWithoutExtension(source).Equals("main", StringComparison.OrdinalIgnoreCase) || await ContainsAnyAsync(path, ["createRoot(", "ReactDOM.render(", "createApp("], cancellationToken))
                entrypoints.Add(source);
        }
        entrypoints = entrypoints.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var imports = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var visualTargets = new List<DashboardVisualTarget>();
        foreach (var source in sourceFiles.Take(120))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(app.RootPath, source);
            var content = await File.ReadAllTextAsync(fullPath, DetectTextEncoding(fullPath), cancellationToken);
            imports[source] = ExtractLocalImports(source, content).ToList();
            if (content.Contains("echarts", StringComparison.OrdinalIgnoreCase) || content.Contains("series", StringComparison.OrdinalIgnoreCase))
                visualTargets.Add(new DashboardVisualTarget(source, "chart-configuration", "ECharts or chart series detected"));
            if (content.Contains("function App", StringComparison.Ordinal) || content.Contains("const App", StringComparison.Ordinal) || content.Contains("export default", StringComparison.Ordinal))
                visualTargets.Add(new DashboardVisualTarget(source, "page-component", "Dashboard page component detected"));
        }
        return new DashboardWorkspaceSnapshot
        {
            ApplicationId = app.Id,
            RootPath = app.RootPath,
            Revision = await ComputeWorkspaceRevisionAsync(app.RootPath, cancellationToken),
            Framework = await DetectFrameworkAsync(app.RootPath, cancellationToken),
            EntryPoints = entrypoints,
            SourceFiles = sourceFiles,
            StyleFiles = styleFiles,
            Files = files,
            Imports = imports,
            VisualTargets = visualTargets
        };
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException("The dashboard workspace does not exist.");
        return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".aiagent.tmp", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetRelativePath(rootPath, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\').Any(IgnoredDirectories.Contains));
    }

    private static bool IsSourceExtension(string extension)
        => extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vue", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svelte", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> ContainsAnyAsync(string path, IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > 1024 * 1024) return false;
        var content = await File.ReadAllTextAsync(path, DetectTextEncoding(path), cancellationToken);
        return values.Any(value => content.Contains(value, StringComparison.Ordinal));
    }

    private static IEnumerable<string> ExtractLocalImports(string sourcePath, string content)
    {
        var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        foreach (Match match in Regex.Matches(content, "(?:import|export)\\s+(?:[^;]*?\\s+from\\s+)?[\\\"'](?<path>[^\\\"']+)[\\\"']"))
        {
            var importPath = match.Groups["path"].Value;
            if (!importPath.StartsWith(".", StringComparison.Ordinal)) continue;
            yield return NormalizeRelativePath(Path.Combine(sourceDirectory, importPath));
        }
    }

    private static IEnumerable<string> FindMissingLocalImports(string rootPath, string sourcePath, string content)
    {
        var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        foreach (var importPath in ExtractLocalImports(sourcePath, content))
        {
            var candidate = Path.GetFullPath(Path.Combine(rootPath, importPath));
            var found = File.Exists(candidate)
                || new[] { ".js", ".jsx", ".ts", ".tsx", ".css", ".json" }.Any(extension => File.Exists(candidate + extension))
                || File.Exists(Path.Combine(candidate, "index.js"))
                || File.Exists(Path.Combine(candidate, "index.jsx"));
            if (!found) yield return importPath;
        }
    }

    private static async Task<string> DetectFrameworkAsync(string rootPath, CancellationToken cancellationToken)
    {
        var packagePath = Path.Combine(rootPath, "package.json");
        if (!File.Exists(packagePath)) return "unknown";
        var package = await File.ReadAllTextAsync(packagePath, DetectTextEncoding(packagePath), cancellationToken);
        if (package.Contains("\"vite\"", StringComparison.Ordinal) && package.Contains("\"react\"", StringComparison.Ordinal)) return "vite-react";
        if (package.Contains("\"next\"", StringComparison.Ordinal)) return "nextjs";
        if (package.Contains("\"vue\"", StringComparison.Ordinal)) return "vite-vue";
        return "javascript";
    }

    private static async Task<string> ComputeWorkspaceRevisionAsync(string rootPath, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var path in EnumerateWorkspaceFiles(rootPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(800))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            builder.Append(NormalizeRelativePath(Path.GetRelativePath(rootPath, path))).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        return ComputeSha256(builder.ToString());
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0) { count++; index += search.Length; }
        return count;
    }

    private static string ComputeSha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeRelativePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static Encoding DetectTextEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path).Take(3).ToArray();
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
        return bytes.Length == 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? new UTF8Encoding(true) : new UTF8Encoding(false, true);
    }

    private IReadOnlyList<DashboardTemplate> GetTemplates()
    {
        var root = ResolveTemplateRoot();
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root).Select(path => DashboardTemplate.TryCreate(path)).Where(item => item != null).Cast<DashboardTemplate>().ToList();
    }

    private string ResolveTemplateRoot()
    {
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "dashboard-templates"), Path.Combine(Directory.GetCurrentDirectory(), "dashboard-templates"), Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "dashboard-templates")) };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private async Task<string> CreateTemplateWorkspaceAsync(DashboardTemplate template, string appId, string? repositoryRoot, CancellationToken cancellationToken)
    {
        var root = string.IsNullOrWhiteSpace(repositoryRoot)
            ? Path.Combine(_dataPath, "dashboard-workspaces", appId)
            : GetRepositoryWorkspaceRoot(repositoryRoot, appId);
        await CopyWorkspaceAsync(template.Path, root, cancellationToken);
        return root;
    }

    private static string GetRepositoryWorkspaceRoot(string repositoryRoot, string appId)
        => Path.Combine(Path.GetFullPath(repositoryRoot), ".aiagent-dashboard", appId);

    private bool IsManagedWorkspace(string rootPath)
    {
        var localWorkspaceRoot = Path.Combine(_dataPath, "dashboard-workspaces") + Path.DirectorySeparatorChar;
        if (rootPath.StartsWith(localWorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return true;
        return rootPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => string.Equals(segment, ".aiagent-dashboard", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CopyWorkspaceAsync(string sourceRoot, string targetRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("The source dashboard workspace does not exist.");
        Directory.CreateDirectory(targetRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, source);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\').Any(IgnoredDirectories.Contains)) continue;
            var destination = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var from = File.OpenRead(source);
            await using var to = File.Create(destination);
            await from.CopyToAsync(to, cancellationToken);
        }
    }

    private sealed record DashboardTemplate(string Id, string Name, string Description, string Technology, string Path)
    {
        public static DashboardTemplate? TryCreate(string path)
        {
            var manifest = System.IO.Path.Combine(path, ".aiagent-template.json");
            if (!File.Exists(manifest)) return null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                return new DashboardTemplate(root.GetProperty("id").GetString() ?? string.Empty, root.GetProperty("name").GetString() ?? string.Empty, root.GetProperty("description").GetString() ?? string.Empty, root.GetProperty("technology").GetString() ?? "React", path);
            }
            catch { return null; }
        }
    }

    private const string CaseIndexHtml = """
<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>生产运营看板</title><style>body{margin:0;background:#f5f7fb;color:#18212f;font:14px Inter,Arial}.shell{padding:28px}.head{display:flex;justify-content:space-between;align-items:center}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin:22px 0}.card,.panel{background:#fff;border:1px solid #e7eaf0;border-radius:12px;padding:18px;box-shadow:0 8px 30px #17233a0a}.number{font-size:30px;font-weight:700;margin-top:8px}.green{color:#0d9f6e}.blue{color:#2563eb}.orange{color:#d97706}.bar{height:9px;background:#edf0f5;border-radius:99px;margin-top:15px}.bar i{display:block;height:100%;width:72%;background:#2563eb;border-radius:inherit}.row{display:grid;grid-template-columns:2fr 1fr;gap:14px}@media(max-width:700px){.grid,.row{grid-template-columns:1fr}}</style></head><body><main class=\"shell\"><header class=\"head\"><div><small>制造执行 · 实时概览</small><h1>生产运营看板</h1></div><button>刷新数据</button></header><section class=\"grid\"><article class=\"card\"><small>当日计划</small><div class=\"number blue\">1,280</div><div class=\"bar\"><i></i></div></article><article class=\"card\"><small>完成数量</small><div class=\"number green\">918</div><div class=\"bar\"><i style=\"width:72%;background:#0d9f6e\"></i></div></article><article class=\"card\"><small>异常工单</small><div class=\"number orange\">12</div><p>需要优先处理</p></article><article class=\"card\"><small>设备稼动率</small><div class=\"number\">86.4%</div><p>较昨日 +2.1%</p></article></section><section class=\"row\"><article class=\"panel\"><h3>产线完成趋势</h3><svg viewBox=\"0 0 600 180\" width=\"100%\"><polyline points=\"0,142 70,120 145,132 220,75 300,92 385,48 460,66 540,25 600,38\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"5\"/></svg></article><article class=\"panel\"><h3>待处理事项</h3><p>🔴 D01 设备保养</p><p>🟠 原料批次待确认</p><p>🟢 夜班排产已完成</p></article></section></main></body></html>
""";
}
