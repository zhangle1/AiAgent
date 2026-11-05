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
        await File.WriteAllTextAsync(file, "second");
        Assert.NotEqual(initial.SnapshotSha256, (await git.ValidateTeamAsync(local, default)).SnapshotSha256);
        // A local commit without a remote branch remains retryable (including first-push failures).
        await Git(local, "add", "--all");
        await Git(local, "commit", "-m", "knowledge by superadmin");
        var beforePush = (await Git(local, "rev-parse", "HEAD")).Trim();
        Assert.True((await git.ValidateTeamAsync(local, default)).IsValid);
        var pushed = await git.CommitAndPushAsync("team-knowledge", local, "knowledge", default, authorUsername: "superadmin");
        Assert.True(pushed.Ok, pushed.Output);
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
