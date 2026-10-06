using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Services.Git;
using Xunit;

public sealed class GitHistoryTests
{
    [Fact]
    public async Task HistoryPaginatesLocalCommitsWithoutChangingWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "aiagent-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Git(root, "init");
            var service = new GitWorkspaceService();
            Assert.Empty(await service.HistoryAsync(root, root, 0, CancellationToken.None));
            for (var i = 0; i < 52; i++)
                await Git(root, "-c", "user.name=History Test", "-c", "user.email=history@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", $"提交 {i}");
            await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "preserve");
            var head = await Git(root, "rev-parse", "HEAD");
            var status = await Git(root, "status", "--porcelain");
            var first = await service.HistoryAsync(root, root, 0, CancellationToken.None);
            var second = await service.HistoryAsync(root, root, 50, CancellationToken.None);
            Assert.Equal(50, first.Count);
            Assert.Equal(2, second.Count);
            Assert.Equal(head.Trim(), first[0].Sha);
            Assert.Equal("提交 51", first[0].Subject);
            Assert.Equal("History Test", first[0].Author);
            Assert.Equal(first[1].Sha, Assert.Single(first[0].Parents));
            Assert.Empty(second.Last().Parents);
            Assert.Empty(first.Select(x => x.Sha).Intersect(second.Select(x => x.Sha)));
            Assert.Equal(head, await Git(root, "rev-parse", "HEAD"));
            Assert.Equal(status, await Git(root, "status", "--porcelain"));
            Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(root, "untracked.txt")));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static async Task<string> Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
