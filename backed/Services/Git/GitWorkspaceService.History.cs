using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Git;

public sealed class GitHistoryCommit
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
    [JsonPropertyName("parents")] public string[] Parents { get; set; } = [];
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("subject")] public string Subject { get; set; } = "";
}

public sealed partial class GitWorkspaceService
{
    // Local HEAD only: no fetch, checkout, credentials or working-tree mutation.
    public Task<List<GitHistoryCommit>> HistoryAsync(string workspaceKey, string rootPath, int skip, CancellationToken cancellationToken)
        => RunExclusiveAsync(workspaceKey, async () =>
        {
            await RequireRepositoryAsync(rootPath, cancellationToken);
            var head = await RunGitAsync(rootPath, ["rev-parse", "--verify", "HEAD"], cancellationToken);
            if (head.ExitCode != 0) return new List<GitHistoryCommit>();
            var result = await RunGitAsync(rootPath,
                ["log", "--no-show-signature", "--max-count=50", $"--skip={Math.Clamp(skip, 0, 10000)}", "--format=%H%x00%P%x00%an%x00%aI%x00%s", "HEAD", "--"], cancellationToken);
            if (result.ExitCode != 0) throw new InvalidOperationException("无法读取本地 Git 历史。");
            return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Split('\0'))
                .Where(parts => parts.Length == 5)
                .Select(parts => new GitHistoryCommit
                {
                    Sha = parts[0], Parents = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    Author = parts[2][..Math.Min(parts[2].Length, 200)], Date = parts[3],
                    Subject = parts[4][..Math.Min(parts[4].Length, 1000)]
                }).ToList();
        }, cancellationToken, null);
}
