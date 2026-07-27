using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Git;

/// <summary>
/// Shared Git command boundary for trusted server workspaces. Callers own path authorization.
/// A workspace has one operation lane, mirroring VS Code SCM's operation manager behavior.
/// </summary>
public interface IGitWorkspaceService
{
    Task<GitWorkspaceStatus> StatusAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken);
    Task<GitWorkspaceBranches> BranchesAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken);
    Task<GitWorkspaceDiff> DiffAsync(string workspaceKey, string rootPath, string? comparison, CancellationToken cancellationToken);
    Task<GitOperationResult> CheckoutAsync(string workspaceKey, string rootPath, string branch, CancellationToken cancellationToken);
    Task<GitOperationResult> DiscardChangesAndPullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken);
    Task<GitOperationResult> PullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken);
    Task<GitOperationResult> CommitAndPushAsync(string workspaceKey, string rootPath, string message, CancellationToken cancellationToken);
}

public sealed class GitWorkspaceStatus
{
    [JsonPropertyName("is_repository")]
    public bool IsRepository { get; set; }
    public string? Branch { get; set; }
    [JsonPropertyName("remote_branch")]
    public string? RemoteBranch { get; set; }
    [JsonPropertyName("remote_name")]
    public string? RemoteName { get; set; }
    public List<string> Changes { get; set; } = [];
    public int Ahead { get; set; }
    public int Behind { get; set; }
    [JsonPropertyName("ahead_files")]
    public int AheadFiles { get; set; }
    [JsonPropertyName("behind_files")]
    public int BehindFiles { get; set; }
    [JsonPropertyName("remote_refresh_error")]
    public string? RemoteRefreshError { get; set; }
    public string Output { get; set; } = string.Empty;
}

public sealed class GitOperationResult
{
    public bool Ok { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public GitWorkspaceStatus Status { get; set; } = new();
}

public sealed class GitWorkspaceBranches
{
    [JsonPropertyName("current_branch")]
    public string? CurrentBranch { get; set; }
    [JsonPropertyName("local_branches")]
    public List<string> LocalBranches { get; set; } = [];
    [JsonPropertyName("remote_branches")]
    public List<string> RemoteBranches { get; set; } = [];
}

public sealed class GitWorkspaceDiff
{
    public string Comparison { get; set; } = "working";
    [JsonPropertyName("remote_branch")]
    public string? RemoteBranch { get; set; }
    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }
    [JsonPropertyName("is_truncated")]
    public bool IsTruncated { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class GitWorkspaceService : IGitWorkspaceService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RecentRemoteRefreshes = new(StringComparer.OrdinalIgnoreCase);

    public Task<GitWorkspaceStatus> StatusAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, () => GetStatusAsync(rootPath, cancellationToken), cancellationToken);

    public Task<GitWorkspaceBranches> BranchesAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, () => GetBranchesAsync(rootPath, cancellationToken), cancellationToken);

    public Task<GitWorkspaceDiff> DiffAsync(string workspaceKey, string rootPath, string? comparison, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, () => GetDiffAsync(rootPath, comparison, cancellationToken), cancellationToken);

    public Task<GitOperationResult> CheckoutAsync(string workspaceKey, string rootPath, string branch, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, async () =>
        {
            await RequireRepositoryAsync(rootPath, cancellationToken);
            var workingChanges = await RunGitAsync(rootPath, ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken);
            if (!string.IsNullOrWhiteSpace(workingChanges.Output))
            {
                throw new InvalidOperationException("当前工作区有未提交修改，请先提交、暂存或还原后再切换分支。");
            }

            var branches = await GetBranchesAsync(rootPath, cancellationToken);
            GitProcessResult checkout;
            if (branches.LocalBranches.Contains(branch, StringComparer.Ordinal))
            {
                checkout = await RunGitAsync(rootPath, ["switch", branch], cancellationToken);
            }
            else if (branches.RemoteBranches.Contains(branch, StringComparer.Ordinal))
            {
                var slash = branch.IndexOf('/');
                var localName = slash >= 0 ? branch[(slash + 1)..] : branch;
                checkout = branches.LocalBranches.Contains(localName, StringComparer.Ordinal)
                    ? await RunGitAsync(rootPath, ["switch", localName], cancellationToken)
                    : await RunGitAsync(rootPath, ["switch", "--track", "-c", localName, branch], cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("所选分支不存在或已被远端删除，请刷新分支列表后重试。");
            }

            return new GitOperationResult
            {
                Ok = checkout.ExitCode == 0,
                Action = "checkout",
                Output = checkout.Output,
                Status = await GetStatusAsync(rootPath, cancellationToken)
            };
        }, cancellationToken);

    public Task<GitOperationResult> DiscardChangesAndPullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, async () =>
        {
            await RequireRepositoryAsync(rootPath, cancellationToken);
            var output = new List<string>();
            var reset = await RunGitAsync(rootPath, ["reset", "--hard", "HEAD"], cancellationToken);
            output.Add(reset.Output);
            if (reset.ExitCode != 0) return await FailureAsync("discard-and-pull", output, rootPath, cancellationToken);

            var pull = await RunGitAsync(rootPath, ["pull", "--ff-only"], cancellationToken);
            output.Add(pull.Output);
            return new GitOperationResult
            {
                Ok = pull.ExitCode == 0,
                Action = "discard-and-pull",
                Output = JoinOutput(output),
                Status = await GetStatusAsync(rootPath, cancellationToken)
            };
        }, cancellationToken);

    public Task<GitOperationResult> PullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, async () =>
        {
            await RequireRepositoryAsync(rootPath, cancellationToken);
            var pull = await RunGitAsync(rootPath, ["pull", "--ff-only"], cancellationToken);
            return new GitOperationResult
            {
                Ok = pull.ExitCode == 0,
                Action = "pull",
                Output = pull.Output,
                Status = await GetStatusAsync(rootPath, cancellationToken)
            };
        }, cancellationToken);

    public Task<GitOperationResult> CommitAndPushAsync(string workspaceKey, string rootPath, string message, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, async () =>
        {
            await RequireRepositoryAsync(rootPath, cancellationToken);
            var output = new List<string>();
            var add = await RunGitAsync(rootPath, ["add", "--all"], cancellationToken);
            output.Add(add.Output);
            if (add.ExitCode != 0) return await FailureAsync("push", output, rootPath, cancellationToken);

            var cached = await RunGitAsync(rootPath, ["diff", "--cached", "--quiet"], cancellationToken);
            if (cached.ExitCode == 1)
            {
                var commit = await RunGitAsync(rootPath, ["commit", "-m", NormalizeCommitMessage(message)], cancellationToken);
                output.Add(commit.Output);
                if (commit.ExitCode != 0) return await FailureAsync("push", output, rootPath, cancellationToken);
            }
            else if (cached.ExitCode != 0)
            {
                output.Add("Unable to inspect staged changes.");
                output.Add(cached.Output);
                return await FailureAsync("push", output, rootPath, cancellationToken);
            }

            var push = await RunGitAsync(rootPath, ["push"], cancellationToken);
            output.Add(push.Output);
            return new GitOperationResult
            {
                Ok = push.ExitCode == 0,
                Action = "push",
                Output = JoinOutput(output),
                Status = await GetStatusAsync(rootPath, cancellationToken)
            };
        }, cancellationToken);

    private async Task<T> RunExclusiveAsync<T>(string workspaceKey, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var gate = _operationGates.GetOrAdd(workspaceKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { gate.Release(); }
    }

    private static async Task<GitOperationResult> FailureAsync(string action, List<string> output, string rootPath, CancellationToken cancellationToken) => new()
    {
        Ok = false,
        Action = action,
        Output = JoinOutput(output),
        Status = await GetStatusAsync(rootPath, cancellationToken)
    };

    private static async Task RequireRepositoryAsync(string rootPath, CancellationToken cancellationToken)
    {
        var check = await RunGitAsync(rootPath, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.ExitCode != 0 || !check.Output.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected workspace is not a Git working tree. Initialize or clone it before using Git management.");
        }
    }

    private static async Task<GitWorkspaceStatus> GetStatusAsync(string rootPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException("The selected Git workspace no longer exists.");
        var check = await RunGitAsync(rootPath, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.ExitCode != 0 || !check.Output.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            return new GitWorkspaceStatus { Output = check.Output };
        }
        var remoteRefreshError = await RefreshRemoteRefsAsync(rootPath, cancellationToken);

        var branch = await RunGitAsync(rootPath, ["branch", "--show-current"], cancellationToken);
        var upstream = await RunGitAsync(rootPath, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cancellationToken);
        var remoteBranch = upstream.ExitCode == 0 ? upstream.Output.Trim() : null;
        var porcelain = await RunGitAsync(rootPath, ["status", "--branch", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken);
        var entries = porcelain.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (behind, ahead) = await GetTrackingCountsAsync(rootPath, remoteBranch, cancellationToken);
        return new GitWorkspaceStatus
        {
            IsRepository = true,
            Branch = branch.Output.Trim(),
            RemoteBranch = remoteBranch,
            RemoteName = GetRemoteName(remoteBranch),
            Changes = entries.Where(item => !item.StartsWith("## ", StringComparison.Ordinal)).Take(200).ToList(),
            Ahead = ahead,
            Behind = behind,
            AheadFiles = await GetChangedFileCountAsync(rootPath, remoteBranch, "HEAD", cancellationToken),
            BehindFiles = await GetChangedFileCountAsync(rootPath, "HEAD", remoteBranch, cancellationToken),
            RemoteRefreshError = remoteRefreshError,
            Output = porcelain.Output
        };
    }

    private static async Task<GitWorkspaceBranches> GetBranchesAsync(string rootPath, CancellationToken cancellationToken)
    {
        await RequireRepositoryAsync(rootPath, cancellationToken);
        await RefreshRemoteRefsAsync(rootPath, cancellationToken);
        var current = await RunGitAsync(rootPath, ["branch", "--show-current"], cancellationToken);
        var local = await RunGitAsync(rootPath, ["for-each-ref", "--format=%(refname:short)", "refs/heads"], cancellationToken);
        var remote = await RunGitAsync(rootPath, ["for-each-ref", "--format=%(refname:short)", "refs/remotes"], cancellationToken);
        return new GitWorkspaceBranches
        {
            CurrentBranch = current.Output.Trim(),
            LocalBranches = ToLines(local.Output),
            RemoteBranches = ToLines(remote.Output).Where(branch => !branch.EndsWith("/HEAD", StringComparison.Ordinal)).ToList()
        };
    }

    private static async Task<GitWorkspaceDiff> GetDiffAsync(string rootPath, string? comparison, CancellationToken cancellationToken)
    {
        await RequireRepositoryAsync(rootPath, cancellationToken);
        var mode = string.IsNullOrWhiteSpace(comparison) ? "working" : comparison.Trim().ToLowerInvariant();
        if (mode is not ("working" or "push" or "pull")) throw new InvalidOperationException("仅支持工作区、推送或拉取差异查看。");

        var remoteRefreshError = await RefreshRemoteRefsAsync(rootPath, cancellationToken);
        var upstream = await RunGitAsync(rootPath, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cancellationToken);
        var remoteBranch = upstream.ExitCode == 0 ? upstream.Output.Trim() : null;
        if (mode != "working" && string.IsNullOrWhiteSpace(remoteBranch))
        {
            return new GitWorkspaceDiff { Comparison = mode, Message = "当前分支未设置远程跟踪分支，无法比较服务器差异。" };
        }

        var range = mode switch
        {
            "working" => "HEAD",
            "push" => $"{remoteBranch}...HEAD",
            _ => $"HEAD...{remoteBranch}"
        };
        var diff = await RunGitAsync(rootPath, ["diff", "--no-ext-diff", "--no-color", "--unified=3", range], cancellationToken);
        var files = await RunGitAsync(rootPath, ["diff", "--name-only", range], cancellationToken);
        if (diff.ExitCode != 0 || files.ExitCode != 0)
        {
            var output = JoinOutput([diff.Output, files.Output]);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(output) ? "无法读取 Git Diff。" : output);
        }
        var content = diff.Output;
        const int maxDiffCharacters = 240_000;
        var isTruncated = content.Length > maxDiffCharacters;
        if (isTruncated) content = $"{content[..maxDiffCharacters]}\n\n… Diff 内容已截断，请按文件进一步查看。";
        return new GitWorkspaceDiff
        {
            Comparison = mode,
            RemoteBranch = remoteBranch,
            FileCount = ToLines(files.Output).Count,
            IsTruncated = isTruncated,
            Content = content,
            Message = remoteRefreshError ?? (string.IsNullOrWhiteSpace(content) ? "没有可显示的差异。" : null)
        };
    }

    private static async Task<string?> RefreshRemoteRefsAsync(string rootPath, CancellationToken cancellationToken)
    {
        if (RecentRemoteRefreshes.TryGetValue(rootPath, out var refreshedAt) && DateTimeOffset.UtcNow - refreshedAt < TimeSpan.FromSeconds(2)) return null;
        var remotes = await RunGitAsync(rootPath, ["remote"], cancellationToken);
        if (remotes.ExitCode != 0 || ToLines(remotes.Output).Count == 0) return null;
        // Refresh tracking refs so the UI represents the current remote rather than a stale local cache.
        var fetch = await RunGitAsync(rootPath, ["fetch", "--quiet", "--prune"], cancellationToken);
        if (fetch.ExitCode == 0)
        {
            RecentRemoteRefreshes[rootPath] = DateTimeOffset.UtcNow;
            return null;
        }
        return string.IsNullOrWhiteSpace(fetch.Output) ? "无法刷新远程分支。" : fetch.Output;
    }

    private static List<string> ToLines(string value) => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task<(int Behind, int Ahead)> GetTrackingCountsAsync(string rootPath, string? remoteBranch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteBranch)) return (0, 0);
        var result = await RunGitAsync(rootPath, ["rev-list", "--left-right", "--count", $"{remoteBranch}...HEAD"], cancellationToken);
        var values = result.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return result.ExitCode == 0 && values.Length >= 2 && int.TryParse(values[0], out var behind) && int.TryParse(values[1], out var ahead)
            ? (behind, ahead)
            : (0, 0);
    }

    private static async Task<int> GetChangedFileCountAsync(string rootPath, string? from, string? to, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return 0;
        var result = await RunGitAsync(rootPath, ["diff", "--name-only", $"{from}...{to}"], cancellationToken);
        return result.ExitCode == 0
            ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length
            : 0;
    }

    private static string? GetRemoteName(string? remoteBranch)
    {
        if (string.IsNullOrWhiteSpace(remoteBranch)) return null;
        var separator = remoteBranch.IndexOf('/');
        return separator > 0 ? remoteBranch[..separator] : null;
    }

    private static async Task<GitProcessResult> RunGitAsync(string rootPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = rootPath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // The server cannot answer an interactive credential prompt; fail clearly instead of leaving the runtime menu waiting.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start Git. Ensure Git is installed on the server.");
        var output = new StringBuilder();
        output.Append(await process.StandardOutput.ReadToEndAsync(cancellationToken));
        output.Append(await process.StandardError.ReadToEndAsync(cancellationToken));
        await process.WaitForExitAsync(cancellationToken);
        return new GitProcessResult(process.ExitCode, output.ToString().Trim());
    }

    private static string NormalizeCommitMessage(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "chore: update workspace" : message.Trim();
        return normalized.Length > 200 ? normalized[..200] : normalized;
    }

    private static string JoinOutput(IEnumerable<string> output) => string.Join("\n", output.Where(item => !string.IsNullOrWhiteSpace(item)));

    private sealed record GitProcessResult(int ExitCode, string Output);
}
