using AiAgent.Backend.Entities.CodeRepository;
using SqlSugar;

namespace AiAgent.Backend.Services.Git;

public interface ICodeRepositoryGitService
{
    Task<GitWorkspaceStatus> StatusAsync(string repositoryName, CancellationToken cancellationToken);
    Task<GitOperationResult> PullAsync(string repositoryName, CancellationToken cancellationToken);
    Task<GitOperationResult> CommitAndPushAsync(string repositoryName, string? message, CancellationToken cancellationToken);
}

/// <summary>Git provider adapter for registered code repositories. It resolves only database-registered repository roots.</summary>
public sealed class CodeRepositoryGitService : ICodeRepositoryGitService
{
    private readonly ISqlSugarClient _db;
    private readonly IGitWorkspaceService _git;

    public CodeRepositoryGitService(ISqlSugarClient db, IGitWorkspaceService git)
    {
        _db = db;
        _git = git;
    }

    public async Task<GitWorkspaceStatus> StatusAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken);
    }

    public async Task<GitOperationResult> PullAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.PullAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken);
    }

    public async Task<GitOperationResult> CommitAndPushAsync(string repositoryName, string? message, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        var commitMessage = string.IsNullOrWhiteSpace(message) ? $"chore: update {repository.DisplayName}" : message;
        return await _git.CommitAndPushAsync($"repository:{repository.Id}", repository.RootPath, commitMessage, cancellationToken);
    }

    private AiCodeRepository Find(string name)
    {
        var repository = _db.Queryable<AiCodeRepository>().Where(x => x.Name == name && !x.IsDeleted).First();
        return repository ?? throw new InvalidOperationException("The selected code repository does not exist.");
    }
}
