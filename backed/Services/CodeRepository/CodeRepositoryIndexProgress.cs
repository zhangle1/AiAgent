using System.Collections.Concurrent;

namespace AiAgent.Backend.Services.CodeRepository;

public sealed class CodeRepositoryIndexProgress
{
    public string RepositoryName { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";
    public string Stage { get; set; } = "idle";
    public string? CurrentPath { get; set; }
    public int TotalFiles { get; set; }
    public int ScannedFiles { get; set; }
    public int IndexedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Percent => TotalFiles <= 0 ? 0 : Math.Min(100, ScannedFiles * 100 / TotalFiles);
}

public interface ICodeRepositoryIndexProgressStore
{
    CodeRepositoryIndexProgress Get(string repositoryName);

    void Set(CodeRepositoryIndexProgress progress);
}

public sealed class CodeRepositoryIndexProgressStore : ICodeRepositoryIndexProgressStore
{
    private readonly ConcurrentDictionary<string, CodeRepositoryIndexProgress> _items = new(StringComparer.OrdinalIgnoreCase);

    public CodeRepositoryIndexProgress Get(string repositoryName) => _items.TryGetValue(repositoryName, out var value) ? value : new CodeRepositoryIndexProgress { RepositoryName = repositoryName };

    public void Set(CodeRepositoryIndexProgress progress)
    { progress.UpdatedAt = DateTime.UtcNow; _items[progress.RepositoryName] = progress; }
}