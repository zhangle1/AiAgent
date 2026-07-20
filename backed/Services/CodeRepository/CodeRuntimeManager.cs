using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Entities.CodeRepository;
using SqlSugar;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

public interface ICodeRuntimeManager
{
    CodeProjectRuntimeDto GetProjectRuntime(long projectId);
    CodeRuntimeProfileDto SaveProfile(long projectId, long? profileId, CodeRuntimeProfileSaveRequest request);
    void DeleteProfile(long projectId, long profileId);
    Task<List<CodeRuntimeRunDto>> StartAsync(long projectId, CodeRuntimeStartRequest request, CancellationToken cancellationToken);
    bool Stop(long projectId, string runId);
    List<CodeRuntimeLogDto> GetLogs(string runId, long afterSequence);
    CodeRuntimePreviewTarget GetPreviewTarget(string runId);
}

/// <summary>
/// Starts only verified dotnet/npm development targets and retains a bounded live log buffer.
/// The service deliberately never passes user text to a shell.
/// </summary>
public sealed class CodeRuntimeManager : ICodeRuntimeManager, IDisposable
{
    private const int MaxLogLines = 600;
    private readonly ISqlSugarClient _db;
    private readonly ICodeRepositoryManager _repositories;
    private readonly ILogger<CodeRuntimeManager> _logger;
    private readonly ConcurrentDictionary<string, RunningProcess> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _portLock = new(1, 1);

    public CodeRuntimeManager(ISqlSugarClient db, ICodeRepositoryManager repositories, ILogger<CodeRuntimeManager> logger)
    {
        _db = db;
        _repositories = repositories;
        _logger = logger;
    }

    public CodeProjectRuntimeDto GetProjectRuntime(long projectId)
    {
        _ = _repositories.GetProject(projectId);
        var repositories = _repositories.List().ToDictionary(item => item.Id);
        return new CodeProjectRuntimeDto
        {
            ProjectId = projectId,
            Profiles = _db.Queryable<AiCodeRepositoryRunProfile>()
                .Where(item => item.ProjectId == projectId)
                .OrderBy(item => new { item.Role, item.Id })
                .ToList()
                .Select(item => ToProfileDto(item, repositories.GetValueOrDefault(item.RepositoryId)))
                .ToList(),
            Runs = _runs.Values
                .Where(item => item.ProjectId == projectId)
                .OrderByDescending(item => item.StartedAt)
                .Select(ToRunDto)
                .ToList()
        };
    }

    public CodeRuntimeProfileDto SaveProfile(long projectId, long? profileId, CodeRuntimeProfileSaveRequest request)
    {
        _ = _repositories.GetProject(projectId);
        var repository = _repositories.Get(request.RepositoryName);
        if (repository.ProjectId != projectId)
            throw new InvalidOperationException("The selected repository does not belong to this project.");

        var role = NormalizeRole(request.Role);
        var entryPath = NormalizeRelativePath(request.EntryPath);
        var runScript = NormalizeRunScript(request.RunScript, role);
        var testScript = NormalizeTestScript(request.TestScript, role);
        var preferredPort = NormalizePreferredPort(request.PreferredPort);
        ValidateProfileTarget(repository, role, entryPath);
        var healthPath = NormalizeHealthPath(request.HealthPath);
        var now = DateTime.UtcNow;

        AiCodeRepositoryRunProfile profile;
        if (profileId.HasValue)
        {
            profile = _db.Queryable<AiCodeRepositoryRunProfile>()
                .First(item => item.Id == profileId.Value && item.ProjectId == projectId)
                ?? throw new KeyNotFoundException("Runtime profile was not found.");
            profile.RepositoryId = repository.Id;
            profile.Role = role;
            profile.EntryPath = entryPath;
            profile.RunScript = runScript;
            profile.TestScript = testScript;
            profile.PreferredPort = preferredPort;
            profile.HealthPath = healthPath;
            profile.IsEnabled = request.IsEnabled;
            profile.IsPreviewEnabled = request.IsPreviewEnabled;
            profile.UpdatedAt = now;
            _db.Updateable(profile).ExecuteCommand();
        }
        else
        {
            if (_db.Queryable<AiCodeRepositoryRunProfile>().Any(item => item.ProjectId == projectId && item.Role == role && item.RepositoryId == repository.Id))
                throw new InvalidOperationException("A runtime profile already exists for this repository and role.");
            profile = new AiCodeRepositoryRunProfile
            {
                ProjectId = projectId,
                RepositoryId = repository.Id,
                Role = role,
                EntryPath = entryPath,
                RunScript = runScript,
                TestScript = testScript,
                PreferredPort = preferredPort,
                HealthPath = healthPath,
                IsEnabled = request.IsEnabled,
                IsPreviewEnabled = request.IsPreviewEnabled,
                CreatedAt = now
            };
            _db.Insertable(profile).ExecuteCommand();
        }

        return ToProfileDto(profile, repository);
    }

    public void DeleteProfile(long projectId, long profileId)
    {
        var profile = _db.Queryable<AiCodeRepositoryRunProfile>()
            .First(item => item.Id == profileId && item.ProjectId == projectId)
            ?? throw new KeyNotFoundException("Runtime profile was not found.");
        if (_runs.Values.Any(item => item.ProfileId == profile.Id && item.IsActive))
            throw new InvalidOperationException("Stop the running program before deleting its runtime profile.");
        _db.Deleteable<AiCodeRepositoryRunProfile>().In(profile.Id).ExecuteCommand();
    }

    public async Task<List<CodeRuntimeRunDto>> StartAsync(long projectId, CodeRuntimeStartRequest request, CancellationToken cancellationToken)
    {
        _ = _repositories.GetProject(projectId);
        EnsureSuggestedProfiles(projectId);
        var requestedProfileIds = request.ProfileIds?.Distinct().Take(4).ToList() ?? [];
        var profiles = _db.Queryable<AiCodeRepositoryRunProfile>()
            .Where(item => item.ProjectId == projectId && item.IsEnabled)
            .ToList()
            .Where(item => requestedProfileIds.Count == 0 || requestedProfileIds.Contains(item.Id))
            .ToList();
        if (profiles.Count == 0)
            throw new InvalidOperationException("Configure at least one enabled front-end or back-end runtime profile first.");

        var started = new List<CodeRuntimeRunDto>();
        try
        {
            foreach (var profile in profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_runs.Values.Any(item => item.ProjectId == projectId && item.Role == profile.Role && item.IsActive))
                    throw new InvalidOperationException($"This project already has a running {profile.Role} process.");
                started.Add(await StartProfileAsync(profile, cancellationToken));
            }
            return started;
        }
        catch
        {
            foreach (var run in started) Stop(projectId, run.RunId);
            throw;
        }
    }

    public bool Stop(long projectId, string runId)
    {
        if (!_runs.TryGetValue(runId, out var run) || run.ProjectId != projectId) return false;
        lock (run.Sync)
        {
            if (!run.IsActive) return false;
            run.StoppedByUser = true;
            run.Status = "stopping";
            AppendLog(run, "system", "Stop requested.");
            try
            {
                if (!run.Process.HasExited) run.Process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // The process ended in the short interval after its state check.
            }
            return true;
        }
    }

    public void Dispose()
    {
        foreach (var run in _runs.Values)
        {
            try
            {
                if (!run.Process.HasExited) run.Process.Kill(true);
                run.Process.Dispose();
            }
            catch
            {
                // Shutdown must continue even if a child process has already exited.
            }
        }
        _portLock.Dispose();
    }

    public List<CodeRuntimeLogDto> GetLogs(string runId, long afterSequence)
    {
        if (!_runs.TryGetValue(runId, out var run)) throw new KeyNotFoundException("Runtime process was not found.");
        lock (run.Sync)
            return run.Logs.Where(item => item.Sequence > Math.Max(0, afterSequence)).ToList();
    }

    public CodeRuntimePreviewTarget GetPreviewTarget(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run) || run.Role != "frontend" || !run.IsActive)
            throw new KeyNotFoundException("A running front-end preview was not found.");
        var profile = _db.Queryable<AiCodeRepositoryRunProfile>().First(item => item.Id == run.ProfileId);
        if (profile is null || !profile.IsPreviewEnabled)
            throw new InvalidOperationException("Preview is disabled for this runtime profile.");
        return new CodeRuntimePreviewTarget(run.RunId, run.Port);
    }

    private async Task<CodeRuntimeRunDto> StartProfileAsync(AiCodeRepositoryRunProfile profile, CancellationToken cancellationToken)
    {
        var repository = _repositories.List().FirstOrDefault(item => item.Id == profile.RepositoryId)
            ?? throw new InvalidOperationException("The runtime profile repository is no longer available.");
        var port = await AllocatePortAsync(profile.Role, profile.PreferredPort, cancellationToken);
        var run = new RunningProcess
        {
            RunId = Guid.NewGuid().ToString("N"),
            ProjectId = profile.ProjectId,
            ProfileId = profile.Id,
            RepositoryId = repository.Id,
            RepositoryName = repository.Name,
            Role = profile.Role,
            Port = port,
            AccessUrls = GetAccessUrls(port),
            IsPreviewEnabled = profile.IsPreviewEnabled,
            StartedAt = DateTime.UtcNow,
            Status = "starting"
        };

        try
        {
            var startInfo = BuildStartInfo(repository, profile, port);
            run.Command = $"{startInfo.FileName} {string.Join(' ', startInfo.ArgumentList)}";
            run.Process = new Process { StartInfo = startInfo };
            if (!run.Process.Start()) throw new InvalidOperationException("The server process could not be started.");
            run.Status = "running";
            _runs[run.RunId] = run;
            AppendLog(run, "system", $"Started {profile.Role}. Available at: {string.Join(", ", run.AccessUrls)}.");
            _ = ObserveProcessAsync(run);
            return ToRunDto(run);
        }
        catch
        {
            run.Process?.Dispose();
            throw;
        }
    }

    private void EnsureSuggestedProfiles(long projectId)
    {
        var existing = _db.Queryable<AiCodeRepositoryRunProfile>()
            .Where(item => item.ProjectId == projectId)
            .ToList()
            .Select(item => (item.RepositoryId, item.Role))
            .ToHashSet();
        var now = DateTime.UtcNow;
        var suggestions = new List<AiCodeRepositoryRunProfile>();
        foreach (var repository in _repositories.List().Where(item => item.ProjectId == projectId))
        {
            var isFrontend = repository.Languages.Contains("TypeScript/JavaScript", StringComparer.OrdinalIgnoreCase)
                && repository.ConfigurationFiles.Any(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase));
            var frontendTarget = repository.PublishTarget?.EndsWith("package.json", StringComparison.OrdinalIgnoreCase) == true
                ? repository.PublishTarget
                : repository.ConfigurationFiles.FirstOrDefault(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase));
            if (isFrontend && !existing.Contains((repository.Id, "frontend")) && !string.IsNullOrWhiteSpace(frontendTarget))
            {
                suggestions.Add(new AiCodeRepositoryRunProfile
                {
                    ProjectId = projectId,
                    RepositoryId = repository.Id,
                    Role = "frontend",
                    EntryPath = frontendTarget,
                    RunScript = ResolveFrontendRunScript(repository, frontendTarget),
                    TestScript = "test",
                    PreferredPort = 4300,
                    IsEnabled = true,
                    IsPreviewEnabled = true,
                    CreatedAt = now
                });
            }

            var backendTarget = ResolveBackendProjectFile(repository);
            if (repository.BuildSystems.Contains("dotnet", StringComparer.OrdinalIgnoreCase) && !existing.Contains((repository.Id, "backend")) && !string.IsNullOrWhiteSpace(backendTarget))
            {
                suggestions.Add(new AiCodeRepositoryRunProfile
                {
                    ProjectId = projectId,
                    RepositoryId = repository.Id,
                    Role = "backend",
                    EntryPath = backendTarget,
                    TestScript = "dotnet test",
                    PreferredPort = 5100,
                    IsEnabled = true,
                    IsPreviewEnabled = false,
                    CreatedAt = now
                });
            }
        }
        if (suggestions.Count > 0) _db.Insertable(suggestions).ExecuteCommand();
    }

    private ProcessStartInfo BuildStartInfo(CodeRepositoryDto repository, AiCodeRepositoryRunProfile profile, int port)
    {
        var rootPath = Path.GetFullPath(repository.RootPath);
        var entryPath = ResolveEntryPath(repository, profile);
        var fullEntryPath = Path.GetFullPath(Path.Combine(rootPath, entryPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithin(rootPath, fullEntryPath) || !File.Exists(fullEntryPath))
            throw new FileNotFoundException("The saved runtime entry file no longer exists.");

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        if (profile.Role == "frontend")
        {
            startInfo.FileName = ResolveNpmExecutable();
            startInfo.WorkingDirectory = Path.GetDirectoryName(fullEntryPath)!;
            var runScript = !string.IsNullOrWhiteSpace(profile.RunScript) && PackageHasScript(fullEntryPath, profile.RunScript)
                ? profile.RunScript
                : ResolveFrontendRunScript(repository, entryPath);
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(runScript);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--host");
            startInfo.ArgumentList.Add("0.0.0.0");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port.ToString());
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.WorkingDirectory = rootPath;
            // Keep the VS development configuration while retaining AiAgent's managed port.
            // launchSettings.json would otherwise override --urls with its own localhost binding.
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(fullEntryPath);
            startInfo.ArgumentList.Add("--no-launch-profile");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add($"http://0.0.0.0:{port}");
        }
        return startInfo;
    }

    private async Task ObserveProcessAsync(RunningProcess run)
    {
        try
        {
            var stdout = PumpAsync(run, run.Process.StandardOutput, "stdout");
            var stderr = PumpAsync(run, run.Process.StandardError, "stderr");
            await Task.WhenAll(run.Process.WaitForExitAsync(), stdout, stderr);
            lock (run.Sync)
            {
                run.ExitCode = run.Process.ExitCode;
                run.CompletedAt = DateTime.UtcNow;
                run.Status = run.StoppedByUser ? "stopped" : run.ExitCode == 0 ? "exited" : "failed";
                AppendLog(run, "system", $"Process {run.Status} with exit code {run.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            lock (run.Sync)
            {
                run.CompletedAt = DateTime.UtcNow;
                run.Status = "failed";
                AppendLog(run, "system", $"Runtime observer failed: {ex.Message}");
            }
            _logger.LogWarning(ex, "Runtime observer failed for {RunId}", run.RunId);
        }
    }

    private static async Task PumpAsync(RunningProcess run, StreamReader reader, string stream)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            lock (run.Sync) AppendLog(run, stream, line);
        }
    }

    private async Task<int> AllocatePortAsync(string role, int? preferredPort, CancellationToken cancellationToken)
    {
        var (first, last) = role == "frontend" ? (4300, 4399) : (5100, 5199);
        await _portLock.WaitAsync(cancellationToken);
        try
        {
            var used = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Select(endpoint => endpoint.Port)
                .ToHashSet();
            used.UnionWith(_runs.Values.Where(item => item.IsActive).Select(item => item.Port));
            var candidatePorts = preferredPort.HasValue
                ? new[] { preferredPort.Value }.Concat(Enumerable.Range(first, last - first + 1).Where(port => port != preferredPort.Value))
                : Enumerable.Range(first, last - first + 1);
            foreach (var port in candidatePorts)
            {
                if (used.Contains(port)) continue;
                using var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                    return port;
                }
                catch (SocketException)
                {
                    // Another process won the port between the listener query and bind.
                }
            }
        }
        finally
        {
            _portLock.Release();
        }
        throw new InvalidOperationException($"No free {role} development ports are available.");
    }

    private static void AppendLog(RunningProcess run, string stream, string line)
    {
        run.NextLogSequence++;
        run.Logs.Add(new CodeRuntimeLogDto { Sequence = run.NextLogSequence, Stream = stream, Line = line, CreatedAt = DateTime.UtcNow });
        if (run.Logs.Count > MaxLogLines) run.Logs.RemoveRange(0, run.Logs.Count - MaxLogLines);
    }

    private static string ResolveEntryPath(CodeRepositoryDto repository, AiCodeRepositoryRunProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.EntryPath)) return profile.EntryPath;
        if (!string.IsNullOrWhiteSpace(repository.PublishTarget)) return repository.PublishTarget;
        throw new InvalidOperationException("Select and save a runtime entry file before starting this program.");
    }

    private static void ValidateProfileTarget(CodeRepositoryDto repository, string role, string? entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) throw new InvalidOperationException("Select a runtime entry file.");
        if (role == "frontend")
        {
            if (!repository.ConfigurationFiles.Contains(entryPath, StringComparer.OrdinalIgnoreCase) || !Path.GetFileName(entryPath).Equals("package.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A front-end runtime must use a selected package.json file.");
            return;
        }
        if (!entryPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || (!repository.SolutionFiles.Contains(entryPath, StringComparer.OrdinalIgnoreCase)
                && !repository.SolutionFiles.Any(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("A back-end runtime must use a selected .csproj file.");
    }

    private static string? ResolveBackendProjectFile(CodeRepositoryDto repository)
    {
        if (repository.PublishTarget?.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) == true) return repository.PublishTarget;
        var selectedProject = repository.SolutionFiles.FirstOrDefault(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(selectedProject)) return selectedProject;

        var solution = repository.SolutionFiles.FirstOrDefault(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(solution)) return null;
        var rootPath = Path.GetFullPath(repository.RootPath);
        var solutionPath = Path.GetFullPath(Path.Combine(rootPath, solution.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithin(rootPath, solutionPath) || !File.Exists(solutionPath)) return null;

        foreach (var line in File.ReadLines(solutionPath))
        {
            var projectPart = line.Split(',').Select(value => value.Trim().Trim('"')).FirstOrDefault(value => value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(projectPart)) continue;
            var projectPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, projectPart));
            if (IsPathWithin(rootPath, projectPath) && File.Exists(projectPath))
                return Path.GetRelativePath(rootPath, projectPath).Replace('\\', '/');
        }
        return null;
    }

    private static string ResolveFrontendRunScript(CodeRepositoryDto repository, string packagePath)
    {
        try
        {
            var rootPath = Path.GetFullPath(repository.RootPath);
            var fullPath = Path.GetFullPath(Path.Combine(rootPath, packagePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsPathWithin(rootPath, fullPath) || !File.Exists(fullPath)) return "dev";
            using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
            if (!document.RootElement.TryGetProperty("scripts", out var scripts)) return "dev";
            if (scripts.TryGetProperty("dev", out _)) return "dev";
            if (scripts.TryGetProperty("start", out _)) return "start";
            if (scripts.TryGetProperty("serve", out _)) return "serve";
        }
        catch (JsonException)
        {
            // The start command will surface a clear npm error if package.json is malformed.
        }
        return "dev";
    }

    private static bool PackageHasScript(string packagePath, string script)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
            return document.RootElement.TryGetProperty("scripts", out var scripts) && scripts.TryGetProperty(script, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<string> GetAccessUrls(int port)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1" };
        foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                     .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                     .Select(item => item.Address)
                     .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)))
        {
            hosts.Add(address.ToString());
        }
        return hosts.OrderBy(host => host == "127.0.0.1" ? $"0-{host}" : $"1-{host}", StringComparer.OrdinalIgnoreCase)
            .Select(host => $"http://{host}:{port}")
            .ToList();
    }

    private static string NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "frontend" => "frontend",
        "backend" => "backend",
        _ => throw new ArgumentException("Runtime role must be frontend or backend.")
    };

    private static string? NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Replace('\\', '/').TrimStart('/').Trim();
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(path)) throw new ArgumentException("Runtime entry must be a repository-relative file.");
        return path;
    }

    private static string? NormalizeRunScript(string? value, string role)
    {
        if (role == "backend") return null;
        var script = string.IsNullOrWhiteSpace(value) ? "dev" : value.Trim();
        if (script.Length > 80 || script.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or ':')))
            throw new ArgumentException("The npm run script contains unsupported characters.");
        return script;
    }

    private static string? NormalizeTestScript(string? value, string role)
    {
        var script = string.IsNullOrWhiteSpace(value) ? (role == "frontend" ? "test" : "dotnet test") : value.Trim();
        if (script.Length > 128 || script.Any(char.IsControl))
            throw new ArgumentException("The test command is invalid.");
        if (role == "frontend" && script.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or ':')))
            throw new ArgumentException("The npm test script contains unsupported characters.");
        if (role == "backend" && !script.StartsWith("dotnet test", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The C# test command must start with 'dotnet test'.");
        return script;
    }

    private static int? NormalizePreferredPort(int? value)
    {
        if (!value.HasValue) return null;
        if (value.Value is < 1024 or > 65535)
            throw new ArgumentException("The preferred development port must be between 1024 and 65535.");
        return value.Value;
    }

    private static string? NormalizeHealthPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        if (!path.StartsWith('/') || path.Length > 256 || path.Contains("//", StringComparison.Ordinal)) throw new ArgumentException("Health path must start with '/'.");
        return path;
    }

    private static string ResolveNpmExecutable()
    {
        if (!OperatingSystem.IsWindows()) return "npm";
        foreach (var pathEntry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var npmPath = Path.Combine(pathEntry.Trim('"'), "npm.cmd");
            if (File.Exists(npmPath)) return npmPath;
        }
        throw new InvalidOperationException("Unable to locate npm.cmd. Install Node.js and include it in the server PATH.");
    }

    private static bool IsPathWithin(string parentPath, string childPath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static CodeRuntimeProfileDto ToProfileDto(AiCodeRepositoryRunProfile profile, CodeRepositoryDto? repository = null) => new()
    {
        Id = profile.Id,
        ProjectId = profile.ProjectId,
        RepositoryId = profile.RepositoryId,
        RepositoryName = repository?.Name,
        Role = profile.Role,
        EntryPath = profile.EntryPath,
        RunScript = profile.RunScript,
        TestScript = profile.TestScript,
        PreferredPort = profile.PreferredPort,
        HealthPath = profile.HealthPath,
        IsEnabled = profile.IsEnabled,
        IsPreviewEnabled = profile.IsPreviewEnabled,
        CreatedAt = profile.CreatedAt,
        UpdatedAt = profile.UpdatedAt
    };

    private static CodeRuntimeRunDto ToRunDto(RunningProcess run) => new()
    {
        RunId = run.RunId,
        ProjectId = run.ProjectId,
        ProfileId = run.ProfileId,
        RepositoryId = run.RepositoryId,
        RepositoryName = run.RepositoryName,
        Role = run.Role,
        Status = run.Status,
        Port = run.Port,
        AccessUrls = run.AccessUrls,
        PreviewUrl = run.Role == "frontend" && run.IsPreviewEnabled ? $"/api/v1/code-runtime/runs/{run.RunId}/preview/" : null,
        Command = run.Command,
        ExitCode = run.ExitCode,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt
    };

    private sealed class RunningProcess
    {
        public object Sync { get; } = new();
        public required string RunId { get; init; }
        public long ProjectId { get; init; }
        public long ProfileId { get; init; }
        public long RepositoryId { get; init; }
        public required string RepositoryName { get; init; }
        public required string Role { get; init; }
        public Process Process { get; set; } = null!;
        public int Port { get; init; }
        public List<string> AccessUrls { get; init; } = [];
        public bool IsPreviewEnabled { get; init; }
        public required DateTime StartedAt { get; init; }
        public DateTime? CompletedAt { get; set; }
        public string Status { get; set; } = "starting";
        public string? Command { get; set; }
        public int? ExitCode { get; set; }
        public bool StoppedByUser { get; set; }
        public long NextLogSequence { get; set; }
        public List<CodeRuntimeLogDto> Logs { get; } = [];
        public bool IsActive => Status is "starting" or "running" or "stopping";
    }
}

public sealed record CodeRuntimePreviewTarget(string RunId, int Port);
