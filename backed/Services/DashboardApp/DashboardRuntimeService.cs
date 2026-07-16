using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AiAgent.Backend.Services.DashboardApp;

public interface IDashboardRuntimeService
{
    Task<object> StartAsync(string applicationId, CancellationToken cancellationToken);
    Task<object> StopAsync(string applicationId, CancellationToken cancellationToken);
    Task<object> StatusAsync(string applicationId, CancellationToken cancellationToken);
}

/// <summary>Starts only the known Vite dev command for template workspaces and keeps a bounded, observable process log.</summary>
public sealed class DashboardRuntimeService : IDashboardRuntimeService, IDisposable
{
    private readonly IDashboardApplicationWorkspace _workspace;
    private readonly ILogger<DashboardRuntimeService> _logger;
    private readonly ConcurrentDictionary<string, RuntimeSession> _sessions = new(StringComparer.Ordinal);

    public DashboardRuntimeService(IDashboardApplicationWorkspace workspace, ILogger<DashboardRuntimeService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<object> StartAsync(string applicationId, CancellationToken cancellationToken)
    {
        var application = await _workspace.GetAsync(applicationId, cancellationToken);
        if (string.IsNullOrWhiteSpace(application.TemplateId)) throw new InvalidOperationException("Only preloaded dashboard templates can be started by the managed runtime.");
        if (_sessions.TryGetValue(applicationId, out var active) && active.Process is { HasExited: false }) return ToPayload(active);

        var packagePath = Path.Combine(application.RootPath, "package.json");
        if (!File.Exists(packagePath)) throw new InvalidOperationException("The workspace does not contain package.json.");
        ValidateViteDevScript(packagePath);
        var port = FindAvailablePort();
        var session = new RuntimeSession(applicationId, application.RootPath, port);
        _sessions[applicationId] = session;
        if (!HasViteExecutable(application.RootPath))
        {
            session.Status = "installing";
            session.AddLog("[runtime] first start: npm ci --ignore-scripts --no-audit --no-fund");
            var installExitCode = await RunNpmAsync(application.RootPath, session, ["ci", "--ignore-scripts", "--no-audit", "--no-fund"], cancellationToken);
            if (installExitCode != 0)
            {
                session.Status = "failed";
                session.AddLog($"[runtime] dependency installation failed with code {installExitCode}");
                return ToPayload(session);
            }
        }
        var startInfo = CreateNpmStartInfo(application.RootPath, "run");
        startInfo.ArgumentList.Add("dev");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("0.0.0.0");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("--strictPort");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) => { session.Status = process.ExitCode == 0 ? "stopped" : "failed"; session.AddLog($"[process] exited with code {process.ExitCode}"); };
        session.Process = process;
        if (!process.Start()) throw new InvalidOperationException("Unable to start npm. Ensure Node.js is installed on the server.");
        session.Status = "starting";
        session.AddLog($"[runtime] npm run dev -- --host 0.0.0.0 --port {port} --strictPort");
        _ = PumpAsync(process.StandardOutput, session, "stdout");
        _ = PumpAsync(process.StandardError, session, "stderr");
        return ToPayload(session);
    }

    public Task<object> StopAsync(string applicationId, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(applicationId, out var session))
        {
            if (session.Process is { HasExited: false }) session.Process.Kill(true);
            session.Status = "stopped";
            session.AddLog("[runtime] preview stopped");
            return Task.FromResult<object>(ToPayload(session));
        }
        return Task.FromResult<object>(new { status = "stopped", port = (int?)null, logs = Array.Empty<string>() });
    }

    public Task<object> StatusAsync(string applicationId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(applicationId, out var session))
        {
            if (session.Process?.HasExited == true) session.Status = session.Process.ExitCode == 0 ? "stopped" : "failed";
            return Task.FromResult<object>(ToPayload(session));
        }
        return Task.FromResult<object>(new { status = "stopped", port = (int?)null, logs = Array.Empty<string>() });
    }

    private static void ValidateViteDevScript(string packagePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
        if (!document.RootElement.TryGetProperty("scripts", out var scripts) || !scripts.TryGetProperty("dev", out var dev) || !string.Equals(dev.GetString(), "vite", StringComparison.Ordinal))
            throw new InvalidOperationException("Managed preview requires the template's unchanged \"dev\": \"vite\" script.");
    }

    private static bool HasViteExecutable(string workspacePath)
    {
        var bin = Path.Combine(workspacePath, "node_modules", ".bin");
        return File.Exists(Path.Combine(bin, "vite.cmd")) || File.Exists(Path.Combine(bin, "vite"));
    }

    private static int FindAvailablePort()
    {
        for (var port = 4310; port <= 4399; port++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                return port;
            }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("No free dashboard preview port is available in 4310-4399.");
    }

    private async Task PumpAsync(StreamReader reader, RuntimeSession session, string stream)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                session.AddLog($"[{stream}] {line}");
                if (line.Contains("Local:", StringComparison.OrdinalIgnoreCase) || line.Contains("ready in", StringComparison.OrdinalIgnoreCase)) session.Status = "running";
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Dashboard runtime output stream ended."); }
    }

    private async Task<int> RunNpmAsync(string workingDirectory, RuntimeSession session, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = CreateNpmStartInfo(workingDirectory);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start npm. Ensure Node.js is installed on the server.");
        await Task.WhenAll(process.WaitForExitAsync(cancellationToken), PumpAsync(process.StandardOutput, session, "install"), PumpAsync(process.StandardError, session, "install-error"));
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateNpmStartInfo(string workingDirectory, params string[] initialArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            var nodeDirectory = FindWindowsNodeDirectory();
            startInfo.FileName = Path.Combine(nodeDirectory, "node.exe");
            startInfo.ArgumentList.Add(Path.Combine(nodeDirectory, "node_modules", "npm", "bin", "npm-cli.js"));
        }
        else startInfo.FileName = "npm";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["FORCE_COLOR"] = "0";
        foreach (var argument in initialArguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string FindWindowsNodeDirectory()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs"));
        foreach (var candidate in candidates)
        {
            var node = Path.Combine(candidate, "node.exe");
            var npmCli = Path.Combine(candidate, "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(node) && File.Exists(npmCli)) return candidate;
        }
        throw new InvalidOperationException("Node.js and its npm CLI were not found on the server PATH.");
    }

    private static object ToPayload(RuntimeSession session) => new { status = session.Status, port = session.Port, started_at = session.StartedAt, logs = session.Logs.ToArray() };

    public void Dispose()
    {
        foreach (var session in _sessions.Values) { try { if (session.Process is { HasExited: false }) session.Process.Kill(true); } catch { } }
    }

    private sealed class RuntimeSession
    {
        private readonly ConcurrentQueue<string> _logs = new();
        public RuntimeSession(string applicationId, string rootPath, int port) { ApplicationId = applicationId; RootPath = rootPath; Port = port; StartedAt = DateTime.UtcNow; }
        public string ApplicationId { get; }
        public string RootPath { get; }
        public int Port { get; }
        public DateTime StartedAt { get; }
        public Process? Process { get; set; }
        public string Status { get; set; } = "stopped";
        public IReadOnlyCollection<string> Logs => _logs.ToArray();
        public void AddLog(string value) { _logs.Enqueue($"{DateTime.Now:HH:mm:ss} {value}"); while (_logs.Count > 300 && _logs.TryDequeue(out _)) { } }
    }
}
