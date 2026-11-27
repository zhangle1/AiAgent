using System.Diagnostics;
using System.Threading.Channels;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat.Orchestration;

/// <summary>Owns ordinary web deliveries for the lifetime of the API host. The transport owns only a reader.</summary>
internal sealed class ChatDeliveryWorker(IServiceScopeFactory scopes, ChatDeliveryService deliveries, ILogger<ChatDeliveryWorker> logger) : BackgroundService
{
    private sealed class ObserverLagException : Exception;

    private sealed record Submission(AuthenticatedUser User, ChatCompleteRequest Request, ChatDebugTrace? Trace,
        ChatDeliveryService.Reservation Reservation, ChatDeliveryProbe? Probe, Channel<AgentStreamEvent> Events);

    private readonly Channel<Submission> _incoming = Channel.CreateBounded<Submission>(
        new BoundedChannelOptions(256) { SingleReader = true });

    public async Task SubmitAndObserveAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatDebugTrace? trace,
        AgentStreamEventHandler send, CancellationToken connection)
    {
        var start = Stopwatch.GetTimestamp();
        var reservation = await deliveries.ReserveAsync(user, request, canPrepare: true, connection)
            ?? throw new InvalidOperationException("A background submission requires a delivery.");
        var probe = ChatDeliveryProbe.Create(start, reservation.Owner, request.DebugTrace ? trace : null);
        var events = Channel.CreateBounded<AgentStreamEvent>(256);
        var submission = new Submission(user, request, trace, reservation, probe, events);
        probe?.Observe("delivery_accepted");
        events.Writer.TryWrite(reservation.Receipt());
        // Once acknowledged in the database, even a disconnect during channel backpressure
        // must leave scheduling under host ownership. The receipt is readable immediately.
        var scheduling = ScheduleAsync(submission);
        try
        {
            await foreach (var value in events.Reader.ReadAllAsync(connection))
                await send(value, connection);
        }
        catch (ObserverLagException)
        {
            // An incomplete transport stream triggers read-only recovery of this command.
        }
        finally
        {
            // Finishing the reader never revokes the execution lease.
            events.Writer.TryComplete();
            await scheduling;
        }
    }

    private async Task ScheduleAsync(Submission job)
    {
        try { await _incoming.Writer.WriteAsync(job); }
        catch (ChannelClosedException error) { job.Events.Writer.TryComplete(error); }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // SqlSugarScope resolves its client from the logical context. Start the channel reader
        // outside the host's ambient context so parallel turns never reuse its open connection.
        using (ExecutionContext.SuppressFlow())
            return Task.Run(() => ProcessQueueAsync(stoppingToken));
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        var active = new HashSet<Task>();
        try
        {
            await foreach (var submission in _incoming.Reader.ReadAllAsync(stoppingToken))
            {
                active.RemoveWhere(task => task.IsCompleted);
                active.Add(ExecuteSubmissionAsync(submission, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _incoming.Writer.TryComplete();
            while (_incoming.Reader.TryRead(out var pending))
                pending.Events.Writer.TryComplete(new OperationCanceledException(stoppingToken));
            await Task.WhenAll(active);
        }
    }

    private async Task ExecuteSubmissionAsync(Submission job, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var execution = services.GetRequiredService<ChatTurnExecutor>();
            Task Publish(AgentStreamEvent value, CancellationToken token)
            {
                // A slow socket cannot hold a model slot. Committed events remain available through recovery.
                job.Probe?.Observe(value.Type, !string.IsNullOrEmpty(value.Content));
                if (!job.Events.Writer.TryWrite(value))
                    job.Events.Writer.TryComplete(new ObserverLagException());
                return Task.CompletedTask;
            }
            await execution.RunAsync(job.User, job.Request, job.Trace, Publish, refreshUser: null, stoppingToken, job.Reservation);
        }
        catch (Exception error)
        {
            logger.LogWarning("Background chat delivery ended during bookkeeping ({ExceptionType}).", error.GetType().Name);
            job.Events.Writer.TryComplete(error);
        }
        finally { job.Events.Writer.TryComplete(); }
    }
}
