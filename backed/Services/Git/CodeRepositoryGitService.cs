using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Push;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

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
    Task<ProjectGitStatus> ProjectStatusAsync(long projectId, CancellationToken cancellationToken);
    Task<ProjectGitBatchOperationResult> ProjectDiscardChangesAndPullAsync(long projectId, IReadOnlyCollection<string>? repositoryNames, CancellationToken cancellationToken);
    Task<ProjectGitBatchOperationResult> ProjectCommitAndPushAsync(long projectId, IReadOnlyCollection<string>? repositoryNames, string? message, CancellationToken cancellationToken);
    Task<ProjectGitBatchOperationResult> ProjectAutomaticDiscardChangesAndPullAsync(long projectId, CancellationToken cancellationToken);
}

public sealed class ProjectGitStatus
{
    [JsonPropertyName("project_id")]
    public long ProjectId { get; set; }
    public string State { get; set; } = "neutral";
    public string Message { get; set; } = string.Empty;
    public List<ProjectGitRepositoryStatus> Repositories { get; set; } = [];
}

public sealed class ProjectGitRepositoryStatus
{
    [JsonPropertyName("repository_id")]
    public long RepositoryId { get; set; }
    [JsonPropertyName("repository_name")]
    public string RepositoryName { get; set; } = string.Empty;
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
    public string State { get; set; } = "failed";
    public string Message { get; set; } = string.Empty;
    public GitWorkspaceStatus? Status { get; set; }
}

public sealed class ProjectGitBatchOperationResult
{
    [JsonPropertyName("project_id")]
    public long ProjectId { get; set; }
    public string Action { get; set; } = string.Empty;
    public List<ProjectGitBatchRepositoryResult> Repositories { get; set; } = [];
}

public sealed class ProjectGitBatchRepositoryResult
{
    [JsonPropertyName("repository_id")]
    public long RepositoryId { get; set; }
    [JsonPropertyName("repository_name")]
    public string RepositoryName { get; set; } = string.Empty;
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
    public string Outcome { get; set; } = "failed";
    public string Message { get; set; } = string.Empty;
    public GitOperationResult? Result { get; set; }
}

/// <summary>Git provider adapter for registered code repositories. It resolves only database-registered repository roots.</summary>
public sealed class CodeRepositoryGitService : ICodeRepositoryGitService
{
    private readonly ISqlSugarClient _db;
    private readonly IGitWorkspaceService _git;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IDataProtector _protector;
    private readonly IProjectPushService _pushes;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _projectOperationGates = new();

    public CodeRepositoryGitService(
        ISqlSugarClient db,
        IGitWorkspaceService git,
        IHttpContextAccessor httpContextAccessor,
        IAuthService authService,
        IDataProtectionProvider dataProtectionProvider,
        IProjectPushService pushes)
    {
        _db = db;
        _git = git;
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.GitAccounts.AccessToken.v1");
        _pushes = pushes;
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
        var credential = await ResolveCredentialAsync(repository, cancellationToken);
        var status = await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, credential);
        if (!HasPushWork(status)) return NoPushRequired(status);

        var result = await _git.CommitAndPushAsync($"repository:{repository.Id}", repository.RootPath, commitMessage, cancellationToken, credential);
        if (result.Ok && repository.ProjectId.HasValue)
            await QueuePushNotificationAsync(repository, result, commitMessage, cancellationToken);
        return result;
    }

    public async Task<ProjectGitStatus> ProjectStatusAsync(long projectId, CancellationToken cancellationToken)
    {
        var repositories = FindProjectRepositories(projectId);
        var rows = new List<ProjectGitRepositoryStatus>();
        foreach (var repository in repositories)
        {
            try
            {
                var status = await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
                rows.Add(ToProjectStatus(repository, status));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(new ProjectGitRepositoryStatus
                {
                    RepositoryId = repository.Id,
                    RepositoryName = repository.Name,
                    DisplayName = repository.DisplayName,
                    State = "failed",
                    Message = ToSafeMessage(ex)
                });
            }
        }

        var state = rows.Count == 0 || (!rows.Any(row => row.Status?.IsRepository == true) && !rows.Any(row => row.State == "failed"))
            ? "neutral"
            : rows.Any(row => row.State is "ahead" or "behind" or "changes" or "no-upstream" or "failed") ? "attention" : "synced";
        var message = state switch
        {
            "synced" => "所有 Git 代码库已与远端同步。",
            "attention" => $"{rows.Count(row => row.State is "ahead" or "behind" or "changes" or "no-upstream" or "failed")} 个代码库需要处理。",
            _ when rows.Count == 0 => "当前项目没有已登记的代码库。",
            _ => "当前项目没有可检查的 Git 代码库。"
        };
        return new ProjectGitStatus { ProjectId = projectId, State = state, Message = message, Repositories = rows };
    }

    public Task<ProjectGitBatchOperationResult> ProjectDiscardChangesAndPullAsync(long projectId, IReadOnlyCollection<string>? repositoryNames, CancellationToken cancellationToken)
        => RunProjectExclusiveAsync(projectId, async () =>
        {
            var results = new ProjectGitBatchOperationResult { ProjectId = projectId, Action = "discard-and-pull" };
            foreach (var repository in FilterProjectRepositories(projectId, repositoryNames))
            {
                try
                {
                    var credential = await ResolveCredentialAsync(repository, cancellationToken);
                    var status = await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, credential);
                    if (!status.IsRepository) { results.Repositories.Add(Skipped(repository, "该目录不是 Git 代码库，已跳过。")); continue; }
                    if (!string.IsNullOrWhiteSpace(status.RemoteRefreshError)) { results.Repositories.Add(Skipped(repository, "远端刷新失败，已跳过重置更新。")); continue; }
                    if (string.IsNullOrWhiteSpace(status.RemoteBranch)) { results.Repositories.Add(Skipped(repository, "未设置远端跟踪分支，已跳过重置更新。")); continue; }
                    if (!HasDiscardAndPullWork(status)) { results.Repositories.Add(Skipped(repository, "没有待还原的本地修改，也没有待拉取的远端提交，已跳过。")); continue; }
                    var result = await _git.DiscardChangesAndPullAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, credential);
                    results.Repositories.Add(new ProjectGitBatchRepositoryResult { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, Outcome = result.Ok ? "succeeded" : "failed", Message = result.Ok ? "已重置本地已跟踪修改并完成快进更新。" : ToSafeOutput(result.Output, "重置更新失败。"), Result = result });
                }
                catch (OperationCanceledException)
                {
                    results.Repositories.Add(new ProjectGitBatchRepositoryResult { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, Outcome = "failed", Message = "操作等待超时或请求连接已中断。代码可能已完成更新，请刷新 Git 状态确认后再重试。" });
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results.Repositories.Add(Failed(repository, ex));
                }
            }
            return results;
        }, cancellationToken);

    public Task<ProjectGitBatchOperationResult> ProjectCommitAndPushAsync(long projectId, IReadOnlyCollection<string>? repositoryNames, string? message, CancellationToken cancellationToken)
        => RunProjectExclusiveAsync(projectId, async () =>
        {
            var results = new ProjectGitBatchOperationResult { ProjectId = projectId, Action = "commit-and-push" };
            var operationId = Guid.NewGuid().ToString("N");
            foreach (var repository in FilterProjectRepositories(projectId, repositoryNames))
            {
                try
                {
                    var status = await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
                    if (!status.IsRepository) { results.Repositories.Add(Skipped(repository, "该目录不是 Git 代码库，已跳过。")); continue; }
                    if (!string.IsNullOrWhiteSpace(status.RemoteRefreshError)) { results.Repositories.Add(Skipped(repository, "远端刷新失败，已跳过提交推送。")); continue; }
                    if (string.IsNullOrWhiteSpace(status.RemoteBranch)) { results.Repositories.Add(Skipped(repository, "未设置远端跟踪分支，已跳过提交推送。")); continue; }
                    if (status.Behind > 0) { results.Repositories.Add(Skipped(repository, $"远端领先 {status.Behind} 个提交，请先一键重置更新。")); continue; }
                    if (!HasPushWork(status)) { results.Repositories.Add(Skipped(repository, "没有待提交文件或待推送提交，已跳过；不会发送钉钉通知。")); continue; }
                    var result = await _git.CommitAndPushAsync($"repository:{repository.Id}", repository.RootPath, string.IsNullOrWhiteSpace(message) ? $"chore: update {repository.DisplayName}" : message.Trim(), cancellationToken, await ResolveCredentialAsync(repository, cancellationToken));
                    results.Repositories.Add(new ProjectGitBatchRepositoryResult { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, Outcome = result.Ok ? "succeeded" : "failed", Message = result.Ok ? "已提交并推送。" : ToSafeOutput(result.Output, "提交推送失败。"), Result = result });
                    if (result.Ok) await QueuePushNotificationAsync(repository, result, message, cancellationToken, operationId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results.Repositories.Add(Failed(repository, ex));
                }
            }
            return results;
        }, cancellationToken);

    public Task<ProjectGitBatchOperationResult> ProjectAutomaticDiscardChangesAndPullAsync(long projectId, CancellationToken cancellationToken)
        => RunProjectExclusiveAsync(projectId, async () =>
        {
            var results = new ProjectGitBatchOperationResult { ProjectId = projectId, Action = "automatic-discard-and-pull" };
            var cutoff = DateTime.UtcNow.AddHours(-3);
            foreach (var repository in FindProjectRepositories(projectId))
            {
                try
                {
                    var credential = await ResolveBackgroundCredentialAsync(repository, cancellationToken);
                    var status = await _git.StatusAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, credential);
                    if (!status.IsRepository) { results.Repositories.Add(Skipped(repository, "不是 Git 代码库，未执行自动更新。")); continue; }
                    if (!string.IsNullOrWhiteSpace(status.RemoteRefreshError)) { results.Repositories.Add(Skipped(repository, "远端刷新失败，未执行自动更新。")); continue; }
                    if (string.IsNullOrWhiteSpace(status.RemoteBranch)) { results.Repositories.Add(Skipped(repository, "未设置远端跟踪分支，未执行自动更新。")); continue; }
                    var changes = await _git.LocalChangesAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, credential);
                    var unsafeChanges = changes.Where(change => !change.LastWriteTimeUtc.HasValue || change.LastWriteTimeUtc.Value > cutoff).ToList();
                    if (unsafeChanges.Count > 0)
                    {
                        var reason = unsafeChanges.Any(change => !change.LastWriteTimeUtc.HasValue)
                            ? "存在删除、路径异常或无法确定修改时间的本地变更，未执行自动重置。"
                            : $"存在 {unsafeChanges.Count} 个近 3 小时修改的文件，未执行自动重置。";
                        results.Repositories.Add(Skipped(repository, reason));
                        continue;
                    }
                    var result = await _git.DiscardChangesAndPullAsync($"repository:{repository.Id}", repository.RootPath, cancellationToken, credential);
                    results.Repositories.Add(new ProjectGitBatchRepositoryResult { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, Outcome = result.Ok ? "succeeded" : "failed", Message = result.Ok ? "自动重置更新完成。" : ToSafeOutput(result.Output, "自动重置更新失败。"), Result = result });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results.Repositories.Add(Failed(repository, ex));
                }
            }
            return results;
        }, cancellationToken);

    private async Task<T> RunProjectExclusiveAsync<T>(long projectId, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var gate = _projectOperationGates.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { gate.Release(); }
    }

    private List<AiCodeRepository> FindProjectRepositories(long projectId)
    {
        if (!_db.Queryable<AiCodeProject>().Any(item => item.Id == projectId && !item.IsDeleted)) throw new InvalidOperationException("所选项目不存在或已被删除。");
        return _db.Queryable<AiCodeRepository>().Where(item => item.ProjectId == projectId && !item.IsDeleted).OrderBy(item => item.DisplayName).ToList();
    }

    private List<AiCodeRepository> FilterProjectRepositories(long projectId, IReadOnlyCollection<string>? repositoryNames)
    {
        var repositories = FindProjectRepositories(projectId);
        if (repositoryNames is null || repositoryNames.Count == 0) return repositories;
        var names = repositoryNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repositories.Where(repository => names.Contains(repository.Name)).ToList();
    }

    private static ProjectGitRepositoryStatus ToProjectStatus(AiCodeRepository repository, GitWorkspaceStatus status)
    {
        var state = !status.IsRepository ? "not-repository"
            : !string.IsNullOrWhiteSpace(status.RemoteRefreshError) ? "failed"
            : string.IsNullOrWhiteSpace(status.RemoteBranch) ? "no-upstream"
            : status.Behind > 0 ? "behind"
            : status.Changes.Count > 0 ? "changes"
            : status.Ahead > 0 ? "ahead" : "synced";
        var message = state switch
        {
            "synced" => "已同步。",
            "changes" => $"有 {status.Changes.Count} 个本地修改待提交并推送。",
            "ahead" => $"本地领先 {status.Ahead} 个提交，待推送。",
            "behind" => $"远端领先 {status.Behind} 个提交。",
            "no-upstream" => "未设置远端或上游分支。",
            "not-repository" => "该目录不是 Git 代码库。",
            _ => ToSafeOutput(status.RemoteRefreshError, "远端刷新失败，需要人工处理。")
        };
        return new ProjectGitRepositoryStatus { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, State = state, Message = message, Status = status };
    }

    private static bool HasPushWork(GitWorkspaceStatus status) => status.Changes.Count > 0 || status.Ahead > 0;
    private static bool HasDiscardAndPullWork(GitWorkspaceStatus status) => status.Changes.Count > 0 || status.Behind > 0;
    private static GitOperationResult NoPushRequired(GitWorkspaceStatus status) => new()
    {
        Ok = true,
        Action = "push",
        Output = "没有待提交文件或待推送提交，已跳过；不会发送钉钉通知。",
        Status = status
    };

    private static ProjectGitBatchRepositoryResult Skipped(AiCodeRepository repository, string message) => new() { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, Outcome = "skipped", Message = message };
    private static ProjectGitBatchRepositoryResult Failed(AiCodeRepository repository, Exception exception) => new() { RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName, Outcome = "failed", Message = ToSafeMessage(exception) };
    private static string ToSafeMessage(Exception exception) => ToSafeOutput(exception.Message, "操作失败，需要人工处理。");
    private static string ToSafeOutput(string? output, string fallback) => string.IsNullOrWhiteSpace(output) ? fallback : output.Length > 800 ? output[..800] : output;

    private AiCodeRepository Find(string name)
    {
        var repository = _db.Queryable<AiCodeRepository>().Where(x => x.Name == name && !x.IsDeleted).First();
        return repository ?? throw new InvalidOperationException("The selected code repository does not exist.");
    }

    private async Task QueuePushNotificationAsync(AiCodeRepository repository, GitOperationResult result, string? message, CancellationToken cancellationToken, string? operationId = null)
    {
        if (!repository.ProjectId.HasValue) return;
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken);
        if (user == null) return;
        await _pushes.QueueGitPushSucceededAsync(new ProjectGitPushSucceededEvent(
            repository.ProjectId.Value,
            repository.Id,
            repository.DisplayName,
            result.Status.Branch ?? "未知分支",
            result.CommitSha,
            string.IsNullOrWhiteSpace(message) ? $"chore: update {repository.DisplayName}" : message.Trim(),
            user.Username,
            operationId ?? Guid.NewGuid().ToString("N")), cancellationToken);
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

    private Task<GitWorkspaceCredential?> ResolveBackgroundCredentialAsync(AiCodeRepository repository, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!repository.GitAccountId.HasValue) return Task.FromResult<GitWorkspaceCredential?>(null);
        var account = _db.Queryable<AiGitAccount>().First(item => item.Id == repository.GitAccountId.Value && item.IsActive && !item.IsDeleted);
        if (account is null || string.IsNullOrWhiteSpace(account.AccessTokenProtected)) return Task.FromResult<GitWorkspaceCredential?>(null);
        try
        {
            return Task.FromResult<GitWorkspaceCredential?>(new GitWorkspaceCredential(account.Username, _protector.Unprotect(account.AccessTokenProtected)));
        }
        catch
        {
            throw new InvalidOperationException("该代码库绑定的 Git 凭据无法读取，未执行自动更新。");
        }
    }
}
