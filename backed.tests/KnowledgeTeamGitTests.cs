using System.Diagnostics;
using System.IO;
using AiAgent.Backend.Services.Git;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeTeamGitTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "knowledge-team-git-" + Guid.NewGuid().ToString("N"));
    public KnowledgeTeamGitTests() => Directory.CreateDirectory(root);

    [Fact]
    public void FixedRootSurvivesApplicationDataPathChangesAndLegacyStillWorks()
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["KnowledgeStorage:RootPath"] = root, ["DataPath"] = Path.Combine(root, "old") }).Build();
        var paths = new KnowledgePathService(settings, NullLogger<KnowledgePathService>.Instance);
        Assert.Equal(Path.Combine(root, "storage", "knowledge_bases"), paths.RootPath);
        settings["DataPath"] = Path.Combine(root, "new");
        Assert.Equal(Path.Combine(root, "storage", "knowledge_bases"), paths.RootPath);
        settings["KnowledgeStorage:RootPath"] = "relative";
        Assert.Throws<InvalidOperationException>(() => paths.RootPath);
        settings["KnowledgeStorage:RootPath"] = "";
        Assert.Equal(Path.Combine(root, "new", "knowledge_bases"), paths.RootPath);
    }

    [Fact]
    public void ProjectionKeepsProjectFoldersAndRejectsPersonalAndTraversalPaths()
    {
        Assert.Equal(Path.Combine("projects", "订单系统", "接口.md"), KnowledgeTeamGitService.ExportPath("viking://projects/订单系统/接口.md"));
        Assert.Throws<ArgumentException>(() => KnowledgeTeamGitService.ExportPath("viking://user/alice/private.md"));
        Assert.Throws<ArgumentException>(() => KnowledgeTeamGitService.ExportPath("viking://projects/%2e%2e/secret.md"));
        Assert.DoesNotContain(".git", KnowledgeTeamGitService.ExportPath("viking://projects/.git/config"));
    }

    [Fact]
    public async Task ConfigurationRejectsCredentialsAndDoesNotReplaceExistingRemote()
    {
        var git = new GitWorkspaceService();
        await Assert.ThrowsAsync<ArgumentException>(() => git.ConfigureTeamAsync(root, "https://user:secret@example.invalid/team.git", "main", default));
        await git.ConfigureTeamAsync(root, "https://example.invalid/team.git", "main", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => git.ConfigureTeamAsync(root, "https://example.invalid/other.git", "main", default));
        Assert.Equal("https://example.invalid/team.git", (await Git(root, "remote", "get-url", "origin")).Trim());
    }

    [Fact]
    public async Task EmptyRemoteFirstPushSnapshotChangesAndRemoteAheadAreHandled()
    {
        var remote = Path.Combine(root, "remote.git");
        await Git(root, "init", "--bare", remote);
        var local = Path.Combine(root, "local"); Directory.CreateDirectory(local);
        await Git(local, "init", "--initial-branch=main");
        await Git(local, "remote", "add", "origin", remote);
        await Git(local, "config", "branch.main.remote", "origin");
        await Git(local, "config", "branch.main.merge", "refs/heads/main");
        await Git(local, "config", "user.name", "Test"); await Git(local, "config", "user.email", "test@example.invalid");
        Directory.CreateDirectory(Path.Combine(local, "projects"));
        var file = Path.Combine(local, "projects", "guide.md"); await File.WriteAllTextAsync(file, "first");
        var git = new GitWorkspaceService();
        var initial = await git.ValidateTeamAsync(local, default);
        Assert.True(initial.IsValid);
        Directory.CreateDirectory(Path.Combine(local, "sessions", "owner"));
        var session = Path.Combine(local, "sessions", "owner", "conversation.md");
        await File.WriteAllTextAsync(session, "published original");
        var sessionSnapshot = (await git.ValidateTeamAsync(local, default)).SnapshotSha256;
        await File.WriteAllTextAsync(session, "edited published copy");
        Assert.NotEqual(sessionSnapshot, (await git.ValidateTeamAsync(local, default)).SnapshotSha256);
        await File.WriteAllTextAsync(file, "second");
        Assert.NotEqual(initial.SnapshotSha256, (await git.ValidateTeamAsync(local, default)).SnapshotSha256);
        // A local commit without a remote branch remains retryable (including first-push failures).
        await Git(local, "add", "--all");
        await Git(local, "commit", "-m", "knowledge by superadmin");
        var beforePush = (await Git(local, "rev-parse", "HEAD")).Trim();
        Assert.True((await git.ValidateTeamAsync(local, default)).IsValid);
        var pushed = await git.CommitAndPushAsync("team-knowledge", local, "knowledge", default, authorUsername: "superadmin");
        Assert.True(pushed.Ok, pushed.Output);
        Assert.Contains("sessions/owner/conversation.md", await Git(local, "ls-tree", "--name-only", "-r", "HEAD"));
        Assert.Equal(beforePush, (await Git(local, "rev-parse", "HEAD")).Trim());
        Assert.EndsWith("by superadmin", (await Git(local, "log", "-1", "--format=%B")).Trim());
        Assert.False((await git.ValidateTeamAsync(local, default)).IsValid);
        var other = Path.Combine(root, "other");
        await Git(root, "clone", "--branch", "main", remote, other);
        await Git(other, "-c", "user.name=Other", "-c", "user.email=other@example.invalid", "commit", "--allow-empty", "-m", "remote change");
        await Git(other, "push");
        await File.WriteAllTextAsync(file, "third");
        var behind = await git.ValidateTeamAsync(local, default);
        Assert.False(behind.IsValid);
        Assert.Contains(behind.Checks, check => check.Name == "history" && !check.Passed);
    }

    [Fact]
    public async Task FirstLocalPublicationCanReplaceAnExistingRemoteHistory()
    {
        var (remote, _, _, _) = await Repositories();
        var local = Path.Combine(root, "new-publication"); Directory.CreateDirectory(local);
        await Git(local, "init", "--initial-branch=main"); await Git(local, "remote", "add", "origin", remote);
        await Git(local, "config", "user.name", "Knowledge"); await Git(local, "config", "user.email", "knowledge@example.invalid");
        Directory.CreateDirectory(Path.Combine(local, "sessions"));
        await File.WriteAllTextAsync(Path.Combine(local, "sessions", "original.md"), "selected local session");
        var git = new GitWorkspaceService();
        var review = await git.ValidateTeamAsync(local, default, force: true);
        Assert.True(review.IsValid);
        var result = await git.PushTeamAsync(local, "首次本地同步", review.SnapshotSha256, true, default);
        Assert.True(result.Ok, result.Output);
        Assert.Equal((await Git(local, "rev-parse", "HEAD")).Trim(), (await Git(remote, "rev-parse", "main")).Trim());
        Assert.Contains("sessions/original.md", await Git(remote, "ls-tree", "--name-only", "-r", "main"));
    }

    [Fact]
    public async Task LocalFirstOverwritesRemoteAheadDivergenceAndUnrelatedHistoryWithoutPulling()
    {
        var (remote, local, other, file) = await Repositories();
        var git = new GitWorkspaceService();
        var originalHead = (await Git(local, "rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(other, "remote-only.md"), "remote content");
        await Git(other, "add", "--all"); await Git(other, "commit", "-m", "remote ahead"); await Git(other, "push");
        Assert.False((await git.ValidateTeamAsync(local, default)).IsValid);
        var overwrite = await git.ValidateTeamAsync(local, default, force: true);
        Assert.True(overwrite.IsValid);
        var result = await git.PushTeamAsync(local, "以本地为准", overwrite.SnapshotSha256, true, default);
        Assert.True(result.Ok, result.Output);
        Assert.Equal(originalHead, (await Git(remote, "rev-parse", "main")).Trim());
        Assert.DoesNotContain("remote-only.md", await Git(remote, "ls-tree", "--name-only", "-r", "main"));
        Assert.False(File.Exists(Path.Combine(local, "remote-only.md")));
        // The other clone still has its old commit, which can move the remote ahead again.
        await Git(other, "push");
        await File.WriteAllTextAsync(file, "local authority");
        await Git(local, "add", "--all"); await Git(local, "commit", "-m", "local divergence");
        Assert.False((await git.ValidateTeamAsync(local, default)).IsValid);
        var diverged = await git.ValidateTeamAsync(local, default, force: true);
        Assert.True((await git.PushTeamAsync(local, "本地分支", diverged.SnapshotSha256, true, default)).Ok);
        var localHead = (await Git(local, "rev-parse", "HEAD")).Trim();
        var tree = (await Git(other, "rev-parse", "HEAD^{tree}")).Trim();
        var unrelated = (await Git(other, "commit-tree", tree, "-m", "unrelated root")).Trim();
        await Git(other, "push", "--force", "origin", unrelated + ":refs/heads/main");
        var unrelatedReview = await git.ValidateTeamAsync(local, default, force: true);
        Assert.True((await git.PushTeamAsync(local, "覆盖独立历史", unrelatedReview.SnapshotSha256, true, default)).Ok);
        Assert.Equal(localHead, (await Git(remote, "rev-parse", "main")).Trim());
    }

    [Fact]
    public async Task ReviewBindsRemoteHeadAndPushModeAndRejectsStaleConfirmationBeforeCommitting()
    {
        var (_, local, other, file) = await Repositories();
        var git = new GitWorkspaceService();
        await File.WriteAllTextAsync(file, "pending local change");
        var review = await git.ValidateTeamAsync(local, default, force: true);
        var head = (await Git(local, "rev-parse", "HEAD")).Trim();
        await Assert.ThrowsAsync<InvalidOperationException>(() => git.PushTeamAsync(local, "changed mode", review.SnapshotSha256, false, default));
        await Git(other, "commit", "--allow-empty", "-m", "new remote commit"); await Git(other, "push");
        await Assert.ThrowsAsync<InvalidOperationException>(() => git.PushTeamAsync(local, "stale", review.SnapshotSha256, true, default));
        Assert.Equal(head, (await Git(local, "rev-parse", "HEAD")).Trim());
        Assert.NotEqual(review.SnapshotSha256, (await git.ValidateTeamAsync(local, default, force: true)).SnapshotSha256);
    }

    [Fact]
    public async Task ForceLeaseRejectsRemoteChangeDuringCommitAndRetryKeepsTheLocalCommit()
    {
        var (remote, local, other, file) = await Repositories();
        var git = new GitWorkspaceService();
        await Git(other, "commit", "--allow-empty", "-m", "concurrent remote");
        var concurrent = (await Git(other, "rev-parse", "HEAD")).Trim();
        await Git(other, "push", "origin", "HEAD:refs/heads/race");
        var hook = Path.Combine(local, ".git", "hooks", "pre-commit");
        var escapedRemote = remote.Replace('\\', '/').Replace("'", "'\"'\"'");
        await File.WriteAllTextAsync(hook, $"#!/bin/sh\ngit -C '{escapedRemote}' update-ref refs/heads/main refs/heads/race\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await File.WriteAllTextAsync(file, "pending local version");
        var review = await git.ValidateTeamAsync(local, default, force: true);
        var rejected = await git.PushTeamAsync(local, "local commit", review.SnapshotSha256, true, default);
        Assert.False(rejected.Ok);
        Assert.Equal(concurrent, (await Git(remote, "rev-parse", "main")).Trim());
        var retained = (await Git(local, "rev-parse", "HEAD")).Trim();
        Assert.NotEqual(concurrent, retained);
        File.Delete(hook);
        var retry = await git.ValidateTeamAsync(local, default, force: true);
        var delivered = await git.PushTeamAsync(local, "retry", retry.SnapshotSha256, true, default);
        Assert.True(delivered.Ok, delivered.Output);
        Assert.Equal(retained, (await Git(local, "rev-parse", "HEAD")).Trim());
        Assert.Equal(retained, (await Git(remote, "rev-parse", "main")).Trim());
    }

    private async Task<(string Remote, string Local, string Other, string File)> Repositories()
    {
        var remote = Path.Combine(root, "remote.git"); var local = Path.Combine(root, "local"); var other = Path.Combine(root, "other");
        await Git(root, "init", "--bare", remote); Directory.CreateDirectory(local);
        await Git(local, "init", "--initial-branch=main"); await Git(local, "remote", "add", "origin", remote);
        await Git(local, "config", "user.name", "Fixture"); await Git(local, "config", "user.email", "fixture@example.invalid");
        Directory.CreateDirectory(Path.Combine(local, "resources")); var file = Path.Combine(local, "resources", "guide.md");
        await File.WriteAllTextAsync(file, "local original"); await Git(local, "add", "--all"); await Git(local, "commit", "-m", "initial");
        await Git(local, "push", "--set-upstream", "origin", "main"); await Git(root, "clone", "--branch", "main", remote, other);
        await Git(other, "config", "user.name", "Other"); await Git(other, "config", "user.email", "other@example.invalid");
        return (remote, local, other, file);
    }

    private static async Task<string> Git(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
