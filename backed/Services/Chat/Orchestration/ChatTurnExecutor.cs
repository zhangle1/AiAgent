using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Memory;

namespace AiAgent.Backend.Services.Chat.Orchestration;

internal sealed class ChatTurnExecutor(IChatOrchestrator orchestrator, ChatTurnPreparation preparation,
    ChatRunFinalizer finalizer, IMemoryService memory, IChatDebugTraceStore debugTraceStore,
    ILogger<ChatTurnExecutor> logger, ChatCommandStore commands, ChatDeliveryService? deliveries = null)
{
    public async Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request,
        Func<CancellationToken, Task<AuthenticatedUser>> authenticate, CancellationToken cancellationToken)
    {
        try { return await CompleteCore(request, authenticate, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase))
        {
            var failure = CodexDiagnosticException.ForRequest(exception, request);
            logger.LogError("[CodexDiagnostic] Id={DiagnosticId} Stage={Stage} Code={Code} ExceptionType={ExceptionType}", failure.DiagnosticId, failure.Stage, failure.Code, exception.GetType().Name);
            throw failure;
        }
    }

    private async Task<ChatCompleteResponse> CompleteCore(ChatCompleteRequest request,
        Func<CancellationToken, Task<AuthenticatedUser>> authenticate, CancellationToken cancellationToken)
    {
        if (request.DeliveryIntent is not null || request.ResumeQueued) throw new ChatDeliveryException("消息投递需要使用聊天流接口。");
        if (!string.IsNullOrWhiteSpace(request.CommandId)) throw new InvalidOperationException("Command IDs require the streaming endpoint.");
        var trace = ChatDebugTrace.Create(request);
        trace?.Complete("backend_received");
        trace?.Start("auth_session_context");
        var user = await authenticate(cancellationToken);
        request.RuntimeUserId = user.Id;
        request.RuntimeUsername = user.Username;
        await preparation.PrepareAndRecordUserMessageAsync(user, request, cancellationToken);
        request.ServerMemoryContext = await memory.BuildPromptContextAsync(user, request, cancellationToken);
        trace?.Complete("auth_session_context");
        trace?.Start("request_started");
        trace?.Complete("request_started");
        trace?.Start("provider_request_started");
        var result = await orchestrator.CompleteAsync(request, cancellationToken);
        trace?.CompleteProvider();
        trace?.Start("persistence");
        await finalizer.SaveAsync(user, request, result, null, cancellationToken);
        trace?.Complete("persistence");
        trace?.Start("frontend_push");
        trace?.Complete("frontend_push");
        trace?.Complete("request_completed");
        result.DebugTrace = trace?.Events.ToList();
        await debugTraceStore.SaveAsync(user, request.SessionId, trace, cancellationToken);
        return result;
    }

    internal async Task RunAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatDebugTrace? trace,
        AgentStreamEventHandler send,
        Func<CancellationToken, Task<AuthenticatedUser>> refreshUser, CancellationToken cancellationToken)
    {
        Task TraceAsync(AgentStreamEvent? value, CancellationToken token) =>
            value is null ? Task.CompletedTask : send(value, token);
        ChatCommandStore.Lease? command = null;
        ChatDeliveryService.Admission? delivery = null;
        CancellationTokenSource? deliveryCancellation = null;
        async Task Emit(AgentStreamEvent value, CancellationToken token)
        {
            command?.Append(value);
            await send(value, token);
        }

        var output = new ChatProviderOutput(trace, Emit);
        try
        {
            delivery = deliveries is null ? null : await deliveries.EnterAsync(user, request, Emit, cancellationToken, preparation.ResolveAttachmentsAsync);
            if (delivery is not null && delivery.Kind != "execute")
            {
                await Emit(new AgentStreamEvent { Type = delivery.Kind == "steered" ? "delivery_steered" : "delivery_held",
                    Content = delivery.Kind == "steered" ? "追加指令已投递给当前任务，等待 Agent 处理。" : delivery.Kind == "cancelled" ? "排队消息已取消，未执行。" : delivery.Kind == "uncertain" ? "本次投递结果待确认，请核对已有结果；不会自动重新执行。" : "排队消息已保留，等待显式恢复。" }, cancellationToken);
                return;
            }
            request.ResumeQueued = false;

            command = (request.RecoverOnly || (request.Goal == null && !string.IsNullOrWhiteSpace(request.CommandId))) ? commands.Begin(request, user) : null;
            delivery?.Registered();
            if (command is { IsOwner: false })
            {
                await command.ReplayAsync(send, cancellationToken);
                return;
            }
            if (delivery is not null)
            {
                deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, delivery.Stop);
                cancellationToken = deliveryCancellation.Token;
                user = await refreshUser(cancellationToken);
                request.RuntimeUsername = user.Username;
            }
            cancellationToken = command?.Link(cancellationToken) ?? cancellationToken;
            await Task.Yield(); // Release the dispatcher after the queue position and execution lease are known.
            await preparation.PrepareAndRecordUserMessageAsync(user, request, cancellationToken);
            await Emit(new AgentStreamEvent { Type = "session_ready" }, cancellationToken);
            if (delivery is not null) await Emit(new AgentStreamEvent { Type = "delivery_started" }, cancellationToken);
            request.ServerMemoryContext = await memory.BuildPromptContextAsync(user, request, cancellationToken);
            await TraceAsync(trace?.Complete("auth_session_context"), cancellationToken);
            await TraceAsync(trace?.Start("request_started"), cancellationToken);
            await TraceAsync(trace?.Complete("request_started"), cancellationToken);
            var result = await orchestrator.CompleteStreamingAsync(request, output.ReceiveAsync, cancellationToken);
            await output.CompleteProviderAsync(cancellationToken);
            await TraceAsync(trace?.Start("persistence"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Once finalization starts, finish history and accounting together even if Stop races it.
            if (delivery is not null) cancellationToken = CancellationToken.None;
            await finalizer.SaveAsync(user, request, result, output, cancellationToken);
            await TraceAsync(trace?.Complete("persistence"), cancellationToken);
            await TraceAsync(trace?.Start("frontend_push"), cancellationToken);
            await TraceAsync(trace?.Complete("frontend_push"), cancellationToken);
            await TraceAsync(trace?.Complete("request_completed"), cancellationToken);
            await debugTraceStore.SaveAsync(user, request.SessionId, trace, CancellationToken.None);
            await Emit(new AgentStreamEvent { Type = "completed" }, cancellationToken);
            if (delivery is not null) delivery.Succeeded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (output.ProviderStarted) await TraceAsync(trace?.CancelProvider(), CancellationToken.None);
            await TraceAsync(trace?.Cancel("request_completed"), CancellationToken.None);
            await debugTraceStore.SaveAsync(user, request.SessionId, trace, CancellationToken.None);
            if (delivery?.Stop.IsCancellationRequested == true && command is { IsOwner: true })
                await Emit(new AgentStreamEvent { Type = "cancelled", Content = "任务已停止。" }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            var clientException = exception;
            if (exception is not ChatDeliveryException && string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase))
            {
                var failure = CodexDiagnosticException.ForRequest(exception, request);
                logger.LogError("[CodexDiagnostic] Id={DiagnosticId} Stage={Stage} Code={Code} ExceptionType={ExceptionType}", failure.DiagnosticId, failure.Stage, failure.Code, exception.GetType().Name);
                await TraceAsync(trace?.ReportCodexFailure(failure), CancellationToken.None);
                clientException = failure;
            }
            if (output.ProviderStarted) await TraceAsync(trace?.FailProvider("request_failed"), CancellationToken.None);
            await TraceAsync(trace?.Fail("request_completed", "request_failed"), CancellationToken.None);
            await debugTraceStore.SaveAsync(user, request.SessionId, trace, CancellationToken.None);
            await Emit(new AgentStreamEvent
            {
                Type = "error",
                Content = ChatExecutionErrors.ToClientError(clientException, request)
            }, CancellationToken.None);
        }
        finally
        {
            try { command?.Dispose(); }
            finally { deliveryCancellation?.Dispose(); if (delivery is not null) await delivery.DisposeAsync(); }
        }
    }
}
