using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat.Agentic;
using SqlSugar;

namespace AiAgent.Backend.Services.Chat;

public sealed class ChatDeliveryException(string message) : InvalidOperationException(message);

public sealed class ChatDeliveryService(ISqlSugarClient database, IChatSessionService sessions, ICodexModelPolicyService? codexPolicy = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, SessionState> _states = new();
    private sealed class SessionState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<string, Waiting> Attached { get; } = new();
        public Waiting? Active;
    }
    internal sealed class Waiting(AiChatDelivery row, ChatCompleteRequest request)
    {
        public bool Claimable;
        public bool WasFirstInQueue { get; init; }
        public AiChatDelivery Row = row;
        public ChatCompleteRequest Request = request;
        public CodexTurnControl Control { get; } = new();
        public TaskCompletionSource<string> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancel { get; } = new();
    }

    public sealed class Admission : IAsyncDisposable
    {
        private readonly Func<bool, Task>? _finish;
        private readonly Action? _registered;
        private int _disposed;
        internal Admission(string kind, bool replay = false, Func<bool, Task>? finish = null, Action? registered = null, CancellationToken stop = default)
        { Kind = kind; IsReplay = replay; _finish = finish; _registered = registered; Stop = stop; }
        internal AuthenticatedUser? ExecutionUser { get; init; }
        public string Kind { get; }
        public bool IsReplay { get; }
        public bool Succeeded { get; set; }
        public CancellationToken Stop { get; }
        public void Registered() => _registered?.Invoke();
        public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0 && _finish is not null) await _finish(Succeeded); }
    }

    private static string Key(string userId, string commandId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + commandId)));
    private SessionState State(string userId, string sessionId) => _states.GetOrAdd(Key(userId, sessionId), _ => new());

    public ChatCompleteRequest Restore(AuthenticatedUser user, ChatCompleteRequest request)
    {
        if (!request.ResumeQueued) return request;
        if (request.RecoverOnly || string.IsNullOrWhiteSpace(request.CommandId)) throw new ChatDeliveryException("恢复排队消息需要原命令编号。");
        using var db = database.CopyNew();
        var row = db.Queryable<AiChatDelivery>().InSingle(Key(user.Id, request.CommandId));
        if (row is null || row.SessionId != request.SessionId || row.Status != "queued") throw new ChatDeliveryException("只有尚未执行的排队消息可以恢复。");
        var saved = JsonSerializer.Deserialize<ChatCompleteRequest>(row.RequestJson, Json)!;
        Authorize(user, saved);
        saved.ResumeQueued = true; saved.RuntimeUserId = user.Id; saved.RuntimeUsername = user.Username;
        return saved;
    }

    // The stable acknowledgement is a projection of the same insert that stores the work.
    // This also gives older delivery rows an identical receipt without a backfill.
    internal sealed record Reservation(AiChatDelivery Row, Waiting? Pending = null, bool Owner = false,
        bool Steer = false, Admission? Outcome = null)
    {
        public AgentStreamEvent Receipt() => new() { Type = "delivery_accepted", Metadata = new()
            { ["command_id"] = Row.CommandId, ["session_id"] = Row.SessionId, ["position"] = Row.Position } };
    }

    public async Task<Admission?> EnterAsync(AuthenticatedUser user, ChatCompleteRequest request, AgentStreamEventHandler emit, CancellationToken ct,
        Func<AuthenticatedUser, ChatCompleteRequest, CancellationToken, Task>? prepareInput = null)
    {
        var reserved = await ReserveAsync(user, request, prepareInput != null, ct);
        if (reserved == null) return null;
        if (request.RecoverOnly) await emit(reserved.Receipt(), ct);
        return await ClaimAsync(reserved, user, request, emit, ct, prepareInput);
    }

    internal async Task<Reservation?> ReserveAsync(AuthenticatedUser user, ChatCompleteRequest request, bool canPrepare, CancellationToken ct)
    {
        if (request.DeliveryIntent is null && !request.ResumeQueued)
        {
            if (!request.RecoverOnly || string.IsNullOrWhiteSpace(request.CommandId)) return null;
            using var lookup = database.CopyNew();
            if (!lookup.Queryable<AiChatDelivery>().Any(d => d.Id == Key(user.Id, request.CommandId))) return null;
        }
        if (request.Goal is not null) throw new ChatDeliveryException("目标模式继续使用暂停／恢复协议，请先暂停目标。");
        if (string.IsNullOrWhiteSpace(request.CommandId) || request.CommandId.Length > 128 || string.IsNullOrWhiteSpace(request.SessionId)) throw new ChatDeliveryException("投递需要会话及命令编号。");
        if (request.DeliveryIntent is not null and not "auto" and not "queue" and not "steer") throw new ChatDeliveryException("未知消息投递方式。");
        Authorize(user, request);
        var needsPreparation = request.HasBookSelection || request.AttachmentIds.Count > 0 || request.DocumentAttachmentIds.Count > 0;
        var state = State(user.Id, request.SessionId); Waiting? waiting = null; var owner = false; var steer = false;
        await state.Gate.WaitAsync(ct);
        try
        {
            using var db = database.CopyNew();
            Reconcile(db, user.Id, request.SessionId);
            var id = Key(user.Id, request.CommandId);
            var row = db.Queryable<AiChatDelivery>().InSingle(id);
            if (row is not null)
            {
                if (row.SessionId != request.SessionId || !request.RecoverOnly && row.Fingerprint != Fingerprint(request)) throw new ChatDeliveryException("同一命令编号不能更换消息、配置或会话。");
                if (row.Status == "steered") return new(row, Outcome: new("steered", true));
                if (row.Status == "cancelled") return new(row, Outcome: new(db.Queryable<AiChatCommand>().Any(c => c.Id == row.Id) ? "execute" : "cancelled", true));
                if (row.Status is "completed" or "failed") return new(row, Outcome: new(db.Queryable<AiChatCommand>().Any(c => c.Id == row.Id) ? "execute" : "uncertain", true));
                if (row.Status == "uncertain") return new(row, Outcome: new("uncertain", true));
                if (request.RecoverOnly && row.Status != "running" &&
                    (row.Status != "queued" || row.Held || !state.Attached.ContainsKey(id))) return new(row, Outcome: new("held", true));
                if (row.Held && !request.ResumeQueued) return new(row, Outcome: new("held", true));
                if (request.ResumeQueued && state.Attached.TryGetValue(id, out var ended) && ended.Ready.Task.IsCompletedSuccessfully && ended.Ready.Task.Result != "execute") state.Attached.Remove(id);
                if (state.Attached.TryGetValue(id, out waiting)) { /* A duplicate joins admission; it never starts another turn. */ }
                else if (row.Status == "running") return new(row, Outcome: new("uncertain", true));
                else
                {
                    row.Held = false; row.InstanceId = _instance; row.UpdatedAt = DateTime.UtcNow; db.Updateable(row).ExecuteCommand();
                    waiting = new(row, request); state.Attached.Add(id, waiting); owner = true;
                }
            }
            else
            {
                if (request.RecoverOnly || request.ResumeQueued) throw new ChatDeliveryException("排队记录不存在，未重新执行。");
                var queue = db.Queryable<AiChatDelivery>().Where(d => d.UserId == user.Id && d.SessionId == request.SessionId)
                    .GroupBy(d => d.SessionId).Select(d => new {
                        LastPosition = SqlFunc.AggregateMax(d.Position),
                        Pending = SqlFunc.AggregateSum(SqlFunc.IIF(d.Status == "queued", 1, 0)),
                        Blocked = SqlFunc.AggregateMax(SqlFunc.IIF(d.Status == "uncertain" || (d.Status == "queued" && d.Held), 1, 0))
                    }).First();
                if (queue?.Pending >= 100) throw new ChatDeliveryException("本会话最多保留 100 条待执行消息。");
                var active = state.Active;
                var native = active?.Control.Ready == true && (!needsPreparation || canPrepare) && CanSteer(active.Request, request);
                if (native && codexPolicy is not null) _ = codexPolicy.ResolveModel(request.CodexModelId, request.CodexReasoningEffort);
                if ((active is not null || queue?.Pending > 0) && request.DeliveryIntent == "steer" && !native) throw new ChatDeliveryException("当前引擎、轮次或输入内容不支持原生追加，请选择排队继续。");
                steer = active is not null && native && request.DeliveryIntent is "auto" or "steer";
                var payload = JsonSerializer.Serialize(request, Json);
                if (payload.Length > 2_000_000 || request.Message.Length > 200_000) throw new ChatDeliveryException("投递消息超过长度上限。");
                var position = (queue?.LastPosition ?? 0) + 1;
                row = new() { Id = id, UserId = user.Id, SessionId = request.SessionId, CommandId = request.CommandId, Fingerprint = Fingerprint(request), RequestJson = payload,
                    Position = position, InstanceId = _instance, Status = steer ? "steering" : "queued", Held = !steer && queue?.Blocked == 1 };
                db.Insertable(row).ExecuteCommand();
                waiting = new(row, request) { WasFirstInQueue = queue is null || queue.Pending == 0 }; state.Attached.Add(id, waiting); owner = true;
                if (steer) waiting.Control.Bind(() => active!.Control.Ready, active!.Control.SteerAsync);
            }
            if (!steer && waiting!.Row.Held)
            {
                if (owner) { state.Attached.Remove(waiting.Row.Id); waiting.Cancel.Dispose(); }
                return new(waiting.Row, Owner: owner, Outcome: new("held", true));
            }
            return new(waiting!.Row, waiting, owner, steer);
        }
        finally { state.Gate.Release(); }

    }

    internal async Task<Admission> ClaimAsync(Reservation reserved, AuthenticatedUser user, ChatCompleteRequest request,
        AgentStreamEventHandler emit, CancellationToken ct, Func<AuthenticatedUser, ChatCompleteRequest, CancellationToken, Task>? prepareInput)
    {
        if (reserved.Outcome != null) return reserved.Outcome;
        var waiting = reserved.Pending!;
        var state = State(user.Id, reserved.Row.SessionId);
        var owner = reserved.Owner;

        if (reserved.Steer)
        {
            if (request.HasBookSelection || request.AttachmentIds.Count > 0 || request.DocumentAttachmentIds.Count > 0)
            {
                try
                {
                    await prepareInput!(user, request, ct);
                    if (!waiting!.Control.Ready) throw new InvalidOperationException("当前轮次已结束，请重新发送或选择排队继续。");
                }
                catch (Exception exception)
                {
                    await FinishSteering(state, waiting!, "cancelled");
                    if (exception is InvalidOperationException) throw new ChatDeliveryException(exception.Message);
                    throw;
                }
            }
            try
            {
                // Persist the user's input before attempting the RPC. An ambiguous acknowledgement is never retried automatically.
                await sessions.RecordUserMessageAsync(user, request, ct);
                await waiting!.Control.SteerAsync(request, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
                await FinishSteering(state, waiting, "steered");
                return new("steered");
            }
            catch
            {
                await FinishSteering(state, waiting!, "uncertain");
                return new("uncertain");
            }
        }
        try
        {
            if (owner)
            {
                await state.Gate.WaitAsync(ct);
                try
                {
                    waiting.Claimable = true;
                    if (state.Active == null && !waiting.Ready.Task.IsCompleted)
                    {
                        using var db = database.CopyNew();
                        StartNext(db, state, user.Id, reserved.Row.SessionId, waiting.WasFirstInQueue ? waiting : null);
                    }
                }
                finally { state.Gate.Release(); }
            }
            if (!waiting.Ready.Task.IsCompleted) await emit(new() { Type = "delivery_queued", Content = "消息已保存，等待前一轮完成。" }, ct);
            while (!waiting.Ready.Task.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (await Task.WhenAny(waiting.Ready.Task, heartbeat) == heartbeat)
                    await emit(new() { Type = "delivery_queued", Content = "消息已保存，仍在等待前一轮完成。" }, ct);
            }
            var kind = await waiting.Ready.Task.WaitAsync(ct);
            if (kind != "execute")
            {
                if (owner)
                {
                    await state.Gate.WaitAsync(ct);
                    try { if (state.Attached.TryGetValue(waiting.Row.Id, out var attached) && ReferenceEquals(attached, waiting)) state.Attached.Remove(waiting.Row.Id); }
                    finally { state.Gate.Release(); }
                    waiting.Cancel.Dispose();
                }
                return new(kind, true);
            }
            if (!owner) { await waiting.Registered.Task.WaitAsync(ct); return new("execute", true); }
            var executionUser = Authorize(user, request);
            request.TurnControl = waiting.Control;
            request.ConversationContext = new()
            {
                LoadThread = binding => {
                    using var db = database.CopyNew();
                    return db.Queryable<AiChatDelivery>().Where(d => d.UserId == user.Id && d.SessionId == request.SessionId && d.NativeBinding == binding && d.NativeThreadId != null)
                        .OrderBy(d => d.Position, OrderByType.Desc).Select(d => d.NativeThreadId).First();
                },
                SaveThread = (binding, thread) => {
                    using var db = database.CopyNew();
                    if (db.Updateable<AiChatDelivery>().SetColumns(d => new AiChatDelivery { NativeBinding = binding, NativeThreadId = thread })
                        .Where(d => d.Id == waiting.Row.Id && d.Status == "running" && d.InstanceId == _instance).ExecuteCommand() != 1)
                        throw new ChatDeliveryException("会话绑定保存失败，未启动新轮次。");
                }
            };
            return new("execute", false, success => LeaveAsync(state, waiting, success), () => waiting.Registered.TrySetResult(), waiting.Cancel.Token) { ExecutionUser = executionUser };
        }
        catch
        {
            if (owner && waiting is not null) await LeaveAsync(state, waiting, false);
            throw;
        }
    }

    private async Task FinishSteering(SessionState state, Waiting waiting, string status)
    {
        await state.Gate.WaitAsync();
        try
        {
            using var db = database.CopyNew();
            waiting.Row.Status = status; waiting.Row.UpdatedAt = DateTime.UtcNow; db.Updateable(waiting.Row).ExecuteCommand();
            if (status == "uncertain")
            {
                db.Updateable<AiChatDelivery>().SetColumns(d => new AiChatDelivery { Held = true, UpdatedAt = DateTime.UtcNow })
                    .Where(d => d.UserId == waiting.Row.UserId && d.SessionId == waiting.Row.SessionId && d.Status == "queued").ExecuteCommand();
                foreach (var pending in state.Attached.Values.Where(w => w.Row.Status == "queued")) pending.Ready.TrySetResult("held");
            }
            waiting.Ready.TrySetResult(status); state.Attached.Remove(waiting.Row.Id);
            waiting.Cancel.Dispose();
        }
        finally { state.Gate.Release(); }
    }

    private async Task LeaveAsync(SessionState state, Waiting waiting, bool success)
    {
        await state.Gate.WaitAsync();
        try
        {
            // A held item may already have been explicitly resumed on a new connection.
            if (!state.Attached.TryGetValue(waiting.Row.Id, out var attached) || !ReferenceEquals(attached, waiting))
            {
                waiting.Registered.TrySetCanceled();
                waiting.Cancel.Dispose();
                return;
            }
            using var db = database.CopyNew();
            var active = ReferenceEquals(state.Active, waiting);
            var row = db.Queryable<AiChatDelivery>().InSingle(waiting.Row.Id);
            if (row is not null && row.Status != "cancelled")
            {
                row.Status = active ? success ? "completed" : waiting.Cancel.IsCancellationRequested ? "cancelled" : "failed" : "queued";
                row.Held = !success; row.UpdatedAt = DateTime.UtcNow; db.Updateable(row).ExecuteCommand();
            }
            if (active)
            {
                state.Active = null;
                if (!success)
                {
                    db.Updateable<AiChatDelivery>().SetColumns(d => new AiChatDelivery { Held = true, UpdatedAt = DateTime.UtcNow })
                        .Where(d => d.UserId == waiting.Row.UserId && d.SessionId == waiting.Row.SessionId && d.Status == "queued").ExecuteCommand();
                    foreach (var other in state.Attached.Values.Where(w => w.Row.Status == "queued")) other.Ready.TrySetResult("held");
                }
            }
            waiting.Registered.TrySetCanceled(); state.Attached.Remove(waiting.Row.Id); waiting.Cancel.Dispose();
            if (success) StartNext(db, state, waiting.Row.UserId, waiting.Row.SessionId);
        }
        finally { state.Gate.Release(); }
    }

    private void StartNext(ISqlSugarClient db, SessionState state, string userId, string sessionId, Waiting? next = null)
    {
        if (state.Active is not null) return;
        // Under the session gate a new row inserted into an empty queue already has
        // the earliest position. Later inserts cannot overtake it.
        if (next == null)
        {
            var first = db.Queryable<AiChatDelivery>().Where(d => d.UserId == userId && d.SessionId == sessionId && d.Status == "queued")
                .OrderBy(d => d.Position).Select(d => new { d.Id, d.Held }).First();
            if (first == null || first.Held || !state.Attached.TryGetValue(first.Id, out next) || !next.Claimable) return;
        }
        var id = next.Row.Id;
        var now = DateTime.UtcNow;
        var claimed = db.Updateable<AiChatDelivery>().SetColumns(d => new AiChatDelivery { Status = "running", UpdatedAt = now })
            .Where(d => d.Id == id && d.Status == "queued" && !d.Held && d.InstanceId == _instance).ExecuteCommand();
        if (claimed != 1) { next.Ready.TrySetResult("uncertain"); return; }
        next.Row.Status = "running";
        state.Active = next;
        next.Ready.TrySetResult("execute");
    }

    public List<string> ActiveSessions(AuthenticatedUser user)
    {
        using var db = database.CopyNew();
        return db.Queryable<AiChatDelivery, AiChatSession>((delivery, session) => delivery.SessionId == session.Id)
            .Where((delivery, session) => delivery.UserId == user.Id && session.UserId == user.Id &&
                !session.IsArchived && !session.IsDeleted && delivery.InstanceId == _instance && delivery.Status == "running")
            .Select((delivery, session) => delivery.SessionId).Distinct().ToList();
    }

    public async Task<List<ChatDeliveryDto>> ListAsync(AuthenticatedUser user, string sessionId, CancellationToken ct)
    {
        using (var probe = database.CopyNew())
            if (!probe.Queryable<AiChatSession>().Any(s => s.Id == sessionId && s.UserId == user.Id && !s.IsDeleted && !s.IsArchived)) return [];
        Authorize(user, new() { SessionId = sessionId }, allowActiveGoal: true);
        var state = State(user.Id, sessionId); await state.Gate.WaitAsync(ct);
        try
        {
            using var db = database.CopyNew(); Reconcile(db, user.Id, sessionId);
            return db.Queryable<AiChatDelivery>().Where(d => d.UserId == user.Id && d.SessionId == sessionId && (d.Status == "queued" || d.Status == "running" || d.Status == "steering" || d.Status == "uncertain"))
                .OrderBy(d => d.Position).Take(102).ToList().Select(row => {
                    var request = JsonSerializer.Deserialize<ChatCompleteRequest>(row.RequestJson, Json)!;
                    return new ChatDeliveryDto { CommandId = row.CommandId, SessionId = row.SessionId, Position = row.Position, Status = row.Status, QueueHeld = row.Held,
                        Agent = request.Agent, Message = request.Message[..Math.Min(600, request.Message.Length)] };
                }).ToList();
        }
        finally { state.Gate.Release(); }
    }

    public async Task<bool> CancelAsync(AuthenticatedUser user, string sessionId, string commandId, CancellationToken ct)
    {
        Authorize(user, new() { SessionId = sessionId }, allowActiveGoal: true);
        var state = State(user.Id, sessionId); CancellationTokenSource? stop = null;
        await state.Gate.WaitAsync(ct);
        try
        {
            using var db = database.CopyNew();
            var row = db.Queryable<AiChatDelivery>().InSingle(Key(user.Id, commandId));
            if (row is null || row.SessionId != sessionId) return false;
            if (row.Status == "running")
            {
                if (state.Active?.Row.Id != row.Id) return false;
                stop = state.Active.Cancel;
            }
            else if (row.Status is "queued" or "uncertain")
            {
                row.Status = "cancelled"; row.UpdatedAt = DateTime.UtcNow; db.Updateable(row).ExecuteCommand();
                if (state.Attached.TryGetValue(row.Id, out var waiting)) { waiting.Ready.TrySetResult("cancelled"); state.Attached.Remove(row.Id); }
                StartNext(db, state, user.Id, sessionId);
            }
            else return false;
        }
        finally { state.Gate.Release(); }
        try { stop?.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    private void Reconcile(ISqlSugarClient db, string userId, string sessionId)
    {
        // A fresh process never adopts an ambiguous execution, and never automatically runs disconnected pending input.
        db.Updateable<AiChatDelivery>().SetColumns(d => new AiChatDelivery {
            Held = true,
            InstanceId = SqlFunc.IIF(d.Status == "queued", _instance, d.InstanceId),
            Status = SqlFunc.IIF(d.Status == "queued", "queued", "uncertain")
        }).Where(d => d.UserId == userId && d.SessionId == sessionId && d.InstanceId != _instance &&
            (d.Status == "running" || d.Status == "steering" || d.Status == "queued")).ExecuteCommand();
    }

    internal AuthenticatedUser Authorize(AuthenticatedUser user, ChatCompleteRequest request, bool allowActiveGoal = false)
    {
        using var db = database.CopyNew();
        var current = db.Queryable<AiUser, AiChatSession>((u, session) => u.Id == session.UserId)
            .Where((u, session) => u.Id == user.Id && !u.IsDisabled && session.Id == request.SessionId && !session.IsDeleted && !session.IsArchived)
            .Select((u, session) => new { u.Id, u.Username, u.Role, u.CanCommitCode, u.Alias, session.CodeProjectId, session.GoalJson }).First();
        if (current is null) throw new UnauthorizedAccessException("会话已不可访问。");
        var actor = new AuthenticatedUser(current.Id, current.Username, current.Role, current.CanCommitCode, current.Alias);
        var access = new ProjectAccessService(db);
        var ids = request.ProjectReferences.Select(p => p.ProjectId).Concat(new[] { request.CodeProjectId, current.CodeProjectId }.OfType<long>()).Distinct();
        if (ids.Any(id => !access.CanAccess(actor, id))) throw new UnauthorizedAccessException("项目权限已变化。");
        if (!allowActiveGoal && !string.IsNullOrWhiteSpace(current.GoalJson))
        {
            using var goal = JsonDocument.Parse(current.GoalJson);
            if (goal.RootElement.TryGetProperty("status", out var status) && status.GetString() == "active") throw new ChatDeliveryException("请先暂停当前目标，再投递普通消息。");
        }
        return actor;
    }

    private static string Fingerprint(ChatCompleteRequest request)
    {
        var copy = JsonSerializer.SerializeToNode(request, Json)!.AsObject();
        foreach (var key in new[] { "command_id", "recover_only", "resume_queued", "client_runtime_id", "trace_id", "debug_trace" }) copy.Remove(key);
        return MarkdownHash(copy.ToJsonString());
    }
    private static string MarkdownHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static bool CanSteer(ChatCompleteRequest active, ChatCompleteRequest next)
        => active.Agent == "codex" && next.Agent == "codex" && active.Goal is null && next.Goal is null
            && active.CodeProjectId == next.CodeProjectId && active.CodexModelId == next.CodexModelId && active.CodexReasoningEffort == next.CodexReasoningEffort && active.CodexSandboxMode == next.CodexSandboxMode
            && next.SkillInvocation is null && next.SkillInvocations.Count == 0
            && ContextKey(active) == ContextKey(next);

    private static string ContextKey(ChatCompleteRequest request) => CodexConversationContext.SelectionKey(request);
}
