using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Services.Chat.Agentic;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.CodeRepository;

public interface ICodeRepositoryIndexService
{
    object BrowseTree(string repositoryName, string? relativePath);

    object ReadFile(string repositoryName, string relativePath);

    object Grep(string repositoryName, string query);

    Task<ToolResult> DescribeAsync(AgentContext context, CancellationToken cancellationToken);

    Task<int> IndexAsync(string repositoryName, CancellationToken cancellationToken);

    Task<ToolResult> SearchAsync(AgentContext context, string query, int topK, CancellationToken cancellationToken);

    Task<ToolResult> FindSymbolAsync(AgentContext context, string symbol, CancellationToken cancellationToken);
}

/// <summary>Read-only source scanner and lightweight file/symbol retrieval service.</summary>
public sealed class CodeRepositoryIndexService : ICodeRepositoryIndexService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", "node_modules", "bin", "obj", "dist", "build", ".next", "coverage", "target", "vendor" };

    private static readonly HashSet<string> IndexedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".rs", ".java", ".go", ".json", ".md", ".yml", ".yaml", ".csproj", ".sln" };

    private static readonly Regex SymbolPattern = new(@"\b(?:class|interface|struct|enum|record|namespace|function|def|fn|public|private|protected|internal|export)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private readonly ISqlSugarClient _db;
    private readonly ICodeRepositoryIndexProgressStore _progressStore;

    public CodeRepositoryIndexService(ISqlSugarClient db, ICodeRepositoryIndexProgressStore progressStore)
    { _db = db; _progressStore = progressStore; }

    public object BrowseTree(string repositoryName, string? relativePath)
    {
        var repository = FindRepository(repositoryName);
        var directory = ResolveRepositoryPath(repository, relativePath, true);
        return new
        {
            path = Path.GetRelativePath(repository.RootPath, directory),
            directories = Directory.EnumerateDirectories(directory).Where(path => !IgnoredDirectories.Contains(Path.GetFileName(path))).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).Take(300).Select(path => new { name = Path.GetFileName(path), path = Path.GetRelativePath(repository.RootPath, path) }),
            files = Directory.EnumerateFiles(directory).Where(path => IndexedExtensions.Contains(Path.GetExtension(path))).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).Take(500).Select(path => new { name = Path.GetFileName(path), path = Path.GetRelativePath(repository.RootPath, path), extension = Path.GetExtension(path), size = new FileInfo(path).Length })
        };
    }

    public object ReadFile(string repositoryName, string relativePath)
    {
        var repository = FindRepository(repositoryName);
        var path = ResolveRepositoryPath(repository, relativePath, false);
        var info = new FileInfo(path);
        if (info.Length > 1024 * 1024 || IsBinary(path)) throw new InvalidOperationException("Only text files up to 1 MB can be previewed.");
        var content = File.ReadAllText(path, Encoding.UTF8);
        return new { path = Path.GetRelativePath(repository.RootPath, path), extension = Path.GetExtension(path), content, line_count = content.Count(x => x == '\n') + 1 };
    }

    public object Grep(string repositoryName, string query)
    {
        var repository = FindRepository(repositoryName);
        var needle = (query ?? string.Empty).Trim();
        if (needle.Length < 2) throw new ArgumentException("Search query must contain at least two characters.");
        var matches = new List<object>();
        foreach (var path in EnumerateFiles(repository.RootPath).Take(600))
        {
            if (matches.Count >= 100) break;
            var info = new FileInfo(path);
            if (info.Length > 512 * 1024 || IsBinary(path)) continue;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            for (var index = 0; index < lines.Length && matches.Count < 100; index++)
            {
                if (!lines[index].Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
                var start = Math.Max(0, index - 2);
                var end = Math.Min(lines.Length, index + 3);
                matches.Add(new { path = Path.GetRelativePath(repository.RootPath, path), line = index + 1, preview = string.Join("\n", lines[start..end]) });
            }
        }
        return new { query = needle, matches, truncated = matches.Count >= 100 };
    }

    /// <summary>Reads root structure and manifests without requiring a persisted source index.</summary>
    public Task<ToolResult> DescribeAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var repositories = FindSelectedRepositories(context);
        if (repositories.Count == 0) return Task.FromResult(ToolResult.Failed("No code repository is selected."));
        var citations = new List<KnowledgeCitationDto>();
        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = Directory.EnumerateFileSystemEntries(repository.RootPath).Where(path => !IgnoredDirectories.Contains(Path.GetFileName(path))).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).Take(80).Select(Path.GetFileName).ToList();
            var paths = new[] { "README.md", "README", "package.json", "pnpm-workspace.yaml", "Cargo.toml", "pyproject.toml", "Directory.Build.props" }.Select(name => Path.Combine(repository.RootPath, name)).Where(File.Exists).Take(8);
            var text = new StringBuilder($"Repository: {repository.DisplayName} ({repository.Name})\nRoot entries: {string.Join(", ", entries)}");
            foreach (var path in paths)
            {
                var content = File.ReadAllText(path, Encoding.UTF8);
                text.Append($"\n\n--- {Path.GetFileName(path)} ---\n");
                text.Append(content[..Math.Min(content.Length, 6000)]);
            }
            citations.Add(new KnowledgeCitationDto { Text = text.ToString(), Metadata = { ["repository_name"] = repository.Name, ["file_path"] = ".", ["source"] = "code_repository_overview" } });
        }
        return Task.FromResult(new ToolResult { Content = string.Join("\n\n", citations.Select(x => x.Text)), Citations = citations, Metadata = { ["tool"] = "code_repository_overview", ["indexed"] = false } });
    }

    public Task<int> IndexAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = FindRepository(repositoryName);
        var files = EnumerateFiles(repository.RootPath).Take(3000).ToList();
        var progress = new CodeRepositoryIndexProgress { RepositoryName = repository.Name, Status = "running", Stage = "scanning", TotalFiles = files.Count };
        _progressStore.Set(progress);
        var rows = new List<AiCodeRepositoryFile>();
        try
        {
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.ScannedFiles++;
                progress.CurrentPath = Path.GetRelativePath(repository.RootPath, path);
                var info = new FileInfo(path);
                if (info.Length > 512 * 1024 || IsBinary(path)) { progress.SkippedFiles++; _progressStore.Set(progress); continue; }
                var content = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content)) { progress.SkippedFiles++; _progressStore.Set(progress); continue; }
                var symbols = SymbolPattern.Matches(content).Select(x => x.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
                rows.Add(new AiCodeRepositoryFile
                {
                    CodeRepositoryId = repository.Id,
                    RelativePath = Path.GetRelativePath(repository.RootPath, path),
                    Extension = Path.GetExtension(path),
                    Content = content,
                    SymbolsJson = JsonSerializer.Serialize(symbols),
                    ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
                    LineCount = content.Count(x => x == '\n') + 1
                });
                progress.IndexedFiles++;
                _progressStore.Set(progress);
            }
            progress.Stage = "saving"; _progressStore.Set(progress);
            _db.Deleteable<AiCodeRepositoryFile>().Where(x => x.CodeRepositoryId == repository.Id).ExecuteCommand();
            if (rows.Count > 0) _db.Insertable(rows).ExecuteCommand();
            _db.Updateable<AiCodeRepository>().SetColumns(x => new AiCodeRepository { LastIndexedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Status = "ready" }).Where(x => x.Id == repository.Id).ExecuteCommand();
            progress.Status = "ready"; progress.Stage = "completed"; progress.CurrentPath = null; _progressStore.Set(progress);
            return Task.FromResult(rows.Count);
        }
        catch (Exception ex)
        {
            progress.Status = "failed"; progress.Stage = "failed"; progress.Error = ex.Message; _progressStore.Set(progress);
            throw;
        }
    }

    public Task<ToolResult> SearchAsync(AgentContext context, string query, int topK, CancellationToken cancellationToken) => SearchCoreAsync(context, query, topK, false, cancellationToken);

    public Task<ToolResult> FindSymbolAsync(AgentContext context, string symbol, CancellationToken cancellationToken) => SearchCoreAsync(context, symbol, 12, true, cancellationToken);

    private Task<ToolResult> SearchCoreAsync(AgentContext context, string query, int topK, bool symbolsOnly, CancellationToken cancellationToken)
    {
        var repositories = FindSelectedRepositories(context);
        if (repositories.Count == 0) return Task.FromResult(ToolResult.Failed("No code repository is selected."));
        var ids = repositories.Select(x => x.Id).ToList();
        var terms = Regex.Matches(query, "[A-Za-z_][A-Za-z0-9_]{1,}|[\\p{IsCJKUnifiedIdeographs}]{2,}").Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        var files = _db.Queryable<AiCodeRepositoryFile>().Where(x => ids.Contains(x.CodeRepositoryId)).ToList()
            .Select(x => new { File = x, Score = Score(x, terms, symbolsOnly) }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(Math.Clamp(topK, 1, 12)).ToList();
        if (files.Count == 0)
        {
            files = SearchLive(repositories, terms, symbolsOnly, cancellationToken)
                .Select(x => new { File = x.File, Score = x.Score })
                .OrderByDescending(x => x.Score)
                .Take(Math.Clamp(topK, 1, 12))
                .ToList();
        }
        if (files.Count == 0) return Task.FromResult(ToolResult.Failed("No matching source was found after a bounded read-only repository scan. Refine the file, symbol, or error query."));
        var citations = files.Select(x => ToCitation(x.File, query, repositories.First(r => r.Id == x.File.CodeRepositoryId).Name)).ToList();
        var content = string.Join("\n\n", citations.Select((x, i) => $"[{i + 1}] {x.Metadata["file_path"]}\n{x.Text}"));
        return Task.FromResult(new ToolResult { Content = content, Citations = citations, Metadata = { ["tool"] = symbolsOnly ? "find_symbol" : "code_search" } });
    }

    private AiCodeRepository FindRepository(string name) => _db.Queryable<AiCodeRepository>().Where(x => x.Name == name && !x.IsDeleted).First() ?? throw new InvalidOperationException($"Code repository '{name}' does not exist.");

    private static string ResolveRepositoryPath(AiCodeRepository repository, string? relativePath, bool directory)
    {
        var root = Path.GetFullPath(repository.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path is outside the selected code repository.");
        if (directory ? !Directory.Exists(path) : !File.Exists(path)) throw new FileNotFoundException("Code repository path does not exist.");
        return path;
    }

    private static IEnumerable<(AiCodeRepositoryFile File, int Score)> SearchLive(List<AiCodeRepository> repositories, List<string> terms, bool symbolsOnly, CancellationToken cancellationToken)
    {
        foreach (var repository in repositories)
            foreach (var path in EnumerateFiles(repository.RootPath).Take(400))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                if (info.Length > 256 * 1024 || IsBinary(path)) continue;
                var content = File.ReadAllText(path, Encoding.UTF8);
                var symbols = SymbolPattern.Matches(content).Select(x => x.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
                var file = new AiCodeRepositoryFile { CodeRepositoryId = repository.Id, RelativePath = Path.GetRelativePath(repository.RootPath, path), Extension = Path.GetExtension(path), Content = content, SymbolsJson = JsonSerializer.Serialize(symbols) };
                var score = Score(file, terms, symbolsOnly);
                if (score > 0) yield return (file, score);
            }
    }

    private List<AiCodeRepository> FindSelectedRepositories(AgentContext context) => context.CodeRepositoryNames.Count == 0 ? [] : _db.Queryable<AiCodeRepository>().Where(x => context.CodeRepositoryNames.Contains(x.Name) && !x.IsDeleted).ToList();

    private static IEnumerable<string> EnumerateFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => IndexedExtensions.Contains(Path.GetExtension(path)) && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IgnoredDirectories.Contains));

    private static bool IsBinary(string path)
    { using var stream = File.OpenRead(path); var length = (int)Math.Min(1024, stream.Length); var bytes = new byte[length]; stream.ReadExactly(bytes); return bytes.Contains((byte)0); }

    private static int Score(AiCodeRepositoryFile file, List<string> terms, bool symbolsOnly) => terms.Sum(term => (file.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase) ? 8 : 0) + (file.SymbolsJson?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ? 12 : 0) + (!symbolsOnly && file.Content.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : 0));

    private static KnowledgeCitationDto ToCitation(AiCodeRepositoryFile file, string query, string repositoryName)
    {
        var position = file.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase); if (position < 0) position = 0;
        var start = Math.Max(0, position - 500); var text = file.Content.Substring(start, Math.Min(1600, file.Content.Length - start));
        return new KnowledgeCitationDto { Text = text, Metadata = { ["repository_name"] = repositoryName, ["file_path"] = file.RelativePath, ["source"] = "code_index" } };
    }
}