using AiAgent.Backend.Services.AgentRuntime;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Tests.AgentRuntime;

public sealed class RuntimeExecutionContextsTests
{
    [Fact]
    public async Task NativeRuntime_EmitsTurnAndStepLedgerEventsAroundLegacyLoop()
    {
        var runtime = new NativeAgentRuntime(new StubLegacyLoop(), new StubToolDispatcher());
        var events = new List<RuntimeEvent>();

        var result = await runtime.ExecuteAsync(CreateRequest(), (runtimeEvent, _) =>
        {
            events.Add(runtimeEvent);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Contains(events, runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.TurnStarted);
        Assert.Equal(1, events.Count(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.StepStarted));
        Assert.Equal(1, events.Count(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.StepCompleted));
        Assert.True(events.FindIndex(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.StepCompleted)
            < events.FindIndex(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.TurnCompleted));
        var stepStarted = Assert.Single(events.Where(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.StepStarted));
        Assert.Equal("turn-1", stepStarted.Metadata!["turn_id"]);
        Assert.Equal(1, stepStarted.Metadata["tool_count"]);
        Assert.DoesNotContain("memory", stepStarted.Metadata.Keys);
    }

    [Fact]
    public void StepContext_UsesStableToolSnapshotAndSafeMetadata()
    {
        var request = CreateRequest();
        var turn = RuntimeTurnContext.Create(request);
        var tools = new[]
        {
            new ToolDefinition { Name = "search", Description = "Search", Parameters = { new ToolParameter { Name = "query", Type = "string" } } },
            new ToolDefinition { Name = "read", Description = "Read" }
        };

        var first = RuntimeStepContext.Capture(turn, request, 1, tools);
        var second = RuntimeStepContext.Capture(turn, request, 2, tools.Reverse().ToArray());

        Assert.Equal(first.ToolSnapshotId, second.ToolSnapshotId);
        Assert.Equal("turn-1", first.ToSafeMetadata()["turn_id"]);
        Assert.Equal(2, first.ToSafeMetadata()["tool_count"]);
        Assert.False(first.ToSafeMetadata().ContainsKey("memory"));
    }

    [Theory]
    [InlineData(TurnRunStatus.WaitingApproval)]
    [InlineData(TurnRunStatus.WaitingInput)]
    public void RunStateMachine_AllowsWaitingRunsToResume(TurnRunStatus waitingStatus)
    {
        Assert.True(RunStateMachine.CanTransition(TurnRunStatus.Running, waitingStatus));
        Assert.True(RunStateMachine.CanTransition(waitingStatus, TurnRunStatus.Running));
    }

    [Fact]
    public void RunStateMachine_RejectsTerminalResume()
    {
        Assert.False(RunStateMachine.CanTransition(TurnRunStatus.Completed, TurnRunStatus.Running));
    }

    private static RuntimeTurnRequest CreateRequest() => new(
        "run-1",
        "user-1",
        "thread-1",
        RuntimeKind.Native,
        "model-1",
        new RuntimeTurnInput("hello", "chat", []),
        new RuntimeCapabilitySnapshot([], [], null, null, null, 5, "memory", "project", "markdown", "index"),
        new Dictionary<string, object?>())
    {
        TurnId = "turn-1"
    };

    private sealed class StubToolDispatcher : IToolDispatcher
    {
        public IReadOnlyList<ToolDefinition> GetDefinitions(AgentContext context) => [new ToolDefinition { Name = "search", Description = "Search" }];
        public Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken) => Task.FromResult(new ToolDispatchOutcome());
    }

    private sealed class StubLegacyLoop : IAgentLoop
    {
        public Task<AgentLoopOutcome> RunAsync(AgentContext context, CancellationToken cancellationToken) => RunStreamingAsync(context, null, cancellationToken);

        public async Task<AgentLoopOutcome> RunStreamingAsync(AgentContext context, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
        {
            if (onEvent is not null)
            {
                await onEvent(new AgentStreamEvent { Type = "loop", Metadata = new Dictionary<string, object?> { ["iteration"] = 1 } }, cancellationToken);
                await onEvent(new AgentStreamEvent { Type = "done", Content = "answer" }, cancellationToken);
            }

            return new AgentLoopOutcome { Query = context.UserMessage, Answer = "answer", Completed = true };
        }
    }
}
