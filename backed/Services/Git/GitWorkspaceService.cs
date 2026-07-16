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
    Task<GitOperationResult> PullAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken);
    Task<GitOperationResult> CommitAndPushAsync(string workspaceKey, string rootPath, string message, CancellationToken cancellationToken);
}

public sealed class GitWorkspaceStatus
{
    [JsonPropertyName("is_repository")]
    public bool IsRepository { get; set; }
    public string? Branch { get; set; }
    public List<string> Changes { get; set; } = [];
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public string Output { get; set; } = string.Empty;
}

public sealed class GitOperationResult
{
    public bool Ok { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public GitWorkspaceStatus Status { get; set; } = new();
}

public sealed class GitWorkspaceService : IGitWorkspaceService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationGates = new(StringComparer.OrdinalIgnoreCase);

    public Task<GitWorkspaceStatus> StatusAsync(string workspaceKey, string rootPath, CancellationToken cancellationToken)
        => GetStatusAsync(rootPath, cancellationToken);

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
        var branch = await RunGitAsync(rootPath, ["branch", "--show-current"], cancellationToken);
        var porcelain = await RunGitAsync(rootPath, ["status", "--branch", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken);
        var entries = porcelain.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var head = entries.FirstOrDefault(item => item.StartsWith("## ", StringComparison.Ordinal)) ?? string.Empty;
        return new GitWorkspaceStatus
        {
            IsRepository = true,
            Branch = branch.Output.Trim(),
            Changes = entries.Where(item => !item.StartsWith("## ", StringComparison.Ordinal)).Take(200).ToList(),
            Ahead = ParseTrackingCount(head, "ahead "),
            Behind = ParseTrackingCount(head, "behind "),
            Output = porcelain.Output
        };
    }

    private static int ParseTrackingCount(string value, string label)
    {
        var index = value.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return 0;
        var digits = new string(value[(index + label.Length)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var count) ? count : 0;
    }

    private static async Task<GitProcessResult> RunGitAsync(string rootPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = rootPath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
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
