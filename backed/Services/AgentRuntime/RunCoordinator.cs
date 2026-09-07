using System.Collections.Concurrent;

namespace AiAgent.Backend.Services.AgentRuntime;

public sealed record TurnRunSnapshot(
    string RunId,
    string ThreadId,
    RuntimeKind RuntimeKind,
    string RuntimeVersion,
    string ProtocolVersion,
    TurnRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error);

public interface IRunCoordinator
{
    Task<RuntimeTurnResult> RunAsync(RuntimeTurnRequest request, RuntimeEventHandler? onEvent, CancellationToken cancellationToken);
    bool TryGetRun(string runId, out TurnRunSnapshot? snapshot);
    bool Cancel(string runId, string userId);
}

public sealed class RunCoordinator : IRunCoordinator
{
    private readonly IReadOnlyDictionary<RuntimeKind, IRuntimeEngine> _engines;
    private readonly ConcurrentDictionary<string, TurnRunSnapshot> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string UserId, CancellationTokenSource Source)> _cancellations = new(StringComparer.Ordinal);
    private readonly IAgentRunStore _store;

    public RunCoordinator(IEnumerable<IRuntimeEngine> engines, IAgentRunStore? store = null)
    {
        _engines = engines.ToDictionary(x => x.Kind);
        _store = store ?? NullAgentRunStore.Instance;
    }

    public bool TryGetRun(string runId, out TurnRunSnapshot? snapshot) => _runs.TryGetValue(runId, out snapshot);

    public bool Cancel(string runId, string userId)
    {
        if (!_cancellations.TryGetValue(runId, out var active) || active.UserId != userId) return false;
        active.Source.Cancel();
        return true;
    }

    public async Task<RuntimeTurnResult> RunAsync(
        RuntimeTurnRequest request,
        RuntimeEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        if (!_engines.TryGetValue(request.RuntimeKind, out var engine))
            throw new InvalidOperationException($"Runtime engine '{request.RuntimeKind}' is not registered.");
        if (!_runs.TryAdd(request.RunId, CreateSnapshot(request, engine)))
            throw new InvalidOperationException($"Run '{request.RunId}' already exists.");
        _store.Create(request, engine);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_cancellations.TryAdd(request.RunId, (request.UserId, linkedCancellation)))
            throw new InvalidOperationException($"Run '{request.RunId}' cancellation lease already exists.");

        long sequence = 0;
        async Task EmitStatus(TurnRunStatus status, string? error = null)
        {
            Transition(request.RunId, status, error);
            _store.UpdateStatus(request.RunId, status, error);
            var statusEvent = RuntimeEventProjector.New(request.RunId, Interlocked.Increment(ref sequence), RuntimeEventKind.RunStatusChanged, status: status);
            _store.Append(statusEvent);
            if (onEvent is not null) await onEvent(statusEvent, CancellationToken.None);
        }

        try
        {
            await EmitStatus(TurnRunStatus.Starting);
            await EmitStatus(TurnRunStatus.Running);
            var result = await engine.ExecuteAsync(request, async (runtimeEvent, token) =>
            {
                var normalized = runtimeEvent with { Sequence = Interlocked.Increment(ref sequence) };
                _store.Append(normalized);
                if (onEvent is not null) await onEvent(normalized, token);
            }, linkedCancellation.Token);
            _store.Complete(request.RunId, result);
            await EmitStatus(TurnRunStatus.Completed);
            return result;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            if (CanTransition(request.RunId, TurnRunStatus.Cancelled))
                await RecordTerminalStatus(TurnRunStatus.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            if (CanTransition(request.RunId, TurnRunStatus.Failed))
                await RecordTerminalStatus(TurnRunStatus.Failed, exception.Message);
            throw;
        }
        finally
        {
            _cancellations.TryRemove(request.RunId, out _);
        }

        async Task RecordTerminalStatus(TurnRunStatus status, string? error = null)
        {
            Transition(request.RunId, status, error);
            _store.UpdateStatus(request.RunId, status, error);
            var statusEvent = RuntimeEventProjector.New(request.RunId, Interlocked.Increment(ref sequence), RuntimeEventKind.RunStatusChanged, status: status);
            _store.Append(statusEvent);
            if (onEvent is null) return;
            try
            {
                await onEvent(statusEvent, CancellationToken.None);
            }
            catch
            {
                // Preserve the original engine/cancellation exception and the terminal ledger state.
            }
        }
    }

    private static TurnRunSnapshot CreateSnapshot(RuntimeTurnRequest request, IRuntimeEngine engine)
    {
        var now = DateTimeOffset.UtcNow;
        return new TurnRunSnapshot(request.RunId, request.ThreadId, request.RuntimeKind,
            engine.RuntimeVersion, engine.ProtocolVersion, TurnRunStatus.Queued, now, now, null);
    }

    private void Transition(string runId, TurnRunStatus next, string? error)
    {
        _runs.AddOrUpdate(runId,
            _ => throw new InvalidOperationException($"Run '{runId}' was not queued."),
            (_, current) => IsAllowed(current.Status, next)
                ? current with { Status = next, UpdatedAt = DateTimeOffset.UtcNow, Error = error }
                : throw new InvalidOperationException($"Invalid run transition: {current.Status} -> {next}."));
    }

    private bool CanTransition(string runId, TurnRunStatus next)
    {
        return _runs.TryGetValue(runId, out var current) && RunStateMachine.CanTransition(current.Status, next);
    }

    private sealed class NullAgentRunStore : IAgentRunStore
    {
        public static readonly NullAgentRunStore Instance = new();
        public void Create(RuntimeTurnRequest request, IRuntimeEngine engine) { }
        public void Append(RuntimeEvent runtimeEvent) { }
        public void UpdateStatus(string runId, TurnRunStatus status, string? errorCode = null) { }
        public void Complete(string runId, RuntimeTurnResult result) { }
        public IReadOnlyList<AgentRunSummaryDto> List(AiAgent.Backend.Services.Auth.AuthenticatedUser user, string sessionId, int limit) => [];
        public AgentRunDetailDto? Get(AiAgent.Backend.Services.Auth.AuthenticatedUser user, string runId) => null;
    }

    private static bool IsAllowed(TurnRunStatus current, TurnRunStatus next) => RunStateMachine.CanTransition(current, next);
}
