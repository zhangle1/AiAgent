using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Admin;
using SqlSugar;

namespace AiAgent.Backend.Services.Chat;

/// <summary>Durable at-most-once admission. Interrupted commands are never executed again.</summary>
public sealed class ChatCommandStore(ISqlSugarClient database)
{
    private readonly ISqlSugarClient _database = database;
    private static readonly string InstanceId = Guid.NewGuid().ToString("N");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Active = new();

    public static string Fingerprint(ChatCompleteRequest request)
    {
        var json = JsonSerializer.SerializeToNode(request, JsonOptions)!.AsObject();
        foreach (var key in new[] { "command_id", "recover_only", "client_runtime_id", "trace_id", "debug_trace" }) json.Remove(key);
        return Hash(json.ToJsonString());
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public Lease? Begin(ChatCompleteRequest request, AuthenticatedUser user)
    {
        if (request.RecoverOnly && (request.Goal != null || string.IsNullOrWhiteSpace(request.CommandId)))
            throw new InvalidOperationException("Recovery requires an ordinary chat command ID.");
        if (request.Goal != null || string.IsNullOrWhiteSpace(request.CommandId)) return null;
        if (request.CommandId.Length > 128 || string.IsNullOrWhiteSpace(request.RuntimeUserId) || string.IsNullOrWhiteSpace(request.SessionId))
            throw new InvalidOperationException("Invalid command identity.");
        using var db = _database.CopyNew();
        var session = db.Queryable<AiChatSession>().First(x => x.Id == request.SessionId && x.UserId == user.Id && !x.IsDeleted);
        if (session == null || user.Id != request.RuntimeUserId)
            throw new UnauthorizedAccessException();
        var id = Hash(request.RuntimeUserId + ":" + request.CommandId);
        var row = db.Queryable<AiChatCommand>().InSingle(id);
        var projectIds = row == null
            ? request.ProjectReferences.Select(x => x.ProjectId).Concat(new[] { request.CodeProjectId, session.CodeProjectId }.OfType<long>()).Distinct().ToArray()
            : JsonSerializer.Deserialize<long[]>(row.ProjectIdsJson ?? "[]")!;
        var access = new ProjectAccessService(db);
        if (projectIds.Any(projectId => !access.CanAccess(user, projectId))) throw new UnauthorizedAccessException();
        if (row == null && request.RecoverOnly) throw new InvalidOperationException("Command outcome unknown; automatic replay stopped.");
        var fingerprint = Fingerprint(request);
        if (row == null)
        {
            row = new AiChatCommand { Id = id, SessionId = request.SessionId, Fingerprint = fingerprint, InstanceId = InstanceId, Status = "running", ProjectIdsJson = JsonSerializer.Serialize(projectIds) };
            try { db.Insertable(row).ExecuteCommand(); return new Lease(this, row, true); }
            catch
            {
                // Only a verified winning row makes an insert failure a duplicate.
                row = db.Queryable<AiChatCommand>().InSingle(id);
                if (row == null) throw;
            }
        }
        if (row.SessionId != request.SessionId || (!request.RecoverOnly && row.Fingerprint != fingerprint))
            throw new InvalidOperationException("Command ID was reused with different input.");
        return new Lease(this, row, false);
    }

    public bool Cancel(ChatCompleteRequest request, AuthenticatedUser user)
    {
        request.RecoverOnly = true;
        using var lease = Begin(request, user);
        if (lease == null) return false;
        return lease.Cancel();
    }

    public sealed class Lease(ChatCommandStore store, AiChatCommand row, bool owner) : IDisposable
    {
        public bool IsOwner => owner;
        private bool terminal;
        private CancellationTokenSource? execution;
        public CancellationToken Link(CancellationToken token)
        {
            if (!owner) return token;
            execution = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (!Active.TryAdd(row.Id, execution)) throw new InvalidOperationException("Command is already active.");
            return execution.Token;
        }
        public bool Cancel()
        {
            if (!Active.TryGetValue(row.Id, out var source)) return false;
            try { source.Cancel(); return true; } catch (ObjectDisposedException) { return false; }
        }
        public void Append(AgentStreamEvent value)
        {
            if (!owner || terminal || value.Type == "debug_trace") return;
            using var db = store._database.CopyNew();
            db.Insertable(new AiChatCommandEvent { CommandId = row.Id, PayloadJson = JsonSerializer.Serialize(value, JsonOptions) }).ExecuteCommand();
            if (value.Type == "completed") Finish("completed");
            else if (value.Type == "error") Finish("unknown");
        }
        private void Finish(string status)
        {
            using var db = store._database.CopyNew();
            db.Updateable<AiChatCommand>().SetColumns(x => x.Status == status).Where(x => x.Id == row.Id).ExecuteCommand();
            terminal = true;
        }
        public async Task ReplayAsync(AgentStreamEventHandler emit, CancellationToken token)
        {
            long cursor = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var db = store._database.CopyNew();
                var current = db.Queryable<AiChatCommand>().InSingle(row.Id);
                var events = db.Queryable<AiChatCommandEvent>().Where(x => x.CommandId == row.Id && x.Id > cursor).OrderBy(x => x.Id).Take(256).ToList();
                foreach (var item in events)
                {
                    var value = JsonSerializer.Deserialize<AgentStreamEvent>(item.PayloadJson!, JsonOptions)!;
                    await emit(value, token);
                    cursor = item.Id;
                    if (value.Type is "completed" or "error") return;
                }
                if (events.Count > 0) continue;
                if (current == null || current.Status != "running" || current.InstanceId != InstanceId)
                {
                    await emit(new AgentStreamEvent { Type = "error", Content = "运行已中断或结果待确认，已停止自动重放。请检查已有结果后发起新操作。" }, token);
                    return;
                }
                await Task.Delay(250, token);
            }
        }
        public void Dispose()
        {
            if (!owner) return;
            try { if (!terminal) Finish("unknown"); }
            finally { Active.TryRemove(row.Id, out _); execution?.Dispose(); }
        }
    }
}
