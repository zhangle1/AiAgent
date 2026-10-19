using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

/// <summary>Only generated facts and classified reasons leave the process; never raw CLI output or exception messages.</summary>
internal sealed class CodexRequestDiagnostics
{
    private readonly ILogger _logger;
    private readonly AgentStreamEventHandler? _onEvent;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, string> _facts = [];
    private readonly Dictionary<string, string> _serverFacts = [];
    private readonly HashSet<string> _notices = [];
    private int _stdoutLines;
    public string Id { get; }
    public string Stage { get; private set; } = "codex_request";
    public string Transport { get; private set; } = "codex_exec";

    public CodexRequestDiagnostics(ChatCompleteRequest request, ILogger logger, AgentStreamEventHandler? onEvent)
    {
        Id = CodexDiagnosticException.EnsureId(request);
        _logger = logger;
        _onEvent = onEvent;
    }

    public void Model(CodexResolvedModel model, string sandbox)
    {
        Transport = model.IsBuiltin ? "codex_app_server" : "codex_exec";
        _facts["model"] = SafeIdentifier(model.Id);
        _facts["profile"] = SafeIdentifier(model.ProfileName);
        _facts["model_override"] = SafeIdentifier(model.AppServerModelId);
        _facts["reasoning"] = SafeIdentifier(model.ReasoningEffort);
        _facts["sandbox"] = sandbox;
    }

    public void Runtime(string command, string workspace, string? profile, int images)
    {
        var customHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var home = string.IsNullOrWhiteSpace(customHome) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : customHome;
        _facts["config_home_source"] = string.IsNullOrWhiteSpace(customHome) ? "user_profile" : "CODEX_HOME";
        _facts["base_config_exists"] = File.Exists(Path.Combine(home, "config.toml")).ToString();
        _facts["profile_config_exists"] = (!string.IsNullOrWhiteSpace(profile) && File.Exists(Path.Combine(home, profile + ".config.toml"))).ToString();
        _facts["image_count"] = images.ToString();
        // These paths only enter the server log, never a browser event or the trace database.
        _serverFacts["cli_path"] = File.Exists(command) ? command : command is "codex" or "codex.exe" or "codex.cmd" ? command : "unresolved";
        _serverFacts["config_home"] = Directory.Exists(home) ? home : "not_found";
        _serverFacts["workspace"] = Directory.Exists(workspace) ? workspace : "not_found";
        _serverFacts["os_user"] = Environment.UserName;
        _serverFacts["machine"] = Environment.MachineName;
    }

    public void Fact(string key, int value) => _facts[key] = value.ToString();
    public void Resume(bool selected) => _facts["resume_selected"] = selected.ToString();

    public async Task StepAsync(string stage, string status = "started", CancellationToken ct = default)
    {
        Stage = stage;
        await PublishAsync(stage, status, null, null, ct);
    }

    public async Task ObserveCliLineAsync(string line, CancellationToken ct)
    {
        _stdoutLines++;
        if (_stdoutLines == 1) await StepAsync("codex_cli_output", "completed", ct);
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var type) && type.GetString() == "error")
            {
                var raw = item.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null;
                var code = CodexDiagnosticException.ClassifyCli(raw) ?? "codex_cli_notice";
                // CLI error items can be warnings followed by turn.completed. They do not determine terminal status.
                if (_notices.Count < 4 && _notices.Add(code)) await PublishAsync("codex_cli_notice", "warning", code, CodexDiagnosticException.Describe(code), ct);
            }
        }
        catch (JsonException) { /* Line contents may contain credentials or user data; do not record them. */ }
    }

    public async Task<CodexDiagnosticException> FailAsync(Exception exception)
    {
        var failure = exception as CodexDiagnosticException ?? CodexDiagnosticException.Create(exception, Id, Stage);
        _facts["exception_type"] = exception.GetType().Name;
        _facts["exception_hresult"] = exception.HResult.ToString();
        var native = FindNativeError(exception);
        if (native.HasValue) _facts["native_error"] = native.Value.ToString();
        _facts["stdout_lines"] = _stdoutLines.ToString();
        // Method names give a useful failure location without serializing exception messages or SQL/HTTP payloads.
        _serverFacts["exception_methods"] = string.Join(" > ", new StackTrace(exception, false).GetFrames().Take(8)
            .Select(frame => frame.GetMethod()).Where(method => method != null).Select(method => method!.DeclaringType?.FullName + "." + method.Name));
        try { await PublishAsync(failure.Stage, "failed", failure.Code, failure.Reason, CancellationToken.None); }
        catch { _logger.LogWarning("[CodexDiagnostic] Id={DiagnosticId} Failure event could not be delivered.", Id); }
        return failure;
    }

    private async Task PublishAsync(string stage, string status, string? code, string? message, CancellationToken ct)
    {
        var level = status == "failed" ? LogLevel.Error : status == "warning" ? LogLevel.Warning : LogLevel.Information;
        _logger.Log(level, "[CodexDiagnostic] Id={DiagnosticId} Stage={Stage} Status={Status} Code={Code} Facts={Facts} Runtime={Runtime}",
            Id, stage, status, code, JsonSerializer.Serialize(_facts), JsonSerializer.Serialize(_serverFacts));
        if (_onEvent != null)
            await _onEvent(new AgentStreamEvent {
                Type = "debug_trace", DebugTrace = new ChatDebugTraceEvent {
                    TraceId = Id, DiagnosticId = Id, Stage = stage, Status = status, ElapsedMs = _clock.ElapsedMilliseconds,
                    Provider = "codex", Transport = Transport, ErrorCode = code, Message = message,
                    Details = new Dictionary<string, string>(_facts)
                }
            }, ct);
    }

    internal static string SafeIdentifier(string? value) => string.IsNullOrWhiteSpace(value) ? "profile_default"
        : value.Length <= 160 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/')
            && !value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("eyJ", StringComparison.Ordinal)
            ? value : "configured";

    private static int? FindNativeError(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is Win32Exception native) return native.NativeErrorCode;
        return null;
    }

    internal static async Task<CodexStderrSummary> ReadStderrAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[1024];
        var tail = new StringBuilder();
        long characters = 0;
        string? code = null;
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), ct);
                if (count == 0) break;
                characters += count;
                tail.Append(buffer, 0, count);
                if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
                var classified = CodexDiagnosticException.ClassifyCli(tail.ToString());
                if (classified is not null and not "codex_model_metadata") code = classified;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        return new(characters, code, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tail.ToString())))[..16]);
    }
}

internal sealed record CodexStderrSummary(long Characters, string? Code, string Fingerprint);

public sealed class CodexDiagnosticException : InvalidOperationException
{
    public string DiagnosticId { get; }
    public string Stage { get; }
    public string Code { get; }
    public string Reason { get; }

    private CodexDiagnosticException(string id, string stage, string code)
        : base($"Codex 执行失败：{Describe(code)} 阶段：{stage}；错误码：{code}；诊断编号：{id}")
    { DiagnosticId = id; Stage = stage; Code = code; Reason = Describe(code); }

    internal static string EnsureId(ChatCompleteRequest request) => request.CodexDiagnosticId ??= Guid.NewGuid().ToString("N");

    public static CodexDiagnosticException ForRequest(Exception error, ChatCompleteRequest request) =>
        error as CodexDiagnosticException ?? Create(error, EnsureId(request), "codex_request_context");

    internal static CodexDiagnosticException Cli(string id, string code) => new(id, "codex_cli_exit", code);

    internal static CodexDiagnosticException Create(Exception exception, string id, string stage)
    {
        var inner = exception.GetBaseException();
        var code = inner switch {
            DirectoryNotFoundException => "codex_workspace_missing",
            UnauthorizedAccessException => "codex_access_denied",
            FileNotFoundException => "codex_cli_missing",
            Win32Exception win32 when win32.NativeErrorCode is 2 or 3 => "codex_cli_missing",
            Win32Exception win32 when win32.NativeErrorCode == 5 => "codex_access_denied",
            Win32Exception => "codex_process_start",
            JsonException => "codex_session_metadata",
            OperationCanceledException => "codex_cancelled",
            _ => ClassifyCli(inner.Message) ?? (stage == "codex_model_resolution" ? "codex_model_policy" : stage == "codex_session_acquire" ? "codex_session_busy" : "codex_runtime_failed")
        };
        return new(id, stage, code);
    }

    internal static string? ClassifyCli(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.ToLowerInvariant();
        if (text.Contains("unexpected argument") || text.Contains("unrecognized option") || text.Contains("unknown option")) return "codex_cli_arguments";
        if (text.Contains("no stored rollout") || text.Contains("session") && (text.Contains("not found") || text.Contains("does not exist"))) return "codex_resume_missing";
        if (text.Contains("profile") && (text.Contains("not found") || text.Contains("does not exist") || text.Contains("could not find"))) return "codex_profile_missing";
        if (text.Contains("toml parse") || text.Contains("error loading config") || text.Contains("error parsing config")) return "codex_config_invalid";
        if (text.Contains("401") || text.Contains("unauthorized") || text.Contains("authentication") || text.Contains("invalid api key")) return "codex_authentication";
        if (text.Contains("403") || text.Contains("permission denied")) return "codex_access_denied";
        if (text.Contains("429") || text.Contains("rate limit") || text.Contains("at capacity")) return "codex_rate_limit";
        if (text.Contains("model metadata") && text.Contains("fallback metadata")) return "codex_model_metadata";
        if (text.Contains("model") && (text.Contains("not found") || text.Contains("does not exist") || text.Contains("not supported"))) return "codex_model_unavailable";
        if (text.Contains("context window") || text.Contains("context length")) return "codex_context_limit";
        if (text.Contains("timed out") || text.Contains("timeout") || text.Contains("codex exceeded") || text.Contains("codex stopped after")) return "codex_timeout";
        if (text.Contains("connection refused") || text.Contains("connection reset") || text.Contains("error sending request") || text.Contains("tls")) return "codex_network";
        return null;
    }

    internal static string Describe(string code) => code switch {
        "codex_cli_missing" => "找不到 Codex CLI，请核对后端账号下的 Codex:Command / AIAGENT_CODEX_COMMAND。",
        "codex_process_start" => "CLI 进程启动失败，请核对可执行文件和访问权限。",
        "codex_workspace_missing" => "项目工作目录不存在，请核对服务器上的项目路径。",
        "codex_access_denied" => "访问被拒绝，请核对服务账号的文件权限和模型访问权限。",
        "codex_profile_missing" => "找不到 Profile，请核对后端账号和 CODEX_HOME 下的配置文件。",
        "codex_config_invalid" => "CLI 配置解析失败，请检查 TOML 格式与 CLI 版本。",
        "codex_cli_arguments" => "CLI 不接受当前参数，请核对实际调用的 CLI 路径与版本。",
        "codex_resume_missing" => "无法续接 CLI 会话；本机可能没有该会话记录，请新建聊天验证。",
        "codex_authentication" => "模型鉴权失败，请核对后端账号使用的 Profile 认证配置。",
        "codex_model_unavailable" => "模型不可用，请核对 Profile 模型及 AiAgent 中的 model id 覆盖项。",
        "codex_rate_limit" => "模型请求受到限流或容量限制，请稍后重试。",
        "codex_context_limit" => "模型上下文超过限制，请压缩或新建会话。",
        "codex_network" => "CLI 无法连接模型服务，请检查网络与代理配置。",
        "codex_timeout" => "CLI 执行或等待输出超时。",
        "codex_session_metadata" => "会话配置无法解析，失败发生在 CLI 会话续接之前。",
        "codex_session_busy" => "会话正在执行，或已达到当前账号的并发会话上限。",
        "codex_model_policy" => "模型或推理等级未被当前策略允许，请核对模型配置。",
        "codex_model_metadata" => "CLI 未找到模型元数据，使用默认能力；此警告本身不代表请求失败。",
        "codex_cli_notice" => "CLI 返回提示信息，内容已省略；是否成功以本轮完成事件和退出码为准。",
        "codex_protocol_incomplete" => "CLI 已退出，但没有返回正常的本轮完成事件。",
        "codex_cancelled" => "请求已取消。",
        _ => "运行失败，请按诊断编号查看服务器分阶段日志。"
    };
}
