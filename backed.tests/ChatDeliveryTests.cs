using System.IO;
using System.Reflection;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class ChatDeliveryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "chat-delivery-tests-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqlSugarScope _db;
    private readonly ChatDeliveryService _service;
    private readonly IChatSessionService _sessions;
    private readonly AuthenticatedUser _user = new("owner", "fixture");
    private readonly List<string> _messages = [];
    public ChatDeliveryTests()
    {
        _db = new(new ConnectionConfig { DbType = DbType.Sqlite, ConnectionString = $"Data Source={_path};Pooling=False", IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new() { EntityService = (_, column) => { if (column.DataType == "nvarchar(max)") column.DataType = "text"; if (column.IsIdentity) column.DataType = "INTEGER"; } } });
        _db.CodeFirst.InitTables(typeof(AiUser), typeof(AiChatSession), typeof(AiChatDelivery), typeof(AiChatCommand));
        _db.Insertable(new AiUser { Id = _user.Id, Username = _user.Username }).ExecuteCommand();
        _db.Insertable(new AiChatSession { Id = "session", UserId = _user.Id, Title = "fixture" }).ExecuteCommand();
        _sessions = DispatchProxy.Create<IChatSessionService, SessionsProxy>();
        ((SessionsProxy)(object)_sessions).Messages = _messages;
        _service = new(_db, _sessions);
    }
    private ChatCompleteRequest Request(string command, string intent = "queue") => new() { CommandId = command, SessionId = "session", Message = command, DeliveryIntent = intent, RuntimeUserId = _user.Id, Agent = "codex" };
    private static Task Ignore(AgentStreamEvent value, CancellationToken ct) => Task.CompletedTask;
    private static TaskCompletionSource QueuedSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<ChatDeliveryService.Admission?> Enter(ChatCompleteRequest request, CancellationToken ct = default) => _service.EnterAsync(_user, request, Ignore, ct);

    [Fact]
    public async Task QueueIsDurableOrderedAndAdvancesOnlyAfterSuccessfulRelease()
    {
        var first = (await Enter(Request("A")))!; first.Registered();
        var signal = QueuedSignal();
        var second = _service.EnterAsync(_user, Request("B"), (e, _) => { signal.TrySetResult(); return Task.CompletedTask; }, default);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var third = Enter(Request("C"));
        Assert.False(second.IsCompleted); Assert.False(third.IsCompleted);
        Assert.Equal(3, _db.Queryable<AiChatDelivery>().Count()); Assert.Empty(_messages);
        var pending = await _service.ListAsync(_user, "session", default);
        Assert.Equal(new[] { "A", "B", "C" }, pending.Select(row => row.CommandId));
        first.Succeeded = true; await first.DisposeAsync();
        var b = (await second.WaitAsync(TimeSpan.FromSeconds(3)))!; b.Registered(); Assert.False(third.IsCompleted);
        b.Succeeded = true; await b.DisposeAsync();
        var c = (await third.WaitAsync(TimeSpan.FromSeconds(3)))!; c.Registered(); c.Succeeded = true; await c.DisposeAsync();
        Assert.All(_db.Queryable<AiChatDelivery>().ToList(), row => Assert.Equal("completed", row.Status));
        await first.DisposeAsync(); // Disposal must not resurrect a completed delivery.
        Assert.Empty(await _service.ListAsync(_user, "session", default));
    }

    [Fact]
    public async Task StopHoldsPendingMessagesAndExplicitResumeUsesOriginalPayload()
    {
        var first = (await Enter(Request("A")))!; first.Registered();
        var second = Enter(Request("B")); var third = Enter(Request("C"));
        await first.DisposeAsync();
        Assert.Equal("held", (await second.WaitAsync(TimeSpan.FromSeconds(3)))!.Kind);
        Assert.Equal("held", (await third.WaitAsync(TimeSpan.FromSeconds(3)))!.Kind);
        var pending = await _service.ListAsync(_user, "session", default);
        Assert.Equal(2, pending.Count); Assert.All(pending, row => Assert.True(row.QueueHeld));
        var recovered = _service.Restore(_user, new() { SessionId = "session", CommandId = "B", ResumeQueued = true, Message = "must not replace saved input" });
        Assert.Equal("B", recovered.Message);
        var resumed = (await Enter(recovered))!; Assert.Equal("execute", resumed.Kind); resumed.Registered(); resumed.Succeeded = true; await resumed.DisposeAsync();
        Assert.True(Assert.Single(await _service.ListAsync(_user, "session", default)).QueueHeld);
    }

    [Fact]
    public async Task DuplicateCommandsNeverAcquireASecondExecutionAndChangedInputIsRejected()
    {
        var first = (await Enter(Request("A")))!; first.Registered();
        var duplicate = (await Enter(Request("A")))!; Assert.True(duplicate.IsReplay);
        var changed = Request("A"); changed.Message = "different";
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Enter(changed));
        Assert.Equal(1, _db.Queryable<AiChatDelivery>().Count());
        await duplicate.DisposeAsync(); Assert.Equal("running", _db.Queryable<AiChatDelivery>().First().Status);
        await first.DisposeAsync();
    }

    [Fact]
    public async Task NativeSteeringPersistsOnceAndDoesNotFinishCurrentExecution()
    {
        var request = Request("A"); var active = (await Enter(request))!; active.Registered();
        var inputs = new List<string>();
        using var native = request.TurnControl!.Bind(() => true, (turn, _) => { inputs.Add(turn.Message); return Task.CompletedTask; });
        Assert.Equal("steered", (await Enter(Request("B", "steer")))!.Kind);
        Assert.Equal("steered", (await Enter(Request("B", "steer")))!.Kind);
        Assert.Equal(new[] { "B" }, inputs); Assert.Equal(new[] { "B" }, _messages);
        Assert.Equal("running", _db.Queryable<AiChatDelivery>().Where(row => row.CommandId == "A").First().Status);
        active.Succeeded = true; await active.DisposeAsync();
    }

    [Fact]
    public async Task ScopedSteeringPreparesOnlyOnceAndRejectsUnavailableChaptersBeforeHistory()
    {
        var request = Request("A");
        request.KnowledgeScopeUris = ["viking://resources/books/"];
        await using var active = (await Enter(request))!;
        active.Registered();
        var received = new List<string>();
        using var native = request.TurnControl!.Bind(() => true, (input, _) => {
            received.Add(Assert.Single(input.BookChapters).RelativePath);
            return Task.CompletedTask;
        });
        var calls = 0;
        Task Prepare(AuthenticatedUser user, ChatCompleteRequest input, CancellationToken ct)
        {
            calls++;
            if (input.Message == "revoked") throw new InvalidOperationException("所选目录不可访问");
            input.BookChapters = [new("manual", "chapter.md", "synthetic", "synthetic")];
            return Task.CompletedTask;
        }
        Task<ChatDeliveryService.Admission?> Steer(string command)
        {
            var followUp = Request(command, "steer");
            followUp.KnowledgeScopeUris = [.. request.KnowledgeScopeUris];
            return _service.EnterAsync(_user, followUp, Ignore, default, Prepare);
        }
        Assert.Equal("steered", (await Steer("B"))!.Kind);
        Assert.Equal("steered", (await Steer("B"))!.Kind);
        await Assert.ThrowsAsync<ChatDeliveryException>(() => Steer("revoked"));
        Assert.Equal(2, calls);
        Assert.Equal(new[] { "chapter.md" }, received);
        Assert.Equal(new[] { "B" }, _messages);
        active.Succeeded = true;
    }

    [Fact]
    public async Task UnacknowledgedSteeringIsNotRetriedAndHoldsFutureInput()
    {
        var request = Request("A"); var active = (await Enter(request))!; active.Registered(); var calls = 0;
        var alreadyQueued = Enter(Request("before-steer"));
        using var native = request.TurnControl!.Bind(() => true, (_, _) => { calls++; throw new IOException("synthetic transport failure"); });
        Assert.Equal("uncertain", (await Enter(Request("B", "steer")))!.Kind);
        Assert.Equal("uncertain", (await Enter(Request("B", "steer")))!.Kind); Assert.Equal(1, calls);
        Assert.Equal("held", (await alreadyQueued.WaitAsync(TimeSpan.FromSeconds(3)))!.Kind);
        active.Succeeded = true; await active.DisposeAsync();
        Assert.Equal("held", (await Enter(Request("C")))!.Kind);
        Assert.Contains(await _service.ListAsync(_user, "session", default), row => row.CommandId == "B" && row.Status == "uncertain");
    }

    [Fact]
    public async Task AutoFallsBackToQueueAndExplicitUnsupportedSteerIsRejected()
    {
        var active = (await Enter(Request("A")))!; active.Registered();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Enter(Request("bad", "steer")));
        var pending = Enter(Request("B", "auto")); Assert.False(pending.IsCompleted);
        active.Succeeded = true; await active.DisposeAsync();
        var second = (await pending.WaitAsync(TimeSpan.FromSeconds(3)))!; second.Registered(); await second.DisposeAsync();
    }

    [Fact]
    public async Task CancelPendingInputDoesNotExecuteItOrCancelTheActiveTurn()
    {
        var active = (await Enter(Request("A")))!; active.Registered(); var pending = Enter(Request("B"));
        Assert.True(await _service.CancelAsync(_user, "session", "B", default));
        Assert.Equal("cancelled", (await pending.WaitAsync(TimeSpan.FromSeconds(3)))!.Kind);
        Assert.Equal("cancelled", (await Enter(Request("B")))!.Kind); Assert.False(active.Stop.IsCancellationRequested);
        active.Succeeded = true; await active.DisposeAsync(); Assert.Empty(_messages);
    }

    [Fact]
    public async Task PendingDisconnectAndRestartRetainInputWithoutAdoptingUnknownRuns()
    {
        var active = (await Enter(Request("A")))!; active.Registered(); using var cancellation = new CancellationTokenSource();
        var pending = Enter(Request("B"), cancellation.Token); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var restarted = new ChatDeliveryService(_db, _sessions);
        var rows = await restarted.ListAsync(_user, "session", default);
        Assert.Contains(rows, row => row.CommandId == "A" && row.Status == "uncertain");
        Assert.Contains(rows, row => row.CommandId == "B" && row.QueueHeld);
        var readOnly = Request("B"); readOnly.RecoverOnly = true; readOnly.Message = "";
        Assert.Equal("held", (await restarted.EnterAsync(_user, readOnly, Ignore, default))!.Kind); Assert.Empty(_messages);
        await active.DisposeAsync();
    }

    [Fact]
    public async Task NativeBindingPersistsAcrossServiceRestartAndIsScopedBySessionAndConfiguration()
    {
        var firstRequest = Request("A"); var first = (await Enter(firstRequest))!; first.Registered();
        firstRequest.ConversationContext!.SaveThread("binding-one", "server-owned-thread");
        first.Succeeded = true; await first.DisposeAsync();
        var restarted = new ChatDeliveryService(_db, _sessions); var secondRequest = Request("B");
        var second = (await restarted.EnterAsync(_user, secondRequest, Ignore, default))!; second.Registered();
        Assert.Equal("server-owned-thread", secondRequest.ConversationContext!.LoadThread("binding-one"));
        Assert.Null(secondRequest.ConversationContext.LoadThread("different-model-or-permissions"));
        _db.Insertable(new AiChatSession { Id = "another-session", UserId = _user.Id, Title = "other" }).ExecuteCommand();
        var otherRequest = Request("C"); otherRequest.SessionId = "another-session";
        var other = (await restarted.EnterAsync(_user, otherRequest, Ignore, default))!;
        Assert.Null(otherRequest.ConversationContext!.LoadThread("binding-one"));
        await other.DisposeAsync(); await second.DisposeAsync();
    }

    [Fact]
    public async Task OwnershipAndPermissionChangesAreRecheckedBeforeExecution()
    {
        Assert.Empty(await _service.ListAsync(new("other", "other"), "session", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.EnterAsync(new("other", "other"), Request("bad"), Ignore, default));
        var active = (await Enter(Request("A")))!; active.Registered(); var pending = Enter(Request("B"));
        _db.Updateable<AiUser>().SetColumns(u => u.IsDisabled == true).Where(u => u.Id == _user.Id).ExecuteCommand();
        active.Succeeded = true; await active.DisposeAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Empty(_messages);
    }

    public class SessionsProxy : DispatchProxy
    {
        public List<string> Messages = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "RecordUserMessageAsync") { Messages.Add(((ChatCompleteRequest)args![1]!).Message); return Task.CompletedTask; }
            throw new NotSupportedException(method?.Name);
        }
    }
    public void Dispose() { _db.Dispose(); File.Delete(_path); }
}
