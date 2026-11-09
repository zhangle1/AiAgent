using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IChatGoalControl
{
    object? GetGoal(string userId, string sessionId);
    object? ControlGoal(string userId, string sessionId, string action, long expectedVersion);
}

public sealed partial class CodexChatService : IChatGoalControl
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _goalExecutions = new();
    private static string GoalKey(string user, string session) => $"{user.Length}:{user}:{session}";

    public object? GetGoal(string userId, string sessionId)
    {
        var goal = LoadGoal(userId, sessionId);
        if (goal == null) return null;
        var running = _goalExecutions.ContainsKey(GoalKey(userId, sessionId));
        // A process restart cannot be interpreted as successful completion or automatic resume.
        if (!running && goal.Status == "active" && !(_activeSessionsByUser.TryGetValue(userId, out var sessions) && sessions.ContainsKey(sessionId)))
        {
            IDisposable recovery;
            try { recovery = AcquireActiveSession(userId, sessionId); }
            catch (InvalidOperationException) { return goal.Snapshot(true); }
            using (recovery)
            {
                goal = LoadGoal(userId, sessionId)!;
                if (goal.Status == "active")
                {
                    goal.Status = "paused";
                    goal.StopReason = "执行已中断，请确认后继续。";
                    SaveGoal(userId, sessionId, goal);
                }
            }
        }
        return goal.Snapshot(running);
    }

    public object? ControlGoal(string userId, string sessionId, string action, long expectedVersion)
    {
        if (action is not ("pause" or "end")) throw new ArgumentException("未知目标操作。");
        var goal = LoadGoal(userId, sessionId) ?? throw new InvalidOperationException("当前会话没有目标。");
        if (goal.Version != expectedVersion) throw new InvalidOperationException("目标状态已更新，请刷新后操作。");
        if (_goalExecutions.TryGetValue(GoalKey(userId, sessionId), out var execution))
        {
            if (action != "pause") throw new InvalidOperationException("请先暂停当前执行，再结束目标。");
            try { execution.Cancel(); }
            catch (ObjectDisposedException) { return GetGoal(userId, sessionId); }
            return goal.Snapshot(true);
        }
        using var sessionLock = AcquireActiveSession(userId, sessionId);
        goal = LoadGoal(userId, sessionId)!;
        if (goal.Version != expectedVersion) throw new InvalidOperationException("目标状态已更新，请刷新后操作。");
        goal.Status = action == "end" ? "ended" : "paused";
        goal.StopReason = action == "end" ? "用户结束目标。" : "用户暂停目标。";
        SaveGoal(userId, sessionId, goal);
        return goal.Snapshot(false);
    }

    private ChatGoalState? LoadGoal(string userId, string sessionId)
    {
        using var db = _db.CopyNew();
        var session = db.Queryable<AiChatSession>().First(x => x.Id == sessionId && x.UserId == userId && !x.IsDeleted)
            ?? throw new UnauthorizedAccessException("会话不存在或无权访问。");
        return string.IsNullOrWhiteSpace(session.GoalJson) ? null : JsonSerializer.Deserialize<ChatGoalState>(session.GoalJson, JsonOptions);
    }

    private void SaveGoal(string userId, string sessionId, ChatGoalState goal)
    {
        using var db = _db.CopyNew();
        goal.Version++;
        var json = JsonSerializer.Serialize(goal, JsonOptions);
        if (db.Updateable<AiChatSession>().SetColumns(x => x.GoalJson == json)
            .Where(x => x.Id == sessionId && x.UserId == userId && !x.IsDeleted).ExecuteCommand() != 1)
            throw new InvalidOperationException("目标状态保存失败。");
    }

    private async Task<CodexRunState> RunGoalRequestAsync(ChatCompleteRequest request, CodexResolvedModel model,
        string workspace, CodexRuntimeLease lease, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var user = request.RuntimeUserId!;
        var session = request.SessionId!;
        var input = request.Goal!;
        var binding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            user, session, request.CodeProjectId, workspace, model.Id, model.ProfileName, model.AppServerModelId,
            model.ReasoningEffort, request.CodexSandboxMode
        }))));
        var goal = LoadGoal(user, session);
        if (input.Action == "start")
        {
            if ((input.ExpectedVersion ?? 0) != (goal?.Version ?? 0))
                throw new InvalidOperationException("目标状态已更新，或该启动请求已经处理，请刷新后操作。");
            var objective = input.Objective?.Trim();
            if (string.IsNullOrWhiteSpace(objective) || objective.Length > 4000 || input.TokenBudget is <= 0)
                throw new ArgumentException("目标须为 1–4000 字符，Token 预算须为正整数或留空。");
            if (goal != null && goal.Status is not ("complete" or "ended"))
                throw new InvalidOperationException("已有目标，请继续或先结束当前目标。");
            goal = new ChatGoalState { Objective = objective, TokenBudget = input.TokenBudget, Binding = binding, Version = goal?.Version ?? 0 };
        }
        else if (input.Action == "resume")
        {
            if (goal == null || goal.Version != input.ExpectedVersion || goal.Binding != binding || goal.Status is "complete" or "ended")
                throw new InvalidOperationException("目标版本或模型、项目、权限配置不匹配，无法继续。");
        }
        else throw new ArgumentException("未知目标操作。");

        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var key = GoalKey(user, session);
        if (!_goalExecutions.TryAdd(key, execution)) throw new InvalidOperationException("目标正在执行。");
        request.GoalRuntime = goal;
        request.SaveGoal = value => SaveGoal(user, session, value);
        var partial = new StringBuilder();
        AgentStreamEventHandler capture = async (ev, ct) => {
            if (ev.Type == "content") partial.Append(ev.Content);
            if (onEvent != null) await onEvent(ev, ct);
        };
        try
        {
            goal.Status = "active";
            goal.StopReason = null;
            SaveGoal(user, session, goal);
            return await lease.RunAsync(request, model, workspace, capture, execution.Token);
        }
        catch (Exception ex)
        {
            if (goal.Status == "active")
            {
                goal.Status = "paused";
                goal.StopReason = ex is OperationCanceledException ? "已暂停；停止或断线中断了执行。" : "执行中断，请检查错误后继续。";
                if (ex.Message.Contains("Method not found", StringComparison.OrdinalIgnoreCase))
                    goal.StopReason = "当前 Codex CLI 不支持目标接口，请升级后继续。";
                SaveGoal(user, session, goal);
            }
            if (partial.Length > 0)
            {
                using var db = _db.CopyNew();
                db.Insertable(new AiChatMessage { SessionId = session, Role = "assistant", Content = partial.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { goal_id = goal.Id, goal_status = goal.Status, interrupted = true }) }).ExecuteCommand();
            }
            throw;
        }
        finally
        {
            _goalExecutions.TryRemove(key, out _);
            request.GoalRuntime = null;
            request.SaveGoal = null;
        }
    }

    private sealed partial class CodexAppServerWorker
    {
        private async Task<CodexRunState> RunGoalAsync(ChatCompleteRequest request, CodexResolvedModel model, string workspace,
            AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
        {
            var goal = request.GoalRuntime!;
            var state = new CodexRunState();
            using var timeout = new CancellationTokenSource(_turnTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var token = linked.Token;
            // The worker is discarded after interruption. Its persisted rollout remains resumable.
            async Task<JsonElement> Rpc(string method, object parameters)
            {
                var id = NextRequestId();
                await SendAsync(_process, new { method, id, @params = parameters }, token);
                return await ReadResponseAsync(_stdout, id, state, onEvent, _turnIdleTimeout, token);
            }
            async Task Publish(JsonElement response)
            {
                if (!response.TryGetProperty("goal", out var native) || native.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Codex 未返回目标状态；请检查 CLI 是否支持目标模式。");
                goal.Status = ReadString(native, "status") ?? "paused";
                if (native.TryGetProperty("tokensUsed", out var used) && used.TryGetInt64(out var tokens)) goal.TokensUsed = tokens;
                if (native.TryGetProperty("timeUsedSeconds", out var time) && time.TryGetInt64(out var seconds)) goal.TimeUsedSeconds = seconds;
                request.SaveGoal!(goal);
                await EmitAsync(onEvent, new AgentStreamEvent { Type = "goal_updated", Metadata = new() { ["goal"] = goal.Snapshot(true) } }, token);
            }
            state.GoalUpdated = Publish;
            try
            {
                if (goal.ThreadId == null)
                {
                    var response = await Rpc("thread/start", new { cwd = workspace, approvalPolicy = "never", sandbox = ToAppServerSandbox(_sandboxMode), ephemeral = false, model = model.AppServerModelId });
                    goal.ThreadId = ReadRequiredString(response, "thread", "id");
                    request.SaveGoal!(goal);
                }
                else await Rpc("thread/resume", new { threadId = goal.ThreadId, cwd = workspace, approvalPolicy = "never", sandbox = ToAppServerSandbox(_sandboxMode), model = model.AppServerModelId });

                var parameters = new Dictionary<string, object?> { ["threadId"] = goal.ThreadId, ["status"] = "active", ["origin"] = "user" };
                if (request.Goal!.Action == "start")
                {
                    parameters["objective"] = goal.Objective;
                    if (goal.TokenBudget.HasValue) parameters["tokenBudget"] = goal.TokenBudget;
                }
                await Publish(await Rpc("thread/goal/set", parameters));
                var turnId = NextRequestId();
                await SendAsync(_process, new { method = "turn/start", id = turnId, @params = new {
                    threadId = goal.ThreadId, clientUserMessageId = Guid.NewGuid().ToString("N"), input = BuildTurnInput(request),
                    cwd = workspace, effort = model.ReasoningEffort, approvalPolicy = "never", sandboxPolicy = new { type = ToAppServerSandboxPolicy(_sandboxMode) }
                } }, token);
                await ReadTurnAsync(_stdout, turnId, state, onEvent, _turnIdleTimeout, token);
                await Publish(await Rpc("thread/goal/get", new { threadId = goal.ThreadId }));
                // Native Codex owns continuation scheduling. Never launch a competing turn/start loop.
                while ((goal.Status == "active" || state.ActiveTurn) && state.TurnStatus == "completed")
                {
                    using var message = await ReadMessageAsync(_stdout, _turnIdleTimeout, token);
                    var root = message.RootElement;
                    if (ReadString(root, "method") == "thread/goal/updated")
                        await Publish(root.GetProperty("params"));
                    else
                    {
                        if (ReadString(root, "method") == "turn/started") state.HasAgentMessageDelta = false;
                        if (await HandleNotificationAsync(root, state, onEvent, token))
                            await Publish(await Rpc("thread/goal/get", new { threadId = goal.ThreadId }));
                    }
                }
                if (goal.Status == "active") { goal.Status = "paused"; goal.StopReason = "本轮未正常完成。"; request.SaveGoal!(goal); }
                _usable = false; // Discard goal subscriptions before the pool serves ordinary chat again.
                return state;
            }
            catch
            {
                _usable = false;
                state.GoalUpdated = null;
                // No second stdout reader: the cancelled read has completed before interrupt handling starts.
                if (state.ActiveTurnId != null && IsProcessRunning())
                {
                    using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        var id = NextRequestId();
                        await SendAsync(_process, new { method = "turn/interrupt", id, @params = new { threadId = goal.ThreadId, turnId = state.ActiveTurnId } }, stopTimeout.Token);
                        while (state.ActiveTurn)
                        {
                            using var message = await ReadMessageAsync(_stdout, TimeSpan.FromSeconds(3), stopTimeout.Token);
                            await HandleNotificationAsync(message.RootElement, state, null, stopTimeout.Token);
                        }
                    }
                    catch { /* Kill below is the bounded fallback for an unresponsive CLI. */ }
                }
                if (goal.ThreadId != null && goal.Status == "active" && IsProcessRunning())
                {
                    using var pauseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        var id = NextRequestId();
                        await SendAsync(_process, new { method = "thread/goal/set", id, @params = new {
                            threadId = goal.ThreadId, status = "paused", origin = cancellationToken.IsCancellationRequested ? "user" : "automatic"
                        } }, pauseTimeout.Token);
                        var response = await ReadResponseAsync(_stdout, id, state, null, TimeSpan.FromSeconds(3), pauseTimeout.Token);
                        if (response.TryGetProperty("goal", out var native) && native.ValueKind == JsonValueKind.Object)
                        {
                            if (native.TryGetProperty("tokensUsed", out var used) && used.TryGetInt64(out var tokens)) goal.TokensUsed = tokens;
                            if (native.TryGetProperty("timeUsedSeconds", out var time) && time.TryGetInt64(out var seconds)) goal.TimeUsedSeconds = seconds;
                        }
                    }
                    catch { /* A dead worker is reconciled on explicit resume; never create a replacement thread. */ }
                }
                TryKillProcess(_process);
                throw;
            }
        }

        private bool IsProcessRunning() { try { return !_process.HasExited; } catch { return false; } }
    }
}
