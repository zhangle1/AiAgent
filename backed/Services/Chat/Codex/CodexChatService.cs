using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Services.Chat.Agentic;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface ICodexChatService
{
    Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the locally installed Codex app-server for a selected registered project.
/// The browser never talks to Codex directly; this service owns the JSONL protocol and forwards normalized chat events.
/// </summary>
public sealed class CodexChatService : ICodexChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqlSugarClient _db;
    private readonly IConfiguration _configuration;

    public CodexChatService(ISqlSugarClient db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("Message is required.", nameof(request));
        var workspacePath = ResolveWorkspacePath(request.CodeProjectId);
        var codexCommand = ResolveCodexCommand();
        var result = new CodexRunState();

        using var process = StartCodex(codexCommand, workspacePath);
        using var stdout = process.StandardOutput;
        var stderrPump = DrainStderrAsync(process.StandardError, cancellationToken);

        try
        {
            await SendAsync(process, new
            {
                method = "initialize",
                id = 1,
                @params = new { clientInfo = new { name = "aiagent", title = "AiAgent", version = "1.0" } }
            }, cancellationToken);
            await ReadResponseAsync(stdout, 1, result, onEvent, cancellationToken);
            await SendAsync(process, new { method = "initialized" }, cancellationToken);

            await SendAsync(process, new
            {
                method = "thread/start",
                id = 2,
                @params = new
                {
                    cwd = workspacePath,
                    approvalPolicy = "never",
                    sandbox = "danger-full-access",
                    ephemeral = true,
                    serviceName = "aiagent"
                }
            }, cancellationToken);
            var threadResponse = await ReadResponseAsync(stdout, 2, result, onEvent, cancellationToken);
            var threadId = ReadRequiredString(threadResponse, "thread", "id");

            await SendAsync(process, new
            {
                method = "turn/start",
                id = 3,
                @params = new
                {
                    threadId,
                    clientUserMessageId = request.SessionId,
                    input = BuildTurnInput(request),
                    cwd = workspacePath,
                    approvalPolicy = "never",
                    sandboxPolicy = new { type = "dangerFullAccess" }
                }
            }, cancellationToken);
            await ReadTurnAsync(stdout, 3, result, onEvent, cancellationToken);

            if (!string.Equals(result.TurnStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(result.TurnStatus == "interrupted" ? "Codex turn was interrupted." : result.ErrorMessage ?? "Codex turn did not complete.");
            }

            var modificationStatus = result.CompletedFiles.Count > 0 ? "completed_changed" : "completed_no_change";
            var answer = result.Answer.ToString().Trim();
            if (answer.Length == 0)
            {
                answer = result.CompletedFiles.Count > 0
                    ? $"Codex 已完成，并修改了 {result.CompletedFiles.Count} 个文件。"
                    : "Codex 已完成，未检测到文件修改。";
            }

            answer = AppendModifiedFileLinks(answer, result.CompletedFiles);

            await EmitAsync(onEvent, new AgentStreamEvent
            {
                Type = "done",
                ModelId = "codex",
                Model = "Codex",
                Content = answer,
                Metadata = new Dictionary<string, object?>
                {
                    ["agent"] = "codex",
                    ["codex_status"] = result.TurnStatus,
                    ["modification_status"] = modificationStatus,
                    ["modified_file_count"] = result.CompletedFiles.Count,
                    ["modified_files"] = result.CompletedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                }
            }, cancellationToken);

            var promptTokens = EstimateTokens(request.Message);
            var completionTokens = EstimateTokens(answer);
            return new ChatCompleteResponse
            {
                Query = request.Message,
                Answer = answer,
                Content = answer,
                ModelId = "codex",
                Model = "Codex",
                Usage = new ChatTokenUsage
                {
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = promptTokens + completionTokens,
                    IsEstimated = true
                }
            };
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
            }
            try { await stderrPump; } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    private string ResolveWorkspacePath(long? projectId)
    {
        if (!projectId.HasValue) throw new InvalidOperationException("Select a code project before handing the task to Codex.");
        var project = _db.Queryable<AiCodeProject>().First(item => item.Id == projectId.Value && !item.IsDeleted)
            ?? throw new InvalidOperationException("The selected code project does not exist.");
        if (string.IsNullOrWhiteSpace(project.RootPath)) throw new InvalidOperationException("The selected code project does not have a workspace path.");
        var workspacePath = Path.GetFullPath(project.RootPath);
        if (!Directory.Exists(workspacePath)) throw new DirectoryNotFoundException("The selected code project directory does not exist on this server.");
        return workspacePath;
    }

    private string ResolveCodexCommand()
    {
        var configured = _configuration["Codex:Command"] ?? Environment.GetEnvironmentVariable("AIAGENT_CODEX_COMMAND");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        // npm on Windows exposes a .cmd launcher. Prefer the known npm locations so a
        // long-running IDE/backend process does not accidentally resolve an App Store alias.
        var npmCommand = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "codex.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd")
        }.FirstOrDefault(File.Exists);
        return npmCommand ?? "codex";
    }

    private static Process StartCodex(string command, string workspacePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workspacePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the local Codex app-server.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("Unable to start the local Codex app-server. Configure a Codex CLI executable that the backend account can run.", exception);
        }
    }

    private static List<object> BuildTurnInput(ChatCompleteRequest request)
    {
        var input = new List<object> { new { type = "text", text = request.Message.Trim() } };
        foreach (var path in request.LocalImagePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            input.Add(new { type = "localImage", path, detail = "high" });
        }
        return input;
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions));
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<JsonElement> ReadResponseAsync(StreamReader stdout, int requestId, CodexRunState state, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var document = await ReadMessageAsync(stdout, cancellationToken);
            var root = document.RootElement;
            if (TryGetResponse(root, requestId, out var result)) return result;
            await HandleNotificationAsync(root, state, onEvent, cancellationToken);
        }
    }

    private static async Task ReadTurnAsync(StreamReader stdout, int requestId, CodexRunState state, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var requestAccepted = false;
        var turnCompleted = false;
        while (true)
        {
            using var document = await ReadMessageAsync(stdout, cancellationToken);
            var root = document.RootElement;
            if (TryGetResponse(root, requestId, out _))
            {
                requestAccepted = true;
            }
            else if (await HandleNotificationAsync(root, state, onEvent, cancellationToken))
            {
                turnCompleted = true;
            }
            if (requestAccepted && turnCompleted) return;
        }
    }

    private static async Task<JsonDocument> ReadMessageAsync(StreamReader stdout, CancellationToken cancellationToken)
    {
        var line = await stdout.ReadLineAsync(cancellationToken);
        if (line == null) throw new InvalidOperationException("Codex app-server closed before the task completed.");
        try { return JsonDocument.Parse(line); }
        catch (JsonException) { throw new InvalidOperationException("Codex app-server returned an invalid protocol message."); }
    }

    private static bool TryGetResponse(JsonElement root, int requestId, out JsonElement result)
    {
        result = default;
        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var value) || value != requestId) return false;
        if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(ReadString(error, "message") ?? "Codex app-server rejected the request.");
        if (!root.TryGetProperty("result", out var response)) throw new InvalidOperationException("Codex app-server returned a response without a result.");
        result = response.Clone();
        return true;
    }

    private static async Task<bool> HandleNotificationAsync(JsonElement root, CodexRunState state, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var method = ReadString(root, "method");
        if (string.IsNullOrWhiteSpace(method)) return false;
        var parameters = root.TryGetProperty("params", out var value) ? value : default;

        if (method == "turn/started")
        {
            await EmitAsync(onEvent, TraceEvent("Codex 已接管项目，开始执行。"), cancellationToken);
            return false;
        }
        if (method == "item/agentMessage/delta")
        {
            var delta = ReadString(parameters, "delta") ?? string.Empty;
            if (delta.Length > 0)
            {
                state.Answer.Append(delta);
                state.HasAgentMessageDelta = true;
                await EmitAsync(onEvent, new AgentStreamEvent { Type = "content", Content = delta, ModelId = "codex", Model = "Codex", Metadata = AgentMetadata() }, cancellationToken);
            }
            return false;
        }
        if (method == "item/commandExecution/outputDelta")
        {
            var output = ReadString(parameters, "delta");
            if (!string.IsNullOrWhiteSpace(output)) await EmitAsync(onEvent, TraceEvent($"Codex 命令输出：{TrimTrace(output)}"), cancellationToken);
            return false;
        }
        if (method == "item/started" || method == "item/completed")
        {
            if (parameters.TryGetProperty("item", out var item)) await HandleItemAsync(item, method == "item/completed", state, onEvent, cancellationToken);
            return false;
        }
        if (method == "turn/diff/updated")
        {
            await EmitAsync(onEvent, TraceEvent("Codex 已更新本轮文件差异。"), cancellationToken);
            return false;
        }
        if (method == "turn/completed")
        {
            var turn = parameters.TryGetProperty("turn", out var valueTurn) ? valueTurn : default;
            state.TurnStatus = ReadString(turn, "status") ?? "failed";
            state.ErrorMessage = turn.TryGetProperty("error", out var error) ? ReadString(error, "message") : null;
            return true;
        }
        return false;
    }

    private static async Task HandleItemAsync(JsonElement item, bool completed, CodexRunState state, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var type = ReadString(item, "type") ?? "tool";
        if (!completed)
        {
            await EmitAsync(onEvent, TraceEvent(type switch
            {
                "fileChange" => "Codex 正在准备文件修改。",
                "commandExecution" => $"Codex 正在执行：{TrimTrace(ReadString(item, "command") ?? "命令")}",
                _ => $"Codex 正在处理：{type}"
            }), cancellationToken);
            return;
        }

        if (type == "agentMessage" && !state.HasAgentMessageDelta)
        {
            var text = ReadString(item, "text") ?? string.Empty;
            if (text.Length > 0)
            {
                state.Answer.Append(text);
                await EmitAsync(onEvent, new AgentStreamEvent { Type = "content", Content = text, ModelId = "codex", Model = "Codex", Metadata = AgentMetadata() }, cancellationToken);
            }
            return;
        }

        if (type == "fileChange")
        {
            var status = ReadString(item, "status") ?? "failed";
            var paths = ReadFileChangePaths(item);
            if (status == "completed") foreach (var path in paths) state.CompletedFiles.Add(path);
            await EmitAsync(onEvent, new AgentStreamEvent
            {
                Type = "tool_result",
                Content = status == "completed" ? $"Codex 文件修改完成：{string.Join(", ", paths)}" : $"Codex 文件修改未完成：{string.Join(", ", paths)}（{status}）",
                Metadata = new Dictionary<string, object?> { ["agent"] = "codex", ["file_change_status"] = status, ["files"] = paths }
            }, cancellationToken);
            return;
        }

        if (type == "commandExecution")
        {
            var status = ReadString(item, "status") ?? "failed";
            await EmitAsync(onEvent, TraceEvent(status == "completed" ? "Codex 命令执行完成。" : $"Codex 命令执行状态：{status}"), cancellationToken);
        }
    }

    private static List<string> ReadFileChangePaths(JsonElement item)
    {
        if (!item.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) return [];
        return changes.EnumerateArray().Select(change => ReadString(change, "path")).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReadRequiredString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current)) throw new InvalidOperationException("Codex app-server returned an incomplete response.");
        }
        return current.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(current.GetString()) ? current.GetString()! : throw new InvalidOperationException("Codex app-server returned an empty identifier.");
    }

    private static string? ReadString(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string AppendModifiedFileLinks(string answer, IEnumerable<string> filePaths)
    {
        var links = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"- [{path.Replace('\\', '/')}](aiagent://code-file?path={Uri.EscapeDataString(path)})")
            .ToList();
        return links.Count == 0 ? answer : $"{answer}\n\n### Involved files\n{string.Join("\n", links)}";
    }

    private static int EstimateTokens(string value) => string.IsNullOrWhiteSpace(value) ? 0 : Math.Max(1, (int)Math.Ceiling(value.Trim().Length / 3.6));
    private static AgentStreamEvent TraceEvent(string content) => new() { Type = "tool", Content = content, ModelId = "codex", Model = "Codex", Metadata = AgentMetadata() };
    private static Dictionary<string, object?> AgentMetadata() => new() { ["agent"] = "codex" };
    private static string TrimTrace(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized[..Math.Min(normalized.Length, 240)];
    }
    private static Task EmitAsync(AgentStreamEventHandler? onEvent, AgentStreamEvent streamEvent, CancellationToken cancellationToken) => onEvent == null ? Task.CompletedTask : onEvent(streamEvent, cancellationToken);
    private static async Task DrainStderrAsync(StreamReader stderr, CancellationToken cancellationToken) { while (await stderr.ReadLineAsync(cancellationToken) != null) { } }

    private sealed class CodexRunState
    {
        public StringBuilder Answer { get; } = new();
        public HashSet<string> CompletedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasAgentMessageDelta { get; set; }
        public string? TurnStatus { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
