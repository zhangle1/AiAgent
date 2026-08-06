using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Usage;
using AiAgent.Backend.Services.Memory;
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

    private readonly IChatOrchestrator _orchestrator;
    private readonly IAuthService _authService;
    private readonly IChatSessionService _sessions;
    private readonly IChatImageAttachmentService _attachments;
    private readonly IUsageStatisticsService _usage;
    private readonly IMemoryService _memory;
    private readonly IProjectReferenceContextService _projectReferences;
    private readonly IMarkdownDocumentReferenceContextService _markdownDocuments;
    private readonly ICodexModelPolicyService _codexModelPolicy;
    private readonly IImageOcrPolicyService _imageOcrPolicy;

    /// <summary>
    /// Creates the WebSocket chat handler.
    /// </summary>
    public ChatWebSocketHandler(IChatOrchestrator orchestrator, IAuthService authService, IChatSessionService sessions, IChatImageAttachmentService attachments, IUsageStatisticsService usage, IMemoryService memory, IProjectReferenceContextService projectReferences, IMarkdownDocumentReferenceContextService markdownDocuments, ICodexModelPolicyService codexModelPolicy, IImageOcrPolicyService imageOcrPolicy)
    {
        _orchestrator = orchestrator;
        _authService = authService;
        _sessions = sessions;
        _attachments = attachments;
        _usage = usage;
        _memory = memory;
        _projectReferences = projectReferences;
        _markdownDocuments = markdownDocuments;
        _codexModelPolicy = codexModelPolicy;
        _imageOcrPolicy = imageOcrPolicy;
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

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var cancellationToken = cancellationSource.Token;
        Task? clientCloseMonitor = null;

        try
        {
            var requestText = await ReceiveTextAsync(socket, cancellationToken);
            clientCloseMonitor = MonitorClientCloseAsync(socket, cancellationSource, cancellationToken);
            var request = JsonSerializer.Deserialize<ChatCompleteRequest>(requestText, JsonOptions)
                ?? throw new InvalidOperationException("Invalid chat request.");
            var user = await _authService.TryGetCurrentUserAsync(context, cancellationToken)
                ?? throw new UnauthorizedAccessException();
            request.RuntimeUserId = user.Id;
            if (request.AttachmentIds.Count > 0)
            {
                if (!string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Image attachments are currently supported only when Codex local agent is selected.");
                }
                var resolvedModel = _codexModelPolicy.ResolveModel(request.CodexModelId, request.CodexReasoningEffort);
                var imagePolicy = _imageOcrPolicy.GetPolicy();
                var imageEnabled = resolvedModel.IsBuiltin
                    ? imagePolicy.NativeImageInputEnabled
                    : resolvedModel.SupportsImageOcr && imagePolicy.Enabled;
                if (!imageEnabled)
                {
                    throw new InvalidOperationException(resolvedModel.IsBuiltin
                        ? "The administrator has disabled native Codex image input."
                        : "The selected Codex profile does not have PaddleOCR image recognition enabled.");
                }
                request.LocalImagePaths = (await _attachments.ResolveLocalAttachmentsAsync(user, request.SessionId, request.AttachmentIds, cancellationToken)).Select(item => item.LocalPath).ToList();
            }
            await _projectReferences.ResolveAsync(user, request, cancellationToken);
            await _markdownDocuments.ResolveAsync(user, request, cancellationToken);
            await _sessions.RecordUserMessageAsync(user, request, cancellationToken);
            request.ServerMemoryContext = await _memory.BuildPromptContextAsync(user, request, cancellationToken);
            var content = new StringBuilder();
            var thinking = new StringBuilder();
            object? citations = null;
            string? modelId = null;
            string? model = null;

            var result = await _orchestrator.CompleteStreamingAsync(request, async (streamEvent, token) =>
            {
                if (streamEvent.Type == "content") content.Append(streamEvent.Content);
                if (streamEvent.Type == "thinking") thinking.Append(streamEvent.Content);
                if (streamEvent.Type == "sources") citations = streamEvent.Citations;
                modelId ??= streamEvent.ModelId;
                model ??= streamEvent.Model;
                await SendEventAsync(socket, streamEvent, token);
            }, cancellationToken);
            var finalContent = content.Length > 0 ? content.ToString() : result.Content;
            var finalModelId = modelId ?? result.ModelId;
            var finalModel = model ?? result.Model;
            await _sessions.RecordAssistantMessageAsync(user, request, finalContent, thinking.ToString(), citations ?? result.Citations, finalModelId, finalModel, cancellationToken);
            await _usage.RecordAsync(user, request, result, cancellationToken);
            await SendEventAsync(socket, new AgentStreamEvent { Type = "completed" }, cancellationToken);

            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "cancelled", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            await SendEventAsync(socket, new AgentStreamEvent
            {
                Type = "error",
                Content = ex.Message
            }, CancellationToken.None);

            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "error", CancellationToken.None);
            }
        }
        finally
        {
            cancellationSource.Cancel();
            if (clientCloseMonitor != null)
            {
                try { await clientCloseMonitor; } catch (OperationCanceledException) { }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The response completed normally or the client requested cancellation.
        }
        catch (WebSocketException)
        {
            // A dropped browser connection must stop the agent just like an explicit stop click.
            cancellationSource.Cancel();
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
}
