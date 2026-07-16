using System.Diagnostics;
using System.Text;
using AiAgent.Backend.Dtos.DashboardApp;

namespace AiAgent.Backend.Services.DashboardApp;

public interface IDashboardGitService
{
    Task<object> StatusAsync(string applicationId, CancellationToken cancellationToken);
    Task<object> PullAsync(string applicationId, CancellationToken cancellationToken);
    Task<object> CommitAndPushAsync(string applicationId, DashboardGitPushRequest request, CancellationToken cancellationToken);
}

/// <summary>Runs a small allow-listed Git workflow inside the selected dashboard workspace.</summary>
public sealed class DashboardGitService : IDashboardGitService
{
    private readonly IDashboardApplicationWorkspace _workspace;

    public DashboardGitService(IDashboardApplicationWorkspace workspace) => _workspace = workspace;

    public async Task<object> StatusAsync(string applicationId, CancellationToken cancellationToken)
    {
        var app = await _workspace.GetAsync(applicationId, cancellationToken);
        return await GetStatusAsync(app.RootPath, cancellationToken);
    }

    public async Task<object> PullAsync(string applicationId, CancellationToken cancellationToken)
    {
        var app = await _workspace.GetAsync(applicationId, cancellationToken);
        await RequireRepositoryAsync(app.RootPath, cancellationToken);
        var result = await RunGitAsync(app.RootPath, ["pull", "--ff-only"], cancellationToken);
        var status = await GetStatusAsync(app.RootPath, cancellationToken);
        return new { ok = result.ExitCode == 0, action = "pull", output = result.Output, status };
    }

    public async Task<object> CommitAndPushAsync(string applicationId, DashboardGitPushRequest request, CancellationToken cancellationToken)
    {
        var app = await _workspace.GetAsync(applicationId, cancellationToken);
        await RequireRepositoryAsync(app.RootPath, cancellationToken);
        var output = new List<string>();
        var add = await RunGitAsync(app.RootPath, ["add", "--all"], cancellationToken);
        output.Add(add.Output);
        if (add.ExitCode != 0) return new { ok = false, action = "push", output = string.Join("\n", output), status = await GetStatusAsync(app.RootPath, cancellationToken) };
        var cached = await RunGitAsync(app.RootPath, ["diff", "--cached", "--quiet"], cancellationToken);
        if (cached.ExitCode == 1)
        {
            var message = string.IsNullOrWhiteSpace(request.Message) ? $"feat(dashboard): update {app.Name}" : request.Message.Trim();
            if (message.Length > 200) message = message[..200];
            var commit = await RunGitAsync(app.RootPath, ["commit", "-m", message], cancellationToken);
            output.Add(commit.Output);
            if (commit.ExitCode != 0) return new { ok = false, action = "push", output = string.Join("\n", output), status = await GetStatusAsync(app.RootPath, cancellationToken) };
        }
        else if (cached.ExitCode != 0) return new { ok = false, action = "push", output = "Unable to inspect staged changes.\n" + cached.Output, status = await GetStatusAsync(app.RootPath, cancellationToken) };
        var push = await RunGitAsync(app.RootPath, ["push"], cancellationToken);
        output.Add(push.Output);
        var status = await GetStatusAsync(app.RootPath, cancellationToken);
        return new { ok = push.ExitCode == 0, action = "push", output = string.Join("\n", output.Where(item => !string.IsNullOrWhiteSpace(item))), status };
    }

    private async Task RequireRepositoryAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var check = await RunGitAsync(workingDirectory, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.ExitCode != 0 || !check.Output.Contains("true", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected code library is not a Git working tree. Initialize or clone it before using Git management.");
    }

    private async Task<object> GetStatusAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var check = await RunGitAsync(workingDirectory, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.ExitCode != 0 || !check.Output.Contains("true", StringComparison.OrdinalIgnoreCase)) return new { is_repository = false, branch = (string?)null, changes = Array.Empty<string>(), ahead = 0, behind = 0, output = check.Output };
        var branch = await RunGitAsync(workingDirectory, ["branch", "--show-current"], cancellationToken);
        var porcelain = await RunGitAsync(workingDirectory, ["status", "--branch", "--porcelain"], cancellationToken);
        var lines = porcelain.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var head = lines.FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal)) ?? string.Empty;
        var ahead = ParseTrackingCount(head, "ahead ");
        var behind = ParseTrackingCount(head, "behind ");
        return new { is_repository = true, branch = branch.Output.Trim(), changes = lines.Where(line => !line.StartsWith("## ", StringComparison.Ordinal)).Take(100).ToArray(), ahead, behind, output = porcelain.Output };
    }

    private static int ParseTrackingCount(string value, string label)
    {
        var index = value.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return 0;
        var tail = value[(index + label.Length)..];
        var digits = new string(tail.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var count) ? count : 0;
    }

    private static async Task<GitResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start Git. Ensure Git is installed on the server.");
        var output = new StringBuilder();
        output.Append(await process.StandardOutput.ReadToEndAsync(cancellationToken));
        output.Append(await process.StandardError.ReadToEndAsync(cancellationToken));
        await process.WaitForExitAsync(cancellationToken);
        return new GitResult(process.ExitCode, output.ToString().Trim());
    }

    private sealed record GitResult(int ExitCode, string Output);
}
