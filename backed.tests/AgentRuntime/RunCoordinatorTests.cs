using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.AgentRuntime;

namespace AiAgent.Backend.Tests.AgentRuntime;

public sealed class RunCoordinatorTests
{
    [Fact]
    public async Task RunAsync_CompletesWithMonotonicEventsAndVersionSnapshot()
    {
        var engine = new StubRuntimeEngine();
        var coordinator = new RunCoordinator([engine]);
        var events = new List<RuntimeEvent>();
        var request = CreateRequest("run-1");

        var result = await coordinator.RunAsync(request, (runtimeEvent, _) =>
        {
            events.Add(runtimeEvent);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal([1L, 2L, 3L, 4L], events.Select(x => x.Sequence));
        Assert.Equal(TurnRunStatus.Starting, events[0].Status);
        Assert.Equal(TurnRunStatus.Running, events[1].Status);
        Assert.Equal(RuntimeEventKind.ItemDelta, events[2].Kind);
        Assert.Equal(TurnRunStatus.Completed, events[3].Status);
        Assert.True(coordinator.TryGetRun(request.RunId, out var snapshot));
        Assert.Equal(TurnRunStatus.Completed, snapshot!.Status);
        Assert.Equal(engine.RuntimeVersion, snapshot.RuntimeVersion);
        Assert.Equal(engine.ProtocolVersion, snapshot.ProtocolVersion);
    }

    [Fact]
    public async Task RunAsync_RecordsFailureWithoutLosingEvidence()
    {
        var engine = new StubRuntimeEngine { Failure = new InvalidOperationException("engine failed") };
        var coordinator = new RunCoordinator([engine]);
        var request = CreateRequest("run-failed");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunAsync(request, null, CancellationToken.None));

        Assert.Equal("engine failed", error.Message);
        Assert.True(coordinator.TryGetRun(request.RunId, out var snapshot));
        Assert.Equal(TurnRunStatus.Failed, snapshot!.Status);
        Assert.Equal("engine failed", snapshot.Error);
    }

    [Fact]
    public async Task RunAsync_RejectsDuplicateRunId()
    {
        var engine = new StubRuntimeEngine();
        var coordinator = new RunCoordinator([engine]);
        var request = CreateRequest("same-run");
        await coordinator.RunAsync(request, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunAsync(request, null, CancellationToken.None));
    }

    private static RuntimeTurnRequest CreateRequest(string runId) => new(
        runId,
        "user-1",
        "thread-1",
        RuntimeKind.Native,
        null,
        new RuntimeTurnInput("hello", "chat", []),
        new RuntimeCapabilitySnapshot([], [], null, null, null, 5, "", "", "", ""),
        new Dictionary<string, object?>());

    private sealed class StubRuntimeEngine : IRuntimeEngine
    {
        public RuntimeKind Kind => RuntimeKind.Native;
        public string RuntimeVersion => "stub/1";
        public string ProtocolVersion => "test/1";
        public Exception? Failure { get; init; }

        public async Task<RuntimeTurnResult> ExecuteAsync(
            RuntimeTurnRequest request,
            RuntimeEventHandler? onEvent,
            CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            if (onEvent is not null)
            {
                await onEvent(RuntimeEventProjector.New(
                    request.RunId, 99, RuntimeEventKind.ItemDelta, "delta"), cancellationToken);
            }

            return new RuntimeTurnResult(
                request.Input.Content, "answer", null, "stub", null, [],
                new ChatTokenUsage { TotalTokens = 1 }, true);
        }
    }
}
