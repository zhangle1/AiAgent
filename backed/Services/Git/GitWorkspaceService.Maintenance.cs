namespace AiAgent.Backend.Services.Git;

public sealed partial class GitWorkspaceService
{
    public Task<string> PrepareMaintenanceAsync(string sourceRoot, string destination, string branch, CancellationToken token, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync("maintenance:" + destination, async () =>
        {
            if (!branch.StartsWith("maintenance/", StringComparison.Ordinal)) throw new ArgumentException("Invalid maintenance branch.");
            if (Directory.Exists(destination)) throw new InvalidOperationException("养护副本已经存在，禁止覆盖。");
            var remote = await RunGitAsync(sourceRoot, ["remote", "get-url", "origin"], token);
            var current = await RunGitAsync(sourceRoot, ["branch", "--show-current"], token);
            if (remote.ExitCode != 0 || current.ExitCode != 0 || string.IsNullOrWhiteSpace(current.Output))
                throw new InvalidOperationException("代码库必须配置 origin，并检出有效分支。");
            // Credentials belong to the current task owner and are supplied only to Git.
            var clone = await RunGitAsync(Path.GetDirectoryName(destination)!, ["clone", "--no-local", "--single-branch", "--branch", current.Output.Trim(), "--", remote.Output.Trim(), destination], token);
            if (clone.ExitCode != 0) throw new InvalidOperationException("创建养护副本失败：" + clone.Output);
            var checkout = await RunGitAsync(destination, ["checkout", "-b", branch], token);
            var tracking = await RunGitAsync(destination, ["branch", "--set-upstream-to=origin/" + current.Output.Trim()], token);
            if (checkout.ExitCode != 0 || tracking.ExitCode != 0) throw new InvalidOperationException("创建养护分支失败。");
            await RunGitAsync(destination, ["config", "user.name", "AiAgent Maintenance"], token);
            await RunGitAsync(destination, ["config", "user.email", "maintenance@aiagent.local"], token);
            var head = await RunGitAsync(destination, ["rev-parse", "HEAD"], token);
            if (head.ExitCode != 0) throw new InvalidOperationException("无法读取养护基线。");
            return head.Output.Trim();
        }, token, credential);

    public Task<GitOperationResult> PushMaintenanceAsync(string root, string branch, string baseHead, string snapshot, string message, CancellationToken token, GitWorkspaceCredential? credential = null)
        => RunExclusiveAsync("maintenance:" + root, async () =>
        {
            if (!branch.StartsWith("maintenance/", StringComparison.Ordinal)) throw new ArgumentException("Invalid maintenance branch.");
            var validation = await GetDeliveryValidationAsync(root, token);
            var head = await RunGitAsync(root, ["rev-parse", "HEAD"], token);
            if (!validation.IsValid || validation.Branch != branch || head.Output.Trim() != baseHead || validation.SnapshotSha256 != snapshot)
                throw new InvalidOperationException("编译后代码、基线或远程状态发生变化，禁止推送，请重新执行养护。");
            var add = await RunGitAsync(root, ["add", "--all"], token);
            if (add.ExitCode != 0) throw new InvalidOperationException("暂存养护改动失败：" + add.Output);
            var commit = await RunGitAsync(root, ["commit", "-m", message], token);
            if (commit.ExitCode != 0) throw new InvalidOperationException("提交养护改动失败：" + commit.Output);
            // A unique branch per run, never force push or change the source branch.
            var push = await RunGitAsync(root, ["push", "origin", "HEAD:refs/heads/" + branch], token);
            var sha = await RunGitAsync(root, ["rev-parse", "HEAD"], token);
            return new GitOperationResult { Ok = push.ExitCode == 0, Action = "push", CommitSha = sha.Output.Trim(), Output = commit.Output + "\n" + push.Output };
        }, token, credential);
}
