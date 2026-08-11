using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;

namespace AiAgent.Backend.Services.Git;

public interface ICodeRepositoryGitService
{
    Task<GitWorkspaceStatus> StatusAsync(string repositoryName, CancellationToken cancellationToken);
    Task<GitWorkspaceBranches> BranchesAsync(string repositoryName, CancellationToken cancellationToken);
    Task<GitWorkspaceDiff> DiffAsync(string repositoryName, string? comparison, CancellationToken cancellationToken);
    Task<GitOperationResult> CheckoutAsync(string repositoryName, string branch, CancellationToken cancellationToken);
    Task<GitOperationResult> DiscardChangesAndPullAsync(string repositoryName, CancellationToken cancellationToken);
    Task<GitOperationResult> PullAsync(string repositoryName, CancellationToken cancellationToken);
    Task<GitOperationResult> CommitAndPushAsync(string repositoryName, string? message, CancellationToken cancellationToken);
}

/// <summary>Git provider adapter for registered code repositories. It resolves only database-registered repository roots.</summary>
public sealed class CodeRepositoryGitService : ICodeRepositoryGitService
{
    private readonly ISqlSugarClient _db;
    private readonly IGitWorkspaceService _git;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IDataProtector _protector;

    public CodeRepositoryGitService(
        ISqlSugarClient db,
        IGitWorkspaceService git,
        IHttpContextAccessor httpContextAccessor,
        IAuthService authService,
        IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _git = git;
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.GitAccounts.AccessToken.v1");
    }

    public async Task<GitWorkspaceStatus> StatusAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    public async Task<GitWorkspaceBranches> BranchesAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.BranchesAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    public async Task<GitWorkspaceDiff> DiffAsync(string repositoryName, string? comparison, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.DiffAsync($"repository:{repository.Id}", repository.RootPath, comparison, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    public async Task<GitOperationResult> CheckoutAsync(string repositoryName, string branch, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.CheckoutAsync($"repository:{repository.Id}", repository.RootPath, branch, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    public async Task<GitOperationResult> DiscardChangesAndPullAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.DiscardChangesAndPullAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    public async Task<GitOperationResult> PullAsync(string repositoryName, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        return await _git.PullAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    public async Task<GitOperationResult> CommitAndPushAsync(string repositoryName, string? message, CancellationToken cancellationToken)
    {
        var repository = Find(repositoryName);
        var commitMessage = string.IsNullOrWhiteSpace(message) ? $"chore: update {repository.DisplayName}" : message;
        return await _git.CommitAndPushAsync($"repository:{repository.Id}", repository.RootPath, commitMessage, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
    }

    private AiCodeRepository Find(string name)
    {
        var repository = _db.Queryable<AiCodeRepository>().Where(x => x.Name == name && !x.IsDeleted).First();
        return repository ?? throw new InvalidOperationException("The selected code repository does not exist.");
    }

    private async Task<GitWorkspaceCredential?> ResolveCredentialAsync(AiCodeRepository repository, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken)
            ?? throw new UnauthorizedAccessException();

        AiGitAccount? account = null;
        if (repository.GitAccountId.HasValue)
        {
            account = _db.Queryable<AiGitAccount>()
                .First(item => item.Id == repository.GitAccountId.Value && item.UserId == user.Id && !item.IsDeleted);
        }

        account ??= _db.Queryable<AiGitAccount>()
            .Where(item => item.UserId == user.Id && item.IsActive && !item.IsDeleted)
            .OrderByDescending(item => item.UpdatedAt)
            .First();
        if (account is null || string.IsNullOrWhiteSpace(account.AccessTokenProtected)) return null;

        try
        {
            return new GitWorkspaceCredential(account.Username, _protector.Unprotect(account.AccessTokenProtected));
        }
        catch
        {
            throw new InvalidOperationException("The saved Git access token cannot be read. Re-save the current Git account with a new token.");
        }
    }
}
