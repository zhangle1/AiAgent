using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.PythonWorkers;

/// <summary>
/// Python worker 进程调用入口。
/// </summary>
public interface IPythonWorkerHost
{
    /// <summary>
    /// 调用指定 worker，并返回 stdout 文本。
    /// </summary>
    Task<string> InvokeAsync(string workerName, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// 调用指定 worker，并在读取到 worker 事件时即时回调。
    /// </summary>
    Task<string> InvokeAsync(string workerName, object payload, Func<PythonWorkerEvent, Task>? onEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析指定 worker 的 Python 解释器路径。
    /// </summary>
    string ResolvePythonPath(string workerName);

    /// <summary>
    /// 解析指定 worker 的脚本路径。
    /// </summary>
    string ResolveWorkerPath(string workerName);

    /// <summary>
    /// 确认路径位于 Python worker 允许访问的目录内。
    /// </summary>
    void EnsureAllowedPath(string path);
}

/// <summary>
/// 基于独立 Python 进程、stdin/stdout JSON 协议的 worker host。
/// </summary>
public sealed class PythonWorkerHost : IPythonWorkerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<PythonWorkerHost> _logger;

    /// <summary>
    /// 初始化 Python worker host。
    /// </summary>
    public PythonWorkerHost(IConfiguration configuration, ILogger<PythonWorkerHost> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 调用指定 worker，并返回 stdout 文本。
    /// </summary>
    public async Task<string> InvokeAsync(string workerName, object payload, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(workerName, payload, null, cancellationToken);
    }

    /// <summary>
    /// 调用指定 worker，并在读取到 worker 事件时即时回调。
    /// </summary>
    public async Task<string> InvokeAsync(string workerName, object payload, Func<PythonWorkerEvent, Task>? onEvent, CancellationToken cancellationToken = default)
    {
        var workerPath = ResolveWorkerPath(workerName);
        var pythonPath = ResolvePythonPath(workerName);
        var timeoutSeconds = Math.Max(10, _configuration.GetValue("PythonWorkers:TimeoutSeconds", 120));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"\"{workerPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = startInfo };
        var input = JsonSerializer.Serialize(payload, JsonOptions);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start Python worker '{workerName}'.");
        }

        try
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();

            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdout = new StringBuilder();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token);
                if (line is null)
                {
                    break;
                }

                if (await TryHandleWorkerEventAsync(line, onEvent))
                {
                    continue;
                }

                if (IsJsonObjectLine(line))
                {
                    stdout.Clear();
                }

                stdout.AppendLine(line);
            }

            await process.WaitForExitAsync(timeoutCts.Token);
            var stdoutText = stdout.ToString().Trim();
            var stderr = await stderrTask;

            if (string.IsNullOrWhiteSpace(stdoutText))
            {
                _logger.LogError("Python worker {WorkerName} returned no stdout. ExitCode={ExitCode}, stderr={Stderr}", workerName, process.ExitCode, stderr);
                throw new InvalidOperationException($"Python worker '{workerName}' returned no output. {stderr}");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Python worker {WorkerName} exited with {ExitCode}: {Stdout} {Stderr}", workerName, process.ExitCode, stdoutText, stderr);
                throw new PythonWorkerException(workerName, stdoutText, stderr, process.ExitCode);
            }

            return stdoutText;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw new TimeoutException($"Python worker '{workerName}' timed out after {timeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    private async Task<bool> TryHandleWorkerEventAsync(string line, Func<PythonWorkerEvent, Task>? onEvent)
    {
        if (onEvent is null || string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString();
            if (!string.Equals(type, "progress", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await onEvent(new PythonWorkerEvent
            {
                Type = type ?? "progress",
                Stage = GetString(root, "stage"),
                Progress = GetInt(root, "progress"),
                Message = GetString(root, "message"),
                RawJson = line
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python worker progress event callback failed.");
            return true;
        }
    }

    /// <summary>
    /// 解析指定 worker 的 Python 解释器路径。
    /// </summary>
    public string ResolvePythonPath(string workerName)
    {
        var configured = GetWorkerSetting(workerName, "PythonPath") ?? GetLegacyPythonPath(workerName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return "python";
        }

        if (Path.IsPathRooted(configured) || configured.Equals("python", StringComparison.OrdinalIgnoreCase) || configured.Equals("py", StringComparison.OrdinalIgnoreCase))
        {
            return configured;
        }

        var candidates = BuildPathCandidates(configured);
        return candidates.FirstOrDefault(File.Exists) ?? configured;
    }

    /// <summary>
    /// 解析指定 worker 的脚本路径。
    /// </summary>
    public string ResolveWorkerPath(string workerName)
    {
        var configured = GetWorkerSetting(workerName, "WorkerPath") ?? GetLegacyWorkerPath(workerName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new FileNotFoundException($"未配置 PythonWorkers:{workerName}:WorkerPath。");
        }

        var candidates = BuildPathCandidates(configured);
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            throw new FileNotFoundException($"找不到 Python worker 脚本：{configured}", candidates[0]);
        }

        return found;
    }

    /// <summary>
    /// 确认路径位于 Python worker 允许访问的目录内。
    /// </summary>
    public void EnsureAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var target = Path.GetFullPath(path);
        var allowedRoots = _configuration.GetSection("PythonWorkers:AllowedRoots").Get<string[]>() ?? ["data"];
        var resolvedRoots = allowedRoots
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(BuildPathCandidates)
            .Select(Path.GetFullPath)
            .ToList();

        if (resolvedRoots.Count == 0)
        {
            throw new InvalidOperationException("PythonWorkers:AllowedRoots is empty.");
        }

        var allowed = resolvedRoots.Any(root => IsPathInsideRoot(target, root));
        if (!allowed)
        {
            throw new UnauthorizedAccessException($"Path is outside Python worker allowed roots: {target}");
        }
    }

    private string? GetWorkerSetting(string workerName, string settingName)
    {
        var normalized = NormalizeWorkerName(workerName);
        return _configuration[$"PythonWorkers:{normalized}:{settingName}"];
    }

    private string? GetLegacyPythonPath(string workerName)
    {
        return NormalizeWorkerName(workerName) == "Rag" ? _configuration["Rag:PythonPath"] : null;
    }

    private string? GetLegacyWorkerPath(string workerName)
    {
        return NormalizeWorkerName(workerName) == "Rag" ? _configuration["Rag:LlamaIndexWorkerPath"] : null;
    }

    private string[] BuildPathCandidates(string configured)
    {
        return Path.IsPathRooted(configured)
            ? [configured]
            :
            [
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured)),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured))
            ];
    }

    private static string NormalizeWorkerName(string workerName)
    {
        return workerName.Trim().ToLowerInvariant() switch
        {
            "rag" => "Rag",
            "parsing" => "Parsing",
            "ocr" => "Ocr",
            _ => workerName.Trim()
        };
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static bool IsJsonObjectLine(string line)
    {
        var value = line.TrimStart();
        return value.StartsWith('{');
    }
}

/// <summary>
/// Python worker 输出的流式事件。
/// </summary>
public sealed class PythonWorkerEvent
{
    /// <summary>
    /// 事件类型。
    /// </summary>
    public string Type { get; set; } = "progress";

    /// <summary>
    /// 当前阶段。
    /// </summary>
    public string? Stage { get; set; }

    /// <summary>
    /// 当前进度百分比。
    /// </summary>
    public int? Progress { get; set; }

    /// <summary>
    /// 面向用户的阶段说明。
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 原始事件 JSON，供日志排查使用。
    /// </summary>
    public string RawJson { get; set; } = string.Empty;
}

/// <summary>
/// Python worker 非零退出异常，保留 stdout/stderr 供上层解析 JSON 错误。
/// </summary>
public sealed class PythonWorkerException : Exception
{
    /// <summary>
    /// Worker 名称。
    /// </summary>
    public string WorkerName { get; }

    /// <summary>
    /// Worker 标准输出。
    /// </summary>
    public string Stdout { get; }

    /// <summary>
    /// Worker 标准错误。
    /// </summary>
    public string Stderr { get; }

    /// <summary>
    /// Worker 退出码。
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// 初始化 Python worker 异常。
    /// </summary>
    public PythonWorkerException(string workerName, string stdout, string stderr, int exitCode)
        : base($"Python worker '{workerName}' exited with {exitCode}.")
    {
        WorkerName = workerName;
        Stdout = stdout;
        Stderr = stderr;
        ExitCode = exitCode;
    }
}
