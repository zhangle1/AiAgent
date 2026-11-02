using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AiAgent.Backend.Services.Git;

namespace AiAgent.Backend.Services.ChangeAtlas;

public sealed record AtlasRawChange(string Path, int? Added, int? Deleted);
public sealed record AtlasRawCommit(string Sha, string? Parent, bool IsMerge, string AuthorId, string Author, DateTime Date, string Subject, List<AtlasRawChange> Changes, bool Truncated);
public sealed record AtlasGitCapture(string Head, string Ref, string Source, List<string> Refs, List<string> Paths, List<AtlasRawCommit> Commits, List<string> Warnings);
public interface IChangeAtlasGit
{
    Task<AtlasGitCapture> CaptureAsync(string source, string cache, string snapshotId, string requestedRef, bool refreshRemote, DateTime since, GitWorkspaceCredential? credential, CancellationToken token);
    Task<(string Patch, bool Truncated)> PatchAsync(string cache, string sha, string? parent, string path, CancellationToken token);
}

/// <summary>Reads immutable Git objects in a private bare cache. Never checks out or alters a registered worktree.</summary>
public sealed class ChangeAtlasGitReader : IChangeAtlasGit
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private static readonly Regex Sha = new("\\A(?:[a-f0-9]{40}|[a-f0-9]{64})\\z", RegexOptions.CultureInvariant);
    public const int CommitLimit = 120;
    public const int FileLimit = 5000;

    public async Task<AtlasGitCapture> CaptureAsync(string source, string cache, string snapshotId, string requestedRef, bool refreshRemote, DateTime since, GitWorkspaceCredential? credential, CancellationToken token)
    {
        var gate = _gates.GetOrAdd(cache, _ => new(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var check = await Run(source, ["rev-parse", "--show-toplevel"], token);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (check.Code != 0 || !string.Equals(Path.GetFullPath(check.Text).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar), comparison))
                throw new InvalidOperationException("登记目录必须是 Git 仓库根目录，请在代码库设置中核对。");
            var sourceHead = await Run(source, ["rev-parse", "--verify", "HEAD"], token);
            if (sourceHead.Code != 0) return new("", "HEAD", "local", [], [], [], ["该代码库还没有提交。"]);
            Directory.CreateDirectory(cache);
            if (!File.Exists(Path.Combine(cache, "HEAD"))) await Require(cache, ["init", "--bare", "--quiet"], token);
            // Source references and commits only; uncommitted files and the index are never read.
            await Require(cache, ["fetch", "--quiet", "--no-tags", "--prune", "--", source, "+HEAD:refs/atlas/local-head", "+refs/heads/*:refs/heads/*"], token);
            var branchResult = await Run(source, ["symbolic-ref", "--quiet", "--short", "HEAD"], token);
            var localBranch = branchResult.Code == 0 ? branchResult.Text : "HEAD";
            var warnings = new List<string>();
            var remoteOk = false;
            if (refreshRemote)
            {
                var remote = await Run(source, ["remote", "get-url", "origin"], token);
                if (remote.Code != 0 || string.IsNullOrWhiteSpace(remote.Text)) warnings.Add("未配置 origin，使用本地已提交版本。");
                else
                {
                    // URL is obtained exclusively from the registered repository; never returned to callers.
                    var fetch = await Run(cache, ["fetch", "--quiet", "--no-tags", "--prune", "--", remote.Text, "+refs/heads/*:refs/remotes/origin/*"], token, credential);
                    remoteOk = fetch.Code == 0;
                    if (!remoteOk) warnings.Add("远端同步失败，请检查本人 Git 凭据或网络；当前使用本地已提交版本。");
                }
            }
            var refsResult = await Require(cache, ["for-each-ref", "--format=%(refname)", "refs/heads", "refs/remotes/origin"], token);
            var refs = refsResult.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\r')).ToList();
            // Do not offer an old remote cache as a freshly synchronized branch.
            if (!remoteOk) refs.RemoveAll(x => x.StartsWith("refs/remotes/", StringComparison.Ordinal));
            var selected = requestedRef == "HEAD"
                ? remoteOk && refs.Contains("refs/remotes/origin/" + localBranch) ? "refs/remotes/origin/" + localBranch : "refs/atlas/local-head"
                : refs.Contains(requestedRef, StringComparer.Ordinal) ? requestedRef : throw new ArgumentException("所选分支不在本次可读取的引用中。读取远端分支时请勾选同步远端。");
            var head = (await Require(cache, ["rev-parse", "--verify", selected + "^{commit}"], token)).Text;
            if (!Sha.IsMatch(head)) throw new InvalidOperationException("Git 版本标识无效。");
            await Require(cache, ["update-ref", "refs/atlas/snapshots/" + snapshotId, head], token);
            var log = await Require(cache, ["log", "--no-show-signature", "--max-count=" + (CommitLimit + 1), "--since=" + since.ToString("O"), "--format=%H%x00%P%x00%an%x00%ae%x00%cI%x00%s", head, "--"], token);
            var rows = log.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length > CommitLimit) warnings.Add($"提交较多，仅载入最近 {CommitLimit} 次；请缩小时间或仓库范围。计数仅针对已载入提交。");
            var commits = new List<AtlasRawCommit>();
            foreach (var row in rows.Take(CommitLimit))
            {
                var fields = row.TrimEnd('\r').Split('\0');
                if (fields.Length != 6 || !Sha.IsMatch(fields[0]) || !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                var parents = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var parent = parents.FirstOrDefault();
                if (parent != null && !Sha.IsMatch(parent)) throw new InvalidOperationException("Git 父版本标识无效。");
                // Renames are represented as deletion/addition in v1. Merge activity uses first-parent diff.
                var args = parent == null
                    ? new List<string> { "diff-tree", "--root", "--no-commit-id", "-r", "--numstat", "-z", "--no-renames", "--no-ext-diff", fields[0], "--" }
                    : new List<string> { "diff", "--numstat", "-z", "--no-renames", "--no-ext-diff", "--no-textconv", parent, fields[0], "--" };
                var changes = ParseNumstat((await Require(cache, args, token, raw: true)).Text);
                commits.Add(new(fields[0], parent, parents.Length > 1, Hash(fields[2] + "\0" + fields[3].ToLowerInvariant()), Clip(fields[2], 200), date.UtcDateTime, Clip(fields[5], 1000), changes.Take(500).ToList(), changes.Count > 500));
            }
            var tree = await Require(cache, ["ls-tree", "-r", "--name-only", "-z", head, "--"], token, raw: true);
            var paths = tree.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length > FileLimit) warnings.Add($"目录结构仅载入前 {FileLimit} 个文件；提交中的改动文件仍会补入。尚未进行函数调用分析。");
            if (commits.Any(x => x.Truncated)) warnings.Add("部分大提交的文件列表超过 500 项，已标记截断；相关计数为已载入部分。");
            var knownAtHead = paths.ToHashSet(StringComparer.Ordinal);
            var selectedPaths = paths.Take(FileLimit).Concat(commits.SelectMany(x => x.Changes).Select(x => x.Path).Where(knownAtHead.Contains)).Distinct(StringComparer.Ordinal).ToList();
            return new(head, selected == "refs/atlas/local-head" ? localBranch : selected, selected.StartsWith("refs/remotes/", StringComparison.Ordinal) ? "remote" : "local", refs, selectedPaths, commits, warnings);
        }
        finally { gate.Release(); }
    }

    public async Task<(string Patch, bool Truncated)> PatchAsync(string cache, string sha, string? parent, string path, CancellationToken token)
    {
        if (!Sha.IsMatch(sha) || parent != null && !Sha.IsMatch(parent) || !ValidPath(path)) throw new ArgumentException("无效的版本或文件。");
        var args = parent == null
            ? new List<string> { "diff-tree", "--root", "--no-commit-id", "-r", "-p", "--no-renames", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3", sha, "--", path }
            : new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--no-color", "--unified=3", parent, sha, "--", path };
        // Literal pathspec prevents a tracked filename such as ':(glob)**' from selecting unrelated files.
        args.Insert(0, "--literal-pathspecs");
        var result = await Run(cache, args, token, maxChars: 24000, allowTruncate: true);
        if (result.Code != 0) throw new InvalidOperationException("无法读取快照差异，请重新生成地图。");
        return (result.Text, result.Truncated);
    }

    public static List<AtlasRawChange> ParseNumstat(string value)
    {
        var rows = new List<AtlasRawChange>();
        foreach (var row in value.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var first = row.IndexOf('\t'); var second = first < 0 ? -1 : row.IndexOf('\t', first + 1);
            if (second < 0) continue;
            var path = row[(second + 1)..];
            if (!ValidPath(path)) continue;
            rows.Add(new(path, int.TryParse(row[..first], out var added) ? added : null, int.TryParse(row[(first + 1)..second], out var deleted) ? deleted : null));
        }
        return rows;
    }
    public static bool ValidPath(string path) => path.Length is > 0 and <= 4096 && !path.StartsWith('/') && !path.Split('/').Contains("..") && !path.Contains('\0');
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..24];
    private static string Clip(string text, int length) => text[..Math.Min(text.Length, length)];
    private sealed record Result(int Code, string Text, bool Truncated);
    private static async Task<Result> Require(string directory, IReadOnlyList<string> args, CancellationToken token, bool raw = false)
    {
        var result = await Run(directory, args, token, raw: raw);
        if (result.Code != 0) throw new InvalidOperationException("无法读取代码库的 Git 版本，请检查登记目录及 Git 可用性。");
        return result;
    }
    private static async Task<Result> Run(string directory, IReadOnlyList<string> args, CancellationToken token, GitWorkspaceCredential? credential = null, bool raw = false, int maxChars = 12_000_000, bool allowTruncate = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("core.fsmonitor=false");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        string? askpass = null;
        if (credential != null)
        {
            askpass = Path.Combine(directory, "askpass-" + Guid.NewGuid().ToString("N") + (OperatingSystem.IsWindows() ? ".cmd" : ".sh"));
            // Git already knows the username. The helper never interpolates prompts or credentials into shell code.
            var script = OperatingSystem.IsWindows()
                ? "@echo off\r\nsetlocal EnableDelayedExpansion\r\necho(!AIAGENT_GIT_TOKEN!\r\n"
                : "#!/bin/sh\nprintf '%s\\n' \"$AIAGENT_GIT_TOKEN\"\n";
            await File.WriteAllTextAsync(askpass, script, timeout.Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(askpass, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            start.Environment["GIT_ASKPASS"] = askpass;
            start.Environment["AIAGENT_GIT_TOKEN"] = credential.AccessToken;
            start.ArgumentList.Insert(0, "credential.helper="); start.ArgumentList.Insert(0, "-c");
            start.ArgumentList.Insert(0, "credential.username=" + credential.Username); start.ArgumentList.Insert(0, "-c");
        }
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 Git。");
            var stdout = Drain(process.StandardOutput, maxChars, timeout.Token);
            var stderr = Drain(process.StandardError, 4096, timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            var output = await stdout;
            if (output.Truncated && !allowTruncate) throw new InvalidOperationException("代码库输出超过本轮读取上限，请选择单个仓库或缩小范围。");
            return new(process.ExitCode, raw ? output.Text : output.Text.Trim(), output.Truncated);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("读取 Git 超时，请检查网络后重试。"); }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(true); } catch { }
            if (askpass != null) try { File.Delete(askpass); } catch { }
        }
    }
    private static async Task<(string Text, bool Truncated)> Drain(StreamReader reader, int max, CancellationToken token)
    {
        var builder = new StringBuilder(); var buffer = new char[8192]; var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            var keep = Math.Min(count, Math.Max(0, max - builder.Length));
            builder.Append(buffer, 0, keep); truncated |= keep < count;
        }
        return (builder.ToString(), truncated);
    }
}
