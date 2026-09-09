using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.CodeRepository;

/// <summary>Fixed build commands only; all discovery stays inside the isolated checkout.</summary>
public sealed class RepositoryMaintenanceBuildService
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "node_modules", "bin", "obj", ".next", "data", "dist", "build", ".venv", "venv" };

    public static string SafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("养护路径越界。");
        var cursor = full;
        while (cursor.Length >= fullRoot.Length)
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("养护路径包含符号链接。");
            cursor = Path.GetDirectoryName(cursor)!;
        }
        return full;
    }

    public static bool IsForbiddenChange(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Any(p => Excluded.Contains(p) || p.Equals(".env", StringComparison.OrdinalIgnoreCase) || p.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
            || parts[^1].Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
            || parts[^1].EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || parts[^1].EndsWith(".pem", StringComparison.OrdinalIgnoreCase);
    }

    public static string Redact(string? value)
    {
        var text = Regex.Replace(value ?? "", @"(?i)(https?://)[^\s/@]+:[^\s/@]+@", "$1[redacted]@");
        text = Regex.Replace(text, @"(?im)(token|password|secret|api[_-]?key|authorization|connectionstring)\s*[:=]\s*[^\r\n]+", "$1=[redacted]");
        return text.Length > 24000 ? text[..24000] + "\n[日志已截断]" : text;
    }

    public static List<string> Discover(string root)
    {
        var files = new List<string>();
        var pending = new Queue<string>(); pending.Enqueue(root);
        var visited = 0;
        while (pending.TryDequeue(out var directory))
        {
            if (++visited > 10000) throw new InvalidOperationException("仓库目录过多，无法自动确定编译范围。");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                if ((attrs & FileAttributes.Directory) != 0) { if (!Excluded.Contains(Path.GetFileName(entry))) pending.Enqueue(entry); }
                else if (entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(entry) == "package.json") files.Add(Path.GetRelativePath(root, entry));
            }
        }
        return files.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public async Task BuildAsync(string root, Func<string, Task> log, CancellationToken token)
    {
        var targets = Discover(root);
        var count = 0;
        foreach (var relative in targets)
        {
            token.ThrowIfCancellationRequested();
            var target = SafePath(root, relative);
            var directory = Path.GetDirectoryName(target)!;
            if (relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await log("编译 " + relative);
                await RunAsync(directory, "dotnet", ["build", target, "--nologo"], log, token);
                var projectText = await File.ReadAllTextAsync(target, token);
                if (projectText.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
                    await RunAsync(directory, "dotnet", ["test", target, "--no-build", "--nologo"], log, token);
                count++;
            }
            else
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(target, token));
                if (!json.RootElement.TryGetProperty("scripts", out var scripts) || !scripts.TryGetProperty("build", out _)) continue;
                if (!File.Exists(Path.Combine(directory, "package-lock.json"))) throw new InvalidOperationException(relative + " 缺少 package-lock.json；当前自动编译支持 npm ci。");
                await log("安装依赖并编译 " + relative);
                await NpmAsync(directory, "ci", log, token);
                await NpmAsync(directory, "run build", log, token);
                count++;
            }
        }
        if (count == 0) throw new InvalidOperationException("未发现支持的编译目标（.csproj 或含 build 脚本和 npm 锁文件的 package.json），禁止推送。");
        await log($"编译验证通过：{count} 个目标。");
    }

    private static Task NpmAsync(string directory, string command, Func<string, Task> log, CancellationToken token)
        => OperatingSystem.IsWindows()
            ? RunAsync(directory, "cmd.exe", ["/d", "/s", "/c", "npm " + command], log, token)
            : RunAsync(directory, "npm", command.Split(' '), log, token);

    private static async Task RunAsync(string directory, string command, string[] args, Func<string, Task> log, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var start = new ProcessStartInfo(command) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.Environment["CI"] = "true";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        var output = new StringBuilder();
        async Task Drain(StreamReader reader)
        {
            var buffer = new char[2048]; int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
                lock (output) { if (output.Length < 20000) output.Append(buffer, 0, Math.Min(read, 20000 - output.Length)); }
        }
        try
        {
            await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync(timeout.Token));
            await log(Redact(output.ToString()));
            if (process.ExitCode != 0) throw new InvalidOperationException($"{command} 编译/测试失败，退出码 {process.ExitCode}。");
        }
        finally { if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } } }
    }
}
