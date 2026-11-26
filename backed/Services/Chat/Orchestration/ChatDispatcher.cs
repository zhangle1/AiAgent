using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat.Orchestration;

public interface IChatDispatcher
{
    Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request,
        Func<CancellationToken, Task<AuthenticatedUser>> authenticate, CancellationToken token);
    Task<bool> CancelAsync(AuthenticatedUser user, string sessionId, string commandId, CancellationToken token);
    Task DispatchStreamAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatDebugTrace? trace,
        AgentStreamEventHandler send, Func<CancellationToken, Task<AuthenticatedUser>> refreshUser, CancellationToken connection);
}

internal sealed class ChatDispatcher(ChatTurnExecutor turns, IChatSessionService sessions, ChatCommandStore commands,
    ChatDeliveryService? deliveries = null, ChatDeliveryWorker? worker = null) : IChatDispatcher
{
    public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request,
        Func<CancellationToken, Task<AuthenticatedUser>> authenticate, CancellationToken token) =>
        turns.CompleteAsync(request, authenticate, token);

    public async Task<bool> CancelAsync(AuthenticatedUser user, string sessionId, string commandId, CancellationToken token)
    {
        if (deliveries is not null && await deliveries.CancelAsync(user, sessionId, commandId, token)) return true;
        var request = new ChatCompleteRequest { CommandId = commandId, SessionId = sessionId, RuntimeUserId = user.Id };
        return commands.Cancel(request, user);
    }

    public Task DispatchStreamAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatDebugTrace? trace,
        AgentStreamEventHandler send, Func<CancellationToken, Task<AuthenticatedUser>> refreshUser, CancellationToken connection)
    {
        request.RuntimeUserId = user.Id;
        request.RuntimeUsername = user.Username;
        request = deliveries?.Restore(user, request) ?? request;
        sessions.PrepareCommandSession(user, request);
        if (worker is not null && request.Goal is null && !request.RecoverOnly && request.DeliveryIntent is not null)
            return worker.SubmitAndObserveAsync(user, request, trace, send, connection);

        return turns.RunAsync(user, request, trace, send, refreshUser, connection);
    }
}
