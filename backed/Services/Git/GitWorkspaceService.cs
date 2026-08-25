using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace AiAgent.Backend.Services.Git;

/// <summary>
/// Ephemeral HTTPS credentials for one server-side Git command lane. They are
/// supplied through the child-process environment only and never persisted in a repository.
/// </summary>
public sealed record GitWorkspaceCredential(string Username, string AccessToken);

/// <summary>
/// Shared Git command boundary for trusted server workspaces. Callers own path authorization.
/// A workspace has one operation lane, mirroring VS Code SCM's operation manager behavior.
/// </summary>
public interface IGitWorkspaceService
{
    Task<GitWorkspaceStatus> StatusAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitWorkspaceBranches> BranchesAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitWorkspaceDiff> DiffAsync(string workspaceKey, string rootPath, string? comparison, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<List<GitWorkspaceLocalChange>> LocalChangesAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitDeliveryValidation> ValidateDeliveryAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitOperationResult> CheckoutAsync(string workspaceKey, string rootPath, string branch, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitOperationResult> DiscardChangesAndPullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitOperationResult> PullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
    Task<GitOperationResult> CommitAndPushAsync(string workspaceKey, string rootPath, string message, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null);
}

public sealed class GitDeliveryValidation
{
    [JsonPropertyName("snapshot_sha256")] public string SnapshotSha256 { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public List<string> Files { get; set; } = [];
    public List<GitDeliveryCheck> Checks { get; set; } = [];
    [JsonPropertyName("is_valid")] public bool IsValid => Checks.Count > 0 && Checks.All(x => x.Passed);
}
public sealed record GitDeliveryCheck(string Name, bool Passed, string Message);

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
    [JsonPropertyName("commit_sha")]
    public string? CommitSha { get; set; }
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
    public List<GitWorkspaceDiffFile> Files { get; set; } = [];
    public string Content { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class GitWorkspaceDiffFile
{
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("old_path")]
    public string? OldPath { get; set; }
}

public sealed class GitWorkspaceLocalChange
{
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("last_write_time_utc")]
    public DateTime? LastWriteTimeUtc { get; set; }
}

public sealed class GitWorkspaceService : IGitWorkspaceService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLocal<GitWorkspaceCredential?> _credential = new();

    public Task<GitWorkspaceStatus> StatusAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync(workspaceKey, () => GetStatusAsync(rootPath, cancellationToken), cancellationToken, credential);

    public Task<GitWorkspaceBranches> BranchesAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync(workspaceKey, () => GetBranchesAsync(rootPath, cancellationToken), cancellationToken, credential);

    public Task<GitWorkspaceDiff> DiffAsync(string workspaceKey, string rootPath, string? comparison, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync(workspaceKey, () => GetDiffAsync(rootPath, comparison, cancellationToken), cancellationToken, credential);

    public Task<List<GitWorkspaceLocalChange>> LocalChangesAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync(workspaceKey, () => GetLocalChangesAsync(rootPath, cancellationToken), cancellationToken, credential);

    public Task<GitDeliveryValidation> ValidateDeliveryAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync(workspaceKey, () => GetDeliveryValidationAsync(rootPath, cancellationToken), cancellationToken, credential);

    public Task<GitOperationResult> CheckoutAsync(string workspaceKey, string rootPath, string branch, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
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
                Status = await GetStatusAsync(rootPath, cancellationToken, refreshRemoteRefs: false)
            };
        }, cancellationToken, credential);

    public Task<GitOperationResult> DiscardChangesAndPullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
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
                Status = await GetStatusAsync(rootPath, cancellationToken, refreshRemoteRefs: false)
            };
        }, cancellationToken, credential);

    public Task<GitOperationResult> PullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync(workspaceKey, async () =>
        {
            await RequireRepositoryAsync(rootPath, cancellationToken);
            var pull = await RunGitAsync(rootPath, ["pull", "--ff-only"], cancellationToken);
            return new GitOperationResult
            {
                Ok = pull.ExitCode == 0,
                Action = "pull",
                Output = pull.Output,
                Status = await GetStatusAsync(rootPath, cancellationToken, refreshRemoteRefs: false)
            };
        }, cancellationToken, credential);

    public Task<GitOperationResult> CommitAndPushAsync(string workspaceKey, string rootPath, string message, CancellationToken cancellationToken, GitWorkspaceCredential? credential = null)
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
            var commitSha = push.ExitCode == 0 ? await RunGitAsync(rootPath, ["rev-parse", "--short", "HEAD"], cancellationToken) : null;
            return new GitOperationResult
            {
                Ok = push.ExitCode == 0,
                Action = "push",
                Output = JoinOutput(output),
                Status = await GetStatusAsync(rootPath, cancellationToken),
                CommitSha = commitSha?.ExitCode == 0 ? commitSha.Output.Trim() : null
            };
        }, cancellationToken, credential);

    private async Task<T> RunExclusiveAsync<T>(string workspaceKey, Func<Task<T>> operation, CancellationToken cancellationToken, GitWorkspaceCredential? credential)
    {
        var gate = _operationGates.GetOrAdd(workspaceKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var previousCredential = _credential.Value;
        _credential.Value = credential;
        try { return await operation(); }
        finally
        {
            _credential.Value = previousCredential;
            gate.Release();
        }
    }

    private async Task<GitOperationResult> FailureAsync(string action, List<string> output, string rootPath, CancellationToken cancellationToken) => new()
    {
        Ok = false,
        Action = action,
        Output = JoinOutput(output),
        Status = await GetStatusAsync(rootPath, cancellationToken, refreshRemoteRefs: false)
    };

    private async Task RequireRepositoryAsync(string rootPath, CancellationToken cancellationToken)
    {
        var check = await RunGitAsync(rootPath, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.ExitCode != 0 || !check.Output.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected workspace is not a Git working tree. Initialize or clone it before using Git management.");
        }
    }

    private async Task<GitWorkspaceStatus> GetStatusAsync(string rootPath, CancellationToken cancellationToken, bool refreshRemoteRefs = true)
    {
        if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException("The selected Git workspace no longer exists.");
        var check = await RunGitAsync(rootPath, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.ExitCode != 0 || !check.Output.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            return new GitWorkspaceStatus { Output = check.Output };
        }
        var remoteRefreshError = refreshRemoteRefs ? await RefreshRemoteRefsAsync(rootPath, cancellationToken) : null;

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

    private async Task<GitWorkspaceBranches> GetBranchesAsync(string rootPath, CancellationToken cancellationToken)
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

    private async Task<GitWorkspaceDiff> GetDiffAsync(string rootPath, string? comparison, CancellationToken cancellationToken)
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
        var files = await GetDiffFilesAsync(rootPath, range, mode == "working", cancellationToken);
        if (diff.ExitCode != 0)
        {
            var output = diff.Output;
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
            FileCount = files.Count,
            IsTruncated = isTruncated,
            Files = files,
            Content = content,
            Message = remoteRefreshError ?? (string.IsNullOrWhiteSpace(content) ? "没有可显示的差异。" : null)
        };
    }

    private async Task<List<GitWorkspaceDiffFile>> GetDiffFilesAsync(string rootPath, string range, bool includeUntracked, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(rootPath, ["diff", "--name-status", "-z", range], cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Output) ? "Unable to read changed Git files." : result.Output);
        var values = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<GitWorkspaceDiffFile>();
        for (var index = 0; index < values.Length;)
        {
            var status = values[index++].Trim();
            if (string.IsNullOrWhiteSpace(status) || index >= values.Length) break;
            string? oldPath = null;
            string path;
            if (status.StartsWith('R') || status.StartsWith('C'))
            {
                oldPath = values[index++];
                if (index >= values.Length) break;
                path = values[index++];
            }
            else
            {
                path = values[index++];
            }
            files.Add(new GitWorkspaceDiffFile { Status = status, Path = path.Replace('\\', '/'), OldPath = oldPath?.Replace('\\', '/') });
        }
        if (includeUntracked)
        {
            var untracked = await RunGitAsync(rootPath, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken);
            if (untracked.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(untracked.Output) ? "Unable to read untracked Git files." : untracked.Output);
            foreach (var path in untracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = path.Replace('\\', '/');
                if (files.All(file => !string.Equals(file.Path, normalized, StringComparison.Ordinal))) files.Add(new GitWorkspaceDiffFile { Status = "??", Path = normalized });
            }
        }
        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Take(600).ToList();
    }

    private async Task<List<GitWorkspaceLocalChange>> GetLocalChangesAsync(string rootPath, CancellationToken cancellationToken)
    {
        await RequireRepositoryAsync(rootPath, cancellationToken);
        var result = await RunGitAsync(rootPath, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Output) ? "Unable to inspect local Git changes." : result.Output);

        var values = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitWorkspaceLocalChange>();
        for (var index = 0; index < values.Length;)
        {
            var entry = values[index++];
            if (entry.Length < 4) continue;
            var status = entry[..2];
            var path = entry[3..];
            AddLocalChange(changes, rootPath, status, path);
            if ((status[0] is 'R' or 'C' || status[1] is 'R' or 'C') && index < values.Length)
            {
                AddLocalChange(changes, rootPath, status, values[index++]);
            }
        }
        return changes.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase).Take(600).ToList();
    }

    private async Task<GitDeliveryValidation> GetDeliveryValidationAsync(string rootPath, CancellationToken cancellationToken)
    {
        await RequireRepositoryAsync(rootPath, cancellationToken);
        var status = await GetStatusAsync(rootPath, cancellationToken);
        var changes = await GetLocalChangesAsync(rootPath, cancellationToken);
        var diffCheck = await RunGitAsync(rootPath, ["diff", "--check", "HEAD"], cancellationToken);
        var head = await RunGitAsync(rootPath, ["rev-parse", "HEAD"], cancellationToken);
        var fingerprint = new StringBuilder().AppendLine(head.Output.Trim()).AppendLine(status.Branch).AppendLine(status.Output);
        foreach (var change in changes.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            fingerprint.Append(change.Status).Append('\t').AppendLine(change.Path);
            var fullPath = Path.GetFullPath(Path.Combine(rootPath, change.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fullPath))
            {
                await using var stream = File.OpenRead(fullPath);
                fingerprint.AppendLine(Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)));
            }
        }
        var checks = new List<GitDeliveryCheck>
        {
            new("repository", status.IsRepository, status.IsRepository ? "Git repository detected." : "Not a Git repository."),
            new("changes", changes.Count > 0 || status.Ahead > 0, changes.Count > 0 || status.Ahead > 0 ? $"{changes.Count} changed files; {status.Ahead} commits ahead." : "No deliverable changes."),
            new("upstream", !string.IsNullOrWhiteSpace(status.RemoteBranch), string.IsNullOrWhiteSpace(status.RemoteBranch) ? "No upstream branch." : $"Tracking {status.RemoteBranch}."),
            new("remote", string.IsNullOrWhiteSpace(status.RemoteRefreshError), status.RemoteRefreshError ?? "Remote refs refreshed."),
            new("behind", status.Behind == 0, status.Behind == 0 ? "Not behind upstream." : $"Upstream is ahead by {status.Behind} commits."),
            new("diff_check", diffCheck.ExitCode == 0, diffCheck.ExitCode == 0 ? "git diff --check passed." : (string.IsNullOrWhiteSpace(diffCheck.Output) ? "git diff --check failed." : diffCheck.Output))
        };
        return new GitDeliveryValidation
        {
            SnapshotSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))).ToLowerInvariant(),
            Branch = status.Branch,
            Files = changes.Select(x => x.Path).ToList(),
            Checks = checks
        };
    }

    private static void AddLocalChange(List<GitWorkspaceLocalChange> changes, string rootPath, string status, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || changes.Any(change => string.Equals(change.Path, normalized, StringComparison.OrdinalIgnoreCase))) return;
        DateTime? lastWrite = null;
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
            var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(fullPath)) lastWrite = File.GetLastWriteTimeUtc(fullPath);
                else if (Directory.Exists(fullPath)) lastWrite = Directory.GetLastWriteTimeUtc(fullPath);
            }
        }
        catch (ArgumentException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        changes.Add(new GitWorkspaceLocalChange { Path = normalized, Status = status, LastWriteTimeUtc = lastWrite });
    }

    private async Task<string?> RefreshRemoteRefsAsync(string rootPath, CancellationToken cancellationToken)
    {
        var remotes = await RunGitAsync(rootPath, ["remote"], cancellationToken);
        if (remotes.ExitCode != 0 || ToLines(remotes.Output).Count == 0) return null;
        // Refresh tracking refs so the UI represents the current remote rather than a stale local cache.
        var fetch = await RunGitAsync(rootPath, ["fetch", "--quiet", "--prune"], cancellationToken);
        if (fetch.ExitCode == 0) return null;
        return string.IsNullOrWhiteSpace(fetch.Output) ? "无法刷新远程分支。" : fetch.Output;
    }

    private static List<string> ToLines(string value) => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task<(int Behind, int Ahead)> GetTrackingCountsAsync(string rootPath, string? remoteBranch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteBranch)) return (0, 0);
        var result = await RunGitAsync(rootPath, ["rev-list", "--left-right", "--count", $"{remoteBranch}...HEAD"], cancellationToken);
        var values = result.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return result.ExitCode == 0 && values.Length >= 2 && int.TryParse(values[0], out var behind) && int.TryParse(values[1], out var ahead)
            ? (behind, ahead)
            : (0, 0);
    }

    private async Task<int> GetChangedFileCountAsync(string rootPath, string? from, string? to, CancellationToken cancellationToken)
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

    private async Task<GitProcessResult> RunGitAsync(string rootPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = rootPath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // The server cannot answer an interactive credential prompt; fail clearly instead of leaving the runtime menu waiting.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        var credential = _credential.Value;
        string? askPassPath = null;
        if (credential is not null)
        {
            askPassPath = Path.Combine(Path.GetTempPath(), $"aiagent-git-askpass-{Guid.NewGuid():N}.cmd");
            await File.WriteAllTextAsync(askPassPath, "@echo off\r\necho %~1 | findstr /b /i \"Username\" >nul && (echo %AIAGENT_GIT_USERNAME%) || (echo %AIAGENT_GIT_TOKEN%)\r\n", Encoding.ASCII, cancellationToken);
            startInfo.Environment["GIT_ASKPASS"] = askPassPath;
            startInfo.Environment["AIAGENT_GIT_USERNAME"] = credential.Username;
            startInfo.Environment["AIAGENT_GIT_TOKEN"] = credential.AccessToken;
        }
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException("Unable to start Git. Ensure Git is installed on the server.");
            var output = new StringBuilder();
            output.Append(await process.StandardOutput.ReadToEndAsync(cancellationToken));
            output.Append(await process.StandardError.ReadToEndAsync(cancellationToken));
            await process.WaitForExitAsync(cancellationToken);
            return new GitProcessResult(process.ExitCode, SanitizeOutput(output.ToString().Trim(), credential));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(askPassPath))
            {
                try { File.Delete(askPassPath); } catch { }
            }
        }
    }

    private static string NormalizeCommitMessage(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "chore: update workspace" : message.Trim();
        return normalized.Length > 200 ? normalized[..200] : normalized;
    }

    private static string JoinOutput(IEnumerable<string> output) => string.Join("\n", output.Where(item => !string.IsNullOrWhiteSpace(item)));

    private static string SanitizeOutput(string output, GitWorkspaceCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(output)) return output;
        if (credential is not null && !string.IsNullOrWhiteSpace(credential.AccessToken)) output = output.Replace(credential.AccessToken, "***", StringComparison.Ordinal);
        return Regex.Replace(output, @"(https?://[^/\s:@]+:)[^@\s/]+@", "$1***@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record GitProcessResult(int ExitCode, string Output);
}
