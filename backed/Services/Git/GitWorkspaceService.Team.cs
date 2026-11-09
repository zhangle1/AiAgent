namespace AiAgent.Backend.Services.Git;

public sealed partial class GitWorkspaceService
{
    public Task<GitDeliveryValidation> ValidateTeamAsync(string root, CancellationToken token, GitWorkspaceCredential? credential = null, bool force = false)
        => RunExclusiveAsync("team-knowledge", async () => (await ReviewTeamAsync(root, force, token)).Validation, token, credential);

    private sealed record TeamReview(GitDeliveryValidation Validation, string Branch, string RemoteHead);

    private async Task<TeamReview> ReviewTeamAsync(string root, bool force, CancellationToken token)
    {
        await RequireRepositoryAsync(root, token);
        var status = await GetStatusAsync(root, token);
        var branch = await RunGitAsync(root, ["symbolic-ref", "--short", "HEAD"], token);
        var head = await RunGitAsync(root, ["rev-parse", "--verify", "HEAD"], token);
        var remote = await RunGitAsync(root, ["rev-parse", "--verify", "refs/remotes/origin/" + branch.Output.Trim()], token);
        var remoteHead = remote.ExitCode == 0 ? remote.Output.Trim() : "";
        var origin = await RunGitAsync(root, ["remote", "get-url", "origin"], token);
        var pushUrls = await RunGitAsync(root, ["remote", "get-url", "--push", "--all", "origin"], token);
        var targetValid = origin.ExitCode == 0 && pushUrls.ExitCode == 0 && ToLines(pushUrls.Output).Count == 1 && pushUrls.Output.Trim() == origin.Output.Trim();
        var changes = await RunGitAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], token);
        var fingerprint = new System.Text.StringBuilder().AppendLine(head.ExitCode == 0 ? head.Output : "unborn")
            .AppendLine(branch.Output).AppendLine(origin.Output).AppendLine(pushUrls.Output).AppendLine(remoteHead)
            .AppendLine(force ? "local-first" : "fast-forward").AppendLine(changes.Output);
        foreach (var folder in new[] { "resources", "projects", "sessions" })
            if (Directory.Exists(Path.Combine(root, folder)))
                foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) await Hash(file);
        if (File.Exists(Path.Combine(root, "manifest.json"))) await Hash(Path.Combine(root, "manifest.json"));
        var related = remote.ExitCode != 0;
        if (!related && head.ExitCode == 0)
            related = (await RunGitAsync(root, ["merge-base", "--is-ancestor", "refs/remotes/origin/" + branch.Output.Trim(), "HEAD"], token)).ExitCode == 0;
        var validation = new GitDeliveryValidation { Branch = branch.Output.Trim(),
            SnapshotSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString()))),
            Files = changes.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Checks = [new("remote", string.IsNullOrEmpty(status.RemoteRefreshError), status.RemoteRefreshError ?? "远端检查成功"),
                new("target", targetValid && branch.ExitCode == 0, targetValid && branch.ExitCode == 0 ? "目标为 origin 当前分支" : "远程推送地址或当前分支不匹配，请检查配置"),
                new("history", force || related, force ? "本地优先：允许覆盖远端领先或分叉的分支" : related ? "可以安全推送" : "远端已有其他历史或领先，可选择本地优先同步"),
                new("changes", changes.Output.Length > 0 || status.Ahead > 0 || head.ExitCode == 0 && (remote.ExitCode != 0 || force && head.Output.Trim() != remoteHead), "包含本地变更、待推送提交或需要覆盖的远端版本")] };
        return new(validation, branch.Output.Trim(), remoteHead);
        async Task Hash(string file)
        {
            fingerprint.AppendLine(Path.GetRelativePath(root, file));
            await using var stream = File.OpenRead(file);
            fingerprint.AppendLine(Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token)));
        }
    }

    public Task<GitOperationResult> PushTeamAsync(string root, string message, string snapshot, bool force, CancellationToken token, GitWorkspaceCredential? credential = null, string? authorUsername = null)
        => RunExclusiveAsync("team-knowledge", async () =>
        {
            var review = await ReviewTeamAsync(root, force, token);
            if (!review.Validation.IsValid || review.Validation.SnapshotSha256 != snapshot)
                throw new InvalidOperationException("本地内容、推送方式或远端版本已变化，请重新检查并确认推送。");
            var output = new List<string>();
            var add = await RunGitAsync(root, ["add", "--all"], token);
            output.Add(add.Output);
            if (add.ExitCode != 0) return await FailureAsync("push", output, root, token);
            var cached = await RunGitAsync(root, ["diff", "--cached", "--quiet"], token);
            if (cached.ExitCode == 1)
            {
                var commit = await RunGitAsync(root, ["commit", "-m", GitCommitMessage.WithAuthor(message, authorUsername)], token);
                output.Add(commit.Output);
                if (commit.ExitCode != 0) return await FailureAsync("push", output, root, token);
            }
            else if (cached.ExitCode != 0)
            {
                output.Add(cached.Output);
                return await FailureAsync("push", output, root, token);
            }
            var args = new List<string> { "push" };
            // Bind overwrite to the exact remote head reviewed by the user, including an absent branch.
            if (force) args.Add($"--force-with-lease=refs/heads/{review.Branch}:{review.RemoteHead}");
            args.AddRange(["origin", $"HEAD:refs/heads/{review.Branch}"]);
            var push = await RunGitAsync(root, args.ToArray(), token);
            output.Add(push.Output);
            var sha = push.ExitCode == 0 ? await RunGitAsync(root, ["rev-parse", "--short", "HEAD"], token) : null;
            return new GitOperationResult { Ok = push.ExitCode == 0, Action = "push", Output = JoinOutput(output),
                CommitSha = sha?.ExitCode == 0 ? sha.Output.Trim() : null,
                Status = await GetStatusAsync(root, token, refreshRemoteRefs: false) };
        }, token, credential);

    public Task ConfigureTeamAsync(string root, string remote, string branch, CancellationToken token)
        => RunExclusiveAsync("team-knowledge", async () =>
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("请使用不含账号、密码或查询参数的 HTTPS 仓库地址。");
            Directory.CreateDirectory(root);
            await Check(["check-ref-format", "--branch", branch]);
            if (!Directory.Exists(Path.Combine(root, ".git"))) await Check(["init", "--initial-branch=" + branch]);
            var current = await RunGitAsync(root, ["symbolic-ref", "--short", "HEAD"], token);
            if (current.ExitCode != 0 || current.Output.Trim() != branch)
                throw new InvalidOperationException("工作区已有其他分支，首轮不自动切换分支。");
            var origin = await RunGitAsync(root, ["remote", "get-url", "origin"], token);
            if (origin.ExitCode == 0 && origin.Output.Trim() != remote)
                throw new InvalidOperationException("工作区已绑定其他远程，为保护已有历史，请由管理员处理后再配置。");
            if (origin.ExitCode != 0) await Check(["remote", "add", "origin", remote]);
            await Check(["config", "branch." + branch + ".remote", "origin"]);
            await Check(["config", "branch." + branch + ".merge", "refs/heads/" + branch]);
            await Check(["config", "user.name", "AiAgent Knowledge"]);
            await Check(["config", "user.email", "knowledge@aiagent.local"]);
            return true;

            async Task Check(string[] args)
            {
                var result = await RunGitAsync(root, args, token);
                if (result.ExitCode != 0) throw new InvalidOperationException("Git 初始化或配置失败，请检查仓库与分支配置。");
            }
        }, token, null);
}
