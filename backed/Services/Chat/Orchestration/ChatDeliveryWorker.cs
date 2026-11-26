using System.Threading.Channels;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat.Orchestration;

/// <summary>Owns ordinary web deliveries for the lifetime of the API host. The transport owns only a reader.</summary>
internal sealed class ChatDeliveryWorker(IServiceScopeFactory scopes, ILogger<ChatDeliveryWorker> logger) : BackgroundService
{
    private sealed class ObserverLagException : Exception;

    private sealed record Submission(AuthenticatedUser User, ChatCompleteRequest Request, ChatDebugTrace? Trace,
        Channel<AgentStreamEvent> Events);

    private readonly Channel<Submission> _incoming = Channel.CreateBounded<Submission>(
        new BoundedChannelOptions(256) { SingleReader = true });

    public async Task SubmitAndObserveAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatDebugTrace? trace,
        AgentStreamEventHandler send, CancellationToken connection)
    {
        var events = Channel.CreateBounded<AgentStreamEvent>(256);
        var submission = new Submission(user, request, trace, events);
        await _incoming.Writer.WriteAsync(submission, connection);
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
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
            var deliveries = services.GetRequiredService<ChatDeliveryService>();
            Task Publish(AgentStreamEvent value, CancellationToken token)
            {
                // A slow socket cannot hold a model slot. Committed events remain available through recovery.
                if (!job.Events.Writer.TryWrite(value))
                    job.Events.Writer.TryComplete(new ObserverLagException());
                return Task.CompletedTask;
            }
            await execution.RunAsync(job.User, job.Request, job.Trace, Publish,
                token => {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(deliveries.Authorize(job.User, job.Request));
                }, stoppingToken);
        }
        catch (Exception error)
        {
            logger.LogWarning("Background chat delivery ended during bookkeeping ({ExceptionType}).", error.GetType().Name);
            job.Events.Writer.TryComplete(error);
        }
        finally { job.Events.Writer.TryComplete(); }
    }
}
