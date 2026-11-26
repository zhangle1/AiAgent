using AiAgent.Backend.Services.Chat.Orchestration;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Auth;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Chat;

/// <summary>
/// Handles chat streaming over WebSocket while reusing the existing agent event pipeline.
/// </summary>
public sealed class ChatWebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IAuthService _authService;
    private readonly IChatDispatcher _dispatcher;

    public ChatWebSocketHandler(IAuthService authService, IChatDispatcher dispatcher)
    {
        _authService = authService;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Accepts a WebSocket, reads one chat request, streams agent events, then closes the socket.
    /// </summary>
    public async Task HandleClientAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket request expected.", context.RequestAborted);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20),
            KeepAliveTimeout = TimeSpan.FromSeconds(20)
        });
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var cancellationToken = cancellationSource.Token;
        Task? clientCloseMonitor = null;
        ChatDebugTrace? trace = null;
        ChatCompleteRequest request = null!;
        AuthenticatedUser user = null!;
        var closeStatus = WebSocketCloseStatus.NormalClosure;

        try
        {
            var requestText = await ReceiveTextAsync(socket, cancellationToken);
            clientCloseMonitor = MonitorClientCloseAsync(socket, cancellationSource, cancellationToken);
            request = JsonSerializer.Deserialize<ChatCompleteRequest>(requestText, JsonOptions)
                ?? throw new InvalidOperationException("Invalid chat request.");
            trace = ChatDebugTrace.Create(request);
            await SendTraceAsync(socket, trace?.Complete("backend_received"), cancellationToken);
            await SendTraceAsync(socket, trace?.Start("auth_session_context"), cancellationToken);
            user = await _authService.TryGetCurrentUserAsync(context, cancellationToken)
                ?? throw new UnauthorizedAccessException();
            await _dispatcher.DispatchStreamAsync(user, request, trace,
                (value, token) => SendEventAsync(socket, value, token),
                async token => await _authService.TryGetCurrentUserAsync(context, token) ?? throw new UnauthorizedAccessException(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            closeStatus = WebSocketCloseStatus.InternalServerError;
            await SendEventAsync(socket, new AgentStreamEvent { Type = "error", Content = ChatExecutionErrors.ToClientError(error, request) }, cancellationToken);
        }
        finally
        {
            // Release the execution slot before waiting for the peer. The monitor owns the only read.
            try
            {
                using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(closeStatus, null, closing.Token);
                if (clientCloseMonitor is not null && socket.State == WebSocketState.CloseSent)
                    await clientCloseMonitor.WaitAsync(closing.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
                socket.Abort();
            }
            finally
            {
                cancellationSource.Cancel();
                if (clientCloseMonitor is not null) await clientCloseMonitor;
            }
        }
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new OperationCanceledException("Client closed the WebSocket before sending a request.");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("Only text WebSocket messages are supported.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static async Task MonitorClientCloseAsync(WebSocket socket, CancellationTokenSource cancellationSource, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    cancellationSource.Cancel();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
            if (!cancellationToken.IsCancellationRequested) cancellationSource.Cancel();
        }
    }

    private static async Task SendEventAsync(WebSocket socket, AgentStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(streamEvent, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static Task SendTraceAsync(WebSocket socket, AgentStreamEvent? streamEvent, CancellationToken cancellationToken)
    {
        return streamEvent is null ? Task.CompletedTask : SendEventAsync(socket, streamEvent, cancellationToken);
    }
}
