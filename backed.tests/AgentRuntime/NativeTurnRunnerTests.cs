using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.AgentRuntime;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Llm;
using System.Net.Http;

namespace AiAgent.Backend.Tests.AgentRuntime;

public sealed class NativeTurnRunnerTests
{
    [Fact]
    public void DeduplicateCitations_UsesSourceLocationButRetainsDistinctChunks()
    {
        var duplicate = new KnowledgeCitationDto
        {
            Text = "same evidence",
            Metadata = { ["source"] = "ai_knowledge_chunk", ["document_id"] = 42, ["chunk_no"] = 3 }
        };
        var citations = NativeTurnRunner.DeduplicateCitations(
        [
            duplicate,
            new KnowledgeCitationDto { Text = "same evidence again", Metadata = { ["source"] = "ai_knowledge_chunk", ["document_id"] = 42, ["chunk_no"] = 3 } },
            new KnowledgeCitationDto { Text = "next chunk", Metadata = { ["source"] = "ai_knowledge_chunk", ["document_id"] = 42, ["chunk_no"] = 4 } }
        ]);

        Assert.Equal(2, citations.Count);
        Assert.Same(duplicate, citations[0]);
        Assert.Equal("next chunk", citations[1].Text);
    }

    [Fact]
    public void ToolExecutionPolicy_ParallelizesOnlySafeReadTools()
    {
        Assert.True(NativeToolExecutionPolicy.CanExecuteInParallel(
        [
            new ToolCall { Name = AgentToolNames.RagSearch },
            new ToolCall { Name = AgentToolNames.CodeSearch }
        ]));
        Assert.False(NativeToolExecutionPolicy.CanExecuteInParallel(
        [
            new ToolCall { Name = AgentToolNames.RagSearch },
            new ToolCall { Name = AgentToolNames.ApplyDashboardPatch }
        ]));
    }

    [Fact]
    public async Task Router_RejectsToolOutsideTheModelVisiblePlan()
    {
        var dispatcher = new StubToolDispatcher();
        var router = new NativeToolRouter(dispatcher);
        var context = CreateContext();
        var execution = await router.ExecuteAsync(context, router.BuildPlan(context), new ToolCall { Id = "call-denied", Name = "not_visible" }, CancellationToken.None);

        Assert.False(execution.Result.Success);
        Assert.Equal(0, dispatcher.CallCount);
    }

    [Fact]
    public async Task Router_ConvertsOwnTimeoutToStructuredToolFailure()
    {
        var dispatcher = new SlowToolDispatcher();
        var router = new NativeToolRouter(dispatcher, TimeSpan.FromMilliseconds(1));
        var context = CreateContext();

        var execution = await router.ExecuteAsync(context, router.BuildPlan(context), new ToolCall { Id = "call-timeout", Name = "search" }, CancellationToken.None);

        Assert.False(execution.Result.Success);
        Assert.Contains("timed out", execution.Result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Router_RetriesOneTransientFailureForReadOnlyTool()
    {
        var dispatcher = new FlakyReadToolDispatcher();
        var router = new NativeToolRouter(dispatcher, TimeSpan.FromSeconds(1));
        var context = CreateContext();

        var execution = await router.ExecuteAsync(context, router.BuildPlan(context), new ToolCall { Id = "call-retry", Name = "search" }, CancellationToken.None);

        Assert.True(execution.Result.Success);
        Assert.Equal(2, dispatcher.CallCount);
    }

    [Fact]
    public async Task Router_DoesNotRetryTransientFailureForWriteTool()
    {
        var dispatcher = new FlakyWriteToolDispatcher();
        var router = new NativeToolRouter(dispatcher, TimeSpan.FromSeconds(1));
        var context = CreateContext();

        await Assert.ThrowsAsync<HttpRequestException>(() => router.ExecuteAsync(context, router.BuildPlan(context), new ToolCall { Id = "call-write", Name = AgentToolNames.WriteDashboardFile }, CancellationToken.None));

        Assert.Equal(1, dispatcher.CallCount);
    }

    [Fact]
    public async Task RunAsync_UsesNativeToolCallThenCompletesWithAssistantMessage()
    {
        var dispatcher = new StubToolDispatcher();
        var runner = new NativeTurnRunner(new StubLlm(), new NativeToolRouter(dispatcher), new StubHistoryStore());
        var events = new List<RuntimeEvent>();

        var result = await runner.RunAsync(CreateRequest(), CreateContext(), (runtimeEvent, _) =>
        {
            events.Add(runtimeEvent);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("native answer", result.Answer);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Equal(2, events.Count(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.ProviderRequestStarted));
        Assert.Equal(2, events.Count(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.UsageUpdated));
        Assert.True(result.Usage.PromptTokens > 0);
        Assert.True(result.Usage.CompletionTokens > 0);
        Assert.Contains(events, runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.ToolCallStarted && runtimeEvent.ItemId == "call-1");
        Assert.Contains(events, runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.ToolCallCompleted && runtimeEvent.ItemId == "call-1");
        Assert.True(events.FindIndex(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.ToolCallCompleted)
            < events.FindIndex(runtimeEvent => runtimeEvent.Kind == RuntimeEventKind.TurnCompleted));
    }

    [Fact]
    public async Task RunAsync_ReturnsDuplicateSemanticToolCallToModelWithoutDispatchingAgain()
    {
        var dispatcher = new StubToolDispatcher();
        var runner = new NativeTurnRunner(new RepeatingToolLlm(), new NativeToolRouter(dispatcher), new StubHistoryStore());

        var result = await runner.RunAsync(CreateRequest(), CreateContext(), null, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("duplicate handled", result.Answer);
        Assert.Equal(1, dispatcher.CallCount);
    }

    private static RuntimeTurnRequest CreateRequest() => new(
        "run-1", "user-1", "thread-1", RuntimeKind.Native, "model-1",
        new RuntimeTurnInput("Find evidence", "chat", []),
        new RuntimeCapabilitySnapshot([], [], null, null, null, 5, "", "", "", ""),
        new Dictionary<string, object?>()) { TurnId = "turn-1" };

    private static AgentContext CreateContext() => new() { RuntimeUserId = "user-1", SessionId = "thread-1", UserMessage = "Find evidence", ModelId = "model-1" };

    private sealed class StubToolDispatcher : IToolDispatcher
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ToolDefinition> GetDefinitions(AgentContext context) => [new ToolDefinition { Name = "search", Description = "Search evidence" }];
        public Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken)
        {
            CallCount += toolCalls.Count;
            return Task.FromResult(new ToolDispatchOutcome { Results = [new ToolResult { Content = "evidence", Success = true }] });
        }
    }

    private sealed class StubLlm : ILlmChatClient
    {
        private int _requests;
        public Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken) => Task.FromResult(new LlmChatResult());
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new LlmStreamChunk { Content = "unused" };
        }
        public async IAsyncEnumerable<LlmStreamChunk> StreamWithToolsAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition> tools, string? modelId, CancellationToken cancellationToken)
        {
            _requests++;
            await Task.CompletedTask;
            if (_requests == 1)
            {
                yield return new LlmStreamChunk { ToolCallDeltas = [new LlmToolCallDelta { Index = 0, Id = "call-1", Name = "search", ArgumentsDelta = "{}" }] };
                yield break;
            }
            yield return new LlmStreamChunk { Content = "native answer" };
        }
    }

    private sealed class RepeatingToolLlm : ILlmChatClient
    {
        private int _requests;
        public Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken) => Task.FromResult(new LlmChatResult());
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new LlmStreamChunk();
        }
        public async IAsyncEnumerable<LlmStreamChunk> StreamWithToolsAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition> tools, string? modelId, CancellationToken cancellationToken)
        {
            _requests++;
            await Task.CompletedTask;
            if (_requests < 3)
            {
                yield return new LlmStreamChunk
                {
                    ToolCallDeltas = [new LlmToolCallDelta { Index = 0, Id = $"call-{_requests}", Name = "search", ArgumentsDelta = "{}" }]
                };
                yield break;
            }
            yield return new LlmStreamChunk { Content = "duplicate handled" };
        }
    }

    private sealed class SlowToolDispatcher : IToolDispatcher
    {
        public IReadOnlyList<ToolDefinition> GetDefinitions(AgentContext context) => [new ToolDefinition { Name = "search" }];
        public async Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return new ToolDispatchOutcome();
        }
    }

    private sealed class FlakyReadToolDispatcher : IToolDispatcher
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ToolDefinition> GetDefinitions(AgentContext context) => [new ToolDefinition { Name = "search" }];
        public Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1) throw new HttpRequestException("temporary provider outage");
            return Task.FromResult(new ToolDispatchOutcome { Results = [new ToolResult { Success = true, Content = "retried evidence" }] });
        }
    }

    private sealed class FlakyWriteToolDispatcher : IToolDispatcher
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ToolDefinition> GetDefinitions(AgentContext context) => [new ToolDefinition { Name = AgentToolNames.WriteDashboardFile }];
        public Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException("temporary write endpoint failure");
        }
    }

    private sealed class StubHistoryStore : INativeThreadHistoryStore
    {
        public NativeThreadHistory Load(RuntimeTurnRequest request) => new([], false);
    }
}
