using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using SqlSugar;
using System.IO;
using System.Reflection;
using AiAgent.Backend.Services.Memory;
using AiAgent.Backend.Services.Usage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class ChatCommandTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"chat-command-{Guid.NewGuid():N}.db");
    private readonly SqlSugarScope db;
    private readonly ChatCommandStore store;
    private readonly AuthenticatedUser user = new("owner", "fixture");
    public ChatCommandTests()
    {
        db = new SqlSugarScope(new ConnectionConfig {
            DbType = DbType.Sqlite, ConnectionString = $"Data Source={path};Pooling=False", IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute, ConfigureExternalServices = new() { EntityService = (_, column) => {
                if (column.DataType == "nvarchar(max)") column.DataType = "text";
                if (column.IsIdentity) column.DataType = "INTEGER";
            } }
        });
        db.CodeFirst.InitTables(typeof(AiChatCommand), typeof(AiChatCommandEvent), typeof(AiChatSession));
        db.Insertable(new AiChatSession { Id = "session", UserId = user.Id, Title = "fixture" }).ExecuteCommand();
        store = new ChatCommandStore(db);
    }
    private ChatCompleteRequest Request(string id = "command") => new() { CommandId = id, SessionId = "session", RuntimeUserId = user.Id, Message = "hello" };

    [Fact]
    public async Task ParallelAdmissionHasOneOwnerAndOneDurableCommand()
    {
        var leases = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Begin(Request(), user)!)));
        Assert.Single(leases, lease => lease.IsOwner);
        Assert.Equal(1, db.Queryable<AiChatCommand>().Count());
        foreach (var lease in leases) lease.Dispose();
    }

    [Fact]
    public async Task DuplicateReplaysFullOutputAndCompletionWithoutStartingAgain()
    {
        using var first = store.Begin(Request(), user)!;
        first.Append(new AgentStreamEvent { Type = "content", Content = "你好" });
        first.Append(new AgentStreamEvent { Type = "content", Content = "世界" });
        first.Append(new AgentStreamEvent { Type = "completed" });
        using var duplicate = new ChatCommandStore(db).Begin(Request(), user)!;
        Assert.False(duplicate.IsOwner);
        var events = new List<AgentStreamEvent>();
        await duplicate.ReplayAsync((value, _) => { events.Add(value); return Task.CompletedTask; }, default);
        Assert.Equal(new[] { "content", "content", "completed" }, events.Select(x => x.Type));
        Assert.Equal("你好世界", string.Concat(events.Select(x => x.Content)));
    }

    [Fact]
    public async Task ActiveDuplicateReadsPastAndFutureEvents()
    {
        using var first = store.Begin(Request(), user)!;
        first.Append(new AgentStreamEvent { Type = "content", Content = "past" });
        using var duplicate = store.Begin(Request(), user)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<AgentStreamEvent>();
        var replay = duplicate.ReplayAsync((value, _) => {
            events.Add(value);
            if (value.Content == "past") first.Append(new AgentStreamEvent { Type = "completed" });
            return Task.CompletedTask;
        }, timeout.Token);
        await replay;
        Assert.Equal(new[] { "content", "completed" }, events.Select(x => x.Type));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrRestartPreservesPartialOutputAndStopsReplay(bool restart)
    {
        var first = store.Begin(Request(), user)!;
        first.Append(new AgentStreamEvent { Type = "content", Content = "partial" });
        if (restart) db.Updateable<AiChatCommand>().SetColumns(x => x.InstanceId == "old-process").Where(x => x.SessionId == "session").ExecuteCommand();
        else first.Dispose();
        var request = Request(); request.RecoverOnly = true; request.Message = "";
        using var recovered = store.Begin(request, user)!;
        var events = new List<AgentStreamEvent>();
        await recovered.ReplayAsync((value, _) => { events.Add(value); return Task.CompletedTask; }, default);
        Assert.Equal("partial", events[0].Content);
        Assert.Equal("error", events[1].Type);
        Assert.False(recovered.IsOwner);
        first.Dispose();
    }

    [Fact]
    public void SameIdCannotChangeMessageModelOrPermissions()
    {
        using var first = store.Begin(Request(), user)!;
        var changed = Request(); changed.Message = "different";
        Assert.Throws<InvalidOperationException>(() => store.Begin(changed, user));
        changed = Request(); changed.CodexModelId = "different";
        Assert.Throws<InvalidOperationException>(() => store.Begin(changed, user));
        changed = Request(); changed.CodexSandboxMode = "full-access";
        Assert.Throws<InvalidOperationException>(() => store.Begin(changed, user));
    }

    [Fact]
    public void DifferentOperationsCanHaveIdenticalTextAndMissingRecoveryCannotExecute()
    {
        using var first = store.Begin(Request("one"), user)!;
        using var second = store.Begin(Request("two"), user)!;
        Assert.True(first.IsOwner && second.IsOwner);
        var recovery = Request("missing"); recovery.RecoverOnly = true;
        Assert.Throws<InvalidOperationException>(() => store.Begin(recovery, user));
        Assert.Equal(2, db.Queryable<AiChatCommand>().Count());
    }

    [Fact]
    public void OtherUsersAndDeletedSessionsCannotRecover()
    {
        using var first = store.Begin(Request(), user)!;
        var other = new AuthenticatedUser("other", "other");
        var request = Request(); request.RuntimeUserId = other.Id;
        Assert.Throws<UnauthorizedAccessException>(() => store.Begin(request, other));
        db.Updateable<AiChatSession>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == "session").ExecuteCommand();
        Assert.Throws<UnauthorizedAccessException>(() => store.Begin(Request(), user));
    }

    [Fact]
    public void DiagnosticsAndBrowserRuntimeDoNotChangeFingerprintButSkillsDo()
    {
        var request = Request(); var fingerprint = ChatCommandStore.Fingerprint(request);
        request.ClientRuntimeId = "new-tab"; request.TraceId = "trace"; request.DebugTrace = true;
        Assert.Equal(fingerprint, ChatCommandStore.Fingerprint(request));
        request.SkillInvocations = [new ChatSkillReference { Id = "skill", Version = "v1" }];
        Assert.NotEqual(fingerprint, ChatCommandStore.Fingerprint(request));
    }

    [Fact]
    public void RecoveryCanCancelTheOriginalExecution()
    {
        using var first = store.Begin(Request(), user)!;
        var token = first.Link(default);
        Assert.False(token.IsCancellationRequested);
        Assert.True(store.Cancel(Request(), user));
        Assert.True(token.IsCancellationRequested);
        first.Dispose();
        Assert.False(store.Cancel(Request(), user));
    }

    [Fact]
    public async Task SseDuplicateDoesNotRepeatHistoryUsageOrModelExecution()
    {
        using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var calls = new List<string>();
        T Stub<T>() where T : class
        {
            var value = DispatchProxy.Create<T, Collaborator>();
            ((Collaborator)(object)value).InvokeMethod = (method, _) => {
                calls.Add(method.Name);
                if (method.Name == "TryGetCurrentUserAsync") return Task.FromResult<AuthenticatedUser?>(user);
                if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
                if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(null, [null]);
                return null;
            };
            return value;
        }
        var engine = new ChatEngine();
        var app = new ChatAppService(new HttpContextAccessor { HttpContext = context }, engine,
            Stub<IAuthService>(), Stub<IChatSessionService>(), Stub<IChatImageAttachmentService>(), Stub<IChatFileAttachmentService>(),
            Stub<IChatUploadLibraryService>(), Stub<IUsageStatisticsService>(), Stub<IMemoryService>(), Stub<IProjectReferenceContextService>(),
            Stub<IMarkdownDocumentReferenceContextService>(), Stub<IProjectAgentMarkdownIndexContextService>(), Stub<ICodexChatService>(),
            Stub<ICodexModelPolicyService>(), Stub<IImageOcrPolicyService>(), Stub<IChatDebugTraceStore>());
        await app.CompleteStream(Request(), default);
        context.Response.Body = new MemoryStream();
        await app.CompleteStream(Request(), default);
        Assert.Equal(1, engine.Calls);
        Assert.Equal(1, calls.Count(x => x == "RecordUserMessageAsync"));
        Assert.Equal(1, calls.Count(x => x == "RecordAssistantMessageAsync"));
        Assert.Equal(1, calls.Count(x => x == "RecordAsync"));
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("answer", response);
        Assert.Contains("completed", response);

        using var socket = new FixtureSocket(JsonSerializer.Serialize(Request()));
        context.Features.Set<IHttpWebSocketFeature>(new SocketFeature(socket));
        var handler = new ChatWebSocketHandler(engine, Stub<IAuthService>(), Stub<IChatSessionService>(),
            Stub<IChatImageAttachmentService>(), Stub<IChatFileAttachmentService>(), Stub<IUsageStatisticsService>(),
            Stub<IMemoryService>(), Stub<IProjectReferenceContextService>(), Stub<IMarkdownDocumentReferenceContextService>(),
            Stub<IProjectAgentMarkdownIndexContextService>(), Stub<ICodexModelPolicyService>(), Stub<IImageOcrPolicyService>(),
            Stub<IChatDebugTraceStore>(), NullLogger<ChatWebSocketHandler>.Instance);
        await handler.HandleClientAsync(context);
        Assert.Equal(1, engine.Calls);
        Assert.Equal(1, calls.Count(x => x == "RecordUserMessageAsync"));
        Assert.Contains(socket.Sent, value => value.Contains("answer"));
        Assert.Contains(socket.Sent, value => value.Contains("completed"));
    }

    private sealed class SocketFeature(WebSocket socket) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(socket);
    }
    private sealed class FixtureSocket(string request) : WebSocket
    {
        private WebSocketState state = WebSocketState.Open;
        private bool received;
        public List<string> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription => "fixture";
        public override WebSocketState State => state;
        public override string? SubProtocol => null;
        public override void Abort() => state = WebSocketState.Aborted;
        public override void Dispose() => state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) { state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => CloseAsync(status, description, token);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            Sent.Add(Encoding.UTF8.GetString(buffer)); return Task.CompletedTask;
        }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            if (received) await Task.Delay(Timeout.Infinite, token);
            received = true;
            var bytes = Encoding.UTF8.GetBytes(request);
            bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
        }
    }

    public class Collaborator : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => InvokeMethod(method!, args);
    }
    private sealed class ChatEngine : IChatOrchestrator
    {
        public int Calls { get; private set; }
        public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, CancellationToken token) => throw new NotSupportedException();
        public async Task<ChatCompleteResponse> CompleteStreamingAsync(ChatCompleteRequest request, AgentStreamEventHandler? emit, CancellationToken token)
        {
            Calls++;
            if (emit != null) await emit(new AgentStreamEvent { Type = "content", Content = "answer" }, token);
            return new ChatCompleteResponse { Answer = "answer", Content = "answer" };
        }
    }

    public void Dispose() { db.Dispose(); File.Delete(path); }
}
