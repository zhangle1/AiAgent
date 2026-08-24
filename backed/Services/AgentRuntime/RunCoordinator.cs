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
}

public sealed class RunCoordinator : IRunCoordinator
{
    private readonly IReadOnlyDictionary<RuntimeKind, IRuntimeEngine> _engines;
    private readonly ConcurrentDictionary<string, TurnRunSnapshot> _runs = new(StringComparer.Ordinal);

    public RunCoordinator(IEnumerable<IRuntimeEngine> engines)
    {
        _engines = engines.ToDictionary(x => x.Kind);
    }

    public bool TryGetRun(string runId, out TurnRunSnapshot? snapshot) => _runs.TryGetValue(runId, out snapshot);

    public async Task<RuntimeTurnResult> RunAsync(
        RuntimeTurnRequest request,
        RuntimeEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        if (!_engines.TryGetValue(request.RuntimeKind, out var engine))
            throw new InvalidOperationException($"Runtime engine '{request.RuntimeKind}' is not registered.");
        if (!_runs.TryAdd(request.RunId, CreateSnapshot(request, engine)))
            throw new InvalidOperationException($"Run '{request.RunId}' already exists.");

        long sequence = 0;
        async Task EmitStatus(TurnRunStatus status, string? error = null)
        {
            Transition(request.RunId, status, error);
            if (onEvent is not null)
                await onEvent(RuntimeEventProjector.New(request.RunId, Interlocked.Increment(ref sequence), RuntimeEventKind.RunStatusChanged, status: status), CancellationToken.None);
        }

        try
        {
            await EmitStatus(TurnRunStatus.Starting);
            await EmitStatus(TurnRunStatus.Running);
            var result = await engine.ExecuteAsync(request, async (runtimeEvent, token) =>
            {
                if (onEvent is null) return;
                var normalized = runtimeEvent with { Sequence = Interlocked.Increment(ref sequence) };
                await onEvent(normalized, token);
            }, cancellationToken);
            await EmitStatus(TurnRunStatus.Completed);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

        async Task RecordTerminalStatus(TurnRunStatus status, string? error = null)
        {
            Transition(request.RunId, status, error);
            if (onEvent is null) return;
            try
            {
                await onEvent(RuntimeEventProjector.New(request.RunId, Interlocked.Increment(ref sequence), RuntimeEventKind.RunStatusChanged, status: status), CancellationToken.None);
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
        return _runs.TryGetValue(runId, out var current) && IsAllowed(current.Status, next);
    }

    private static bool IsAllowed(TurnRunStatus current, TurnRunStatus next) => (current, next) switch
    {
        (TurnRunStatus.Queued, TurnRunStatus.Starting) => true,
        (TurnRunStatus.Starting, TurnRunStatus.Running) => true,
        (TurnRunStatus.Running, TurnRunStatus.Completed or TurnRunStatus.Failed or TurnRunStatus.Cancelled or TurnRunStatus.WaitingApproval or TurnRunStatus.WaitingInput) => true,
        (TurnRunStatus.Starting, TurnRunStatus.Failed or TurnRunStatus.Cancelled) => true,
        _ => false
    };
}
