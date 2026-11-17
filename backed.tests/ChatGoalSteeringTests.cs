using AiAgent.Backend.Services.Chat;

namespace AiAgent.Backend.Tests;

public sealed partial class ChatGoalTests
{
    [Fact]
    public async Task QueuedCodexTurnsResumeServerOwnedConversationRatherThanCreatingNewThreads()
    {
        using var fixture = new Fixture(); using var service = fixture.Service();
        var threads = new Dictionary<string, string>();
        CodexConversationContext Context() => new() { LoadThread = binding => threads.GetValueOrDefault(binding), SaveThread = (binding, thread) => threads[binding] = thread };
        var first = fixture.Request(); first.ConversationContext = Context(); first.TurnControl = new();
        await service.CompleteAsync(first, null, default);
        var second = fixture.Request(); second.Message = "follow-up"; second.ConversationContext = Context(); second.TurnControl = new();
        await service.CompleteAsync(second, null, default);
        var calls = fixture.Calls();
        var start = Assert.Single(calls, call => call.GetProperty("method").GetString() == "thread/start");
        Assert.False(start.GetProperty("params").GetProperty("ephemeral").GetBoolean());
        var resume = Assert.Single(calls, call => call.GetProperty("method").GetString() == "thread/resume");
        Assert.Equal("persistent-thread", resume.GetProperty("params").GetProperty("threadId").GetString());
        Assert.Equal(2, calls.Count(call => call.GetProperty("method").GetString() == "turn/start"));
        Assert.Single(threads);
        var changedScope = fixture.Request(); changedScope.KnowledgeScopeUri = "viking://resources/restricted/"; changedScope.ConversationContext = Context(); changedScope.TurnControl = new();
        await service.CompleteAsync(changedScope, null, default);
        Assert.Equal(2, threads.Count);
        Assert.Equal(2, fixture.Calls().Count(call => call.GetProperty("method").GetString() == "thread/start"));
    }

    [Fact]
    public async Task OrdinaryCodexTurnSteersWithExpectedTurnIdUsingOnlyOneStdoutReader()
    {
        using var fixture = new Fixture("hang"); using var service = fixture.Service();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = fixture.Request(); request.TurnControl = new();
        var running = service.CompleteAsync(request, null, cancellation.Token);
        await Ready(request.TurnControl, cancellation.Token);
        await request.TurnControl.SteerAsync("additional instruction", cancellation.Token);
        var result = await running.WaitAsync(cancellation.Token);
        Assert.Contains("steered:additional instruction", result.Content);
        var calls = fixture.Calls();
        Assert.Single(calls, call => call.GetProperty("method").GetString() == "turn/start");
        var steer = Assert.Single(calls, call => call.GetProperty("method").GetString() == "turn/steer").GetProperty("params");
        Assert.Equal("t1", steer.GetProperty("expectedTurnId").GetString());
        Assert.Equal("persistent-thread", steer.GetProperty("threadId").GetString());
        Assert.False(request.TurnControl.Ready);
    }

    [Fact]
    public async Task ConcurrentSteeringRequestsAreSerializedAndBothReceiveTheirOwnAcknowledgement()
    {
        using var fixture = new Fixture("steer-two"); using var service = fixture.Service();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = fixture.Request(); request.TurnControl = new();
        // The fixture keeps the initial turn open in this mode.
        fixture.Mode("hang"); var running = service.CompleteAsync(request, null, cancellation.Token);
        await Ready(request.TurnControl, cancellation.Token); fixture.Mode("steer-two");
        await Task.WhenAll(request.TurnControl.SteerAsync("one", cancellation.Token), request.TurnControl.SteerAsync("two", cancellation.Token));
        var result = await running.WaitAsync(cancellation.Token);
        Assert.Contains("steered:one", result.Content); Assert.Contains("steered:two", result.Content);
        Assert.Equal(2, fixture.Calls().Count(call => call.GetProperty("method").GetString() == "turn/steer"));
    }

    [Fact]
    public async Task RejectedSteeringDoesNotConsumeAnotherRpcResponseOrAbortTheMainTurn()
    {
        using var fixture = new Fixture("hang"); using var service = fixture.Service();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = fixture.Request(); request.TurnControl = new();
        var running = service.CompleteAsync(request, null, cancellation.Token);
        await Ready(request.TurnControl, cancellation.Token); fixture.Mode("steer-reject");
        await Assert.ThrowsAnyAsync<Exception>(() => request.TurnControl.SteerAsync("rejected", cancellation.Token));
        Assert.False(running.IsCompleted); Assert.True(request.TurnControl.Ready);
        fixture.Mode("hang"); await request.TurnControl.SteerAsync("accepted", cancellation.Token);
        Assert.Contains("steered:accepted", (await running.WaitAsync(cancellation.Token)).Content);
    }

    private static async Task Ready(CodexTurnControl control, CancellationToken ct)
    {
        while (!control.Ready) await Task.Delay(10, ct);
    }
}
