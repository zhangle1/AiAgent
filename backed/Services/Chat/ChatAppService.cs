using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Usage;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Memory;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Chat;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/chat")]
public sealed class ChatAppService : IDynamicApiController
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IChatOrchestrator _orchestrator;
    private readonly IAuthService _authService;
    private readonly IChatSessionService _sessions;
    private readonly IChatImageAttachmentService _attachments;
    private readonly IChatFileAttachmentService _fileAttachments;
    private readonly IChatUploadLibraryService _uploadLibrary;
    private readonly IUsageStatisticsService _usage;
    private readonly IMemoryService _memory;
    private readonly IProjectReferenceContextService _projectReferences;
    private readonly IMarkdownDocumentReferenceContextService _markdownDocuments;
    private readonly IProjectAgentMarkdownIndexContextService _projectAgentMarkdownIndex;
    private readonly ICodexChatService _codex;
    private readonly ICodexModelPolicyService _codexModelPolicy;
    private readonly IImageOcrPolicyService _imageOcrPolicy;
    private readonly IChatDebugTraceStore _debugTraceStore;

    /// <summary>
    /// 初始化聊天 API 服务。
    /// </summary>
    public ChatAppService(IHttpContextAccessor httpContextAccessor, IChatOrchestrator orchestrator, IAuthService authService, IChatSessionService sessions, IChatImageAttachmentService attachments, IChatFileAttachmentService fileAttachments, IChatUploadLibraryService uploadLibrary, IUsageStatisticsService usage, IMemoryService memory, IProjectReferenceContextService projectReferences, IMarkdownDocumentReferenceContextService markdownDocuments, IProjectAgentMarkdownIndexContextService projectAgentMarkdownIndex, ICodexChatService codex, ICodexModelPolicyService codexModelPolicy, IImageOcrPolicyService imageOcrPolicy, IChatDebugTraceStore debugTraceStore)
    {
        _httpContextAccessor = httpContextAccessor;
        _orchestrator = orchestrator;
        _authService = authService;
        _sessions = sessions;
        _attachments = attachments;
        _fileAttachments = fileAttachments;
        _uploadLibrary = uploadLibrary;
        _usage = usage;
        _memory = memory;
        _projectReferences = projectReferences;
        _markdownDocuments = markdownDocuments;
        _projectAgentMarkdownIndex = projectAgentMarkdownIndex;
        _codex = codex;
        _codexModelPolicy = codexModelPolicy;
        _imageOcrPolicy = imageOcrPolicy;
        _debugTraceStore = debugTraceStore;
    }

    /// <summary>
    /// 执行一次聊天完成：创建 Agent 上下文、调用工具、调用 LLM，并返回最终回答。
    /// </summary>
    [HttpPost("complete")]
    public async Task<ChatCompleteResponse> Complete([FromBody] ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        var trace = ChatDebugTrace.Create(request);
        trace?.Complete("backend_received");
        trace?.Start("auth_session_context");
        var user = await RequireUser(cancellationToken);
        request.RuntimeUserId = user.Id;
        await ResolveImageAttachmentsAsync(user, request, cancellationToken);
        await ResolveDocumentAttachmentsAsync(user, request, cancellationToken);
        await _projectReferences.ResolveAsync(user, request, cancellationToken);
        await _markdownDocuments.ResolveAsync(user, request, cancellationToken);
        await _projectAgentMarkdownIndex.ResolveAsync(user, request, cancellationToken);
        await _sessions.RecordUserMessageAsync(user, request, cancellationToken);
        request.ServerMemoryContext = await _memory.BuildPromptContextAsync(user, request, cancellationToken);
        trace?.Complete("auth_session_context");
        trace?.Start("request_started");
        trace?.Complete("request_started");
        trace?.Start("provider_request_started");
        var result = await _orchestrator.CompleteAsync(request, cancellationToken);
        trace?.CompleteProvider();
        trace?.Start("persistence");
        await _sessions.RecordAssistantMessageAsync(user, request, result.Content, null, result.Citations, result.ModelId, result.Model, cancellationToken);
        await _usage.RecordAsync(user, request, result, cancellationToken);
        trace?.Complete("persistence");
        trace?.Start("frontend_push");
        trace?.Complete("frontend_push");
        trace?.Complete("request_completed");
        result.DebugTrace = trace?.Events.ToList();
        await _debugTraceStore.SaveAsync(user, request.SessionId, trace, cancellationToken);
        return result;
    }

    /// <summary>
    /// 执行一次流式聊天完成，通过 SSE 推送 label、工具、思考、内容和完成事件。
    /// </summary>
    [HttpPost("complete/stream")]
    public async Task CompleteStream([FromBody] ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        var response = _httpContextAccessor.HttpContext?.Response
            ?? throw new InvalidOperationException("HttpContext is not available.");
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        var trace = ChatDebugTrace.Create(request);
        await WriteTraceAsync(response, trace?.Complete("backend_received"), cancellationToken);
        await WriteTraceAsync(response, trace?.Start("auth_session_context"), cancellationToken);
        var user = await RequireUser(cancellationToken);
        request.RuntimeUserId = user.Id;
        await ResolveImageAttachmentsAsync(user, request, cancellationToken);
        await ResolveDocumentAttachmentsAsync(user, request, cancellationToken);
        await _projectReferences.ResolveAsync(user, request, cancellationToken);
        await _markdownDocuments.ResolveAsync(user, request, cancellationToken);
        await _projectAgentMarkdownIndex.ResolveAsync(user, request, cancellationToken);
        await _sessions.RecordUserMessageAsync(user, request, cancellationToken);
        await WriteSseAsync(response, new AgentStreamEvent { Type = "session_ready" }, cancellationToken);
        request.ServerMemoryContext = await _memory.BuildPromptContextAsync(user, request, cancellationToken);
        await WriteTraceAsync(response, trace?.Complete("auth_session_context"), cancellationToken);
        var content = new System.Text.StringBuilder();
        var thinking = new System.Text.StringBuilder();
        object? citations = null;
        string? modelId = null;
        string? model = null;
        var providerRequestStarted = false;

        try
        {
            await WriteTraceAsync(response, trace?.Start("request_started"), cancellationToken);
            await WriteTraceAsync(response, trace?.Complete("request_started"), cancellationToken);
            var result = await _orchestrator.CompleteStreamingAsync(request, async (streamEvent, token) =>
            {
                if (streamEvent.Type == "provider_request_started")
                {
                    if (!providerRequestStarted)
                    {
                        providerRequestStarted = true;
                        await WriteTraceAsync(response, trace?.Start("provider_request_started"), token);
                    }
                    return;
                }
                if (providerRequestStarted && streamEvent.Type != "debug_trace")
                {
                    await WriteTraceAsync(response, trace?.FirstStreamEvent(), token);
                }
                if (streamEvent.Type == "content") content.Append(streamEvent.Content);
                if (streamEvent.Type == "thinking") thinking.Append(streamEvent.Content);
                if (streamEvent.Type == "sources") citations = streamEvent.Citations;
                modelId ??= streamEvent.ModelId;
                model ??= streamEvent.Model;
                await WriteSseAsync(response, streamEvent, token);
            }, cancellationToken);
            await WriteTraceAsync(response, trace?.CompleteProvider(), cancellationToken);
            providerRequestStarted = false;
            var finalContent = content.Length > 0 ? content.ToString() : result.Content;
            var finalModelId = modelId ?? result.ModelId;
            var finalModel = model ?? result.Model;
            await WriteTraceAsync(response, trace?.Start("persistence"), cancellationToken);
            await _sessions.RecordAssistantMessageAsync(user, request, finalContent, thinking.ToString(), citations ?? result.Citations, finalModelId, finalModel, cancellationToken);
            await _usage.RecordAsync(user, request, result, cancellationToken);
            await WriteTraceAsync(response, trace?.Complete("persistence"), cancellationToken);
            await WriteTraceAsync(response, trace?.Start("frontend_push"), cancellationToken);
            await WriteTraceAsync(response, trace?.Complete("frontend_push"), cancellationToken);
            await WriteTraceAsync(response, trace?.Complete("request_completed"), cancellationToken);
            await _debugTraceStore.SaveAsync(user, request.SessionId, trace, CancellationToken.None);
            await WriteSseAsync(response, new AgentStreamEvent { Type = "completed" }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (providerRequestStarted) await WriteTraceAsync(response, trace?.CancelProvider(), CancellationToken.None);
            await WriteTraceAsync(response, trace?.Cancel("request_completed"), CancellationToken.None);
            await _debugTraceStore.SaveAsync(user, request.SessionId, trace, CancellationToken.None);
        }
        catch (Exception exception)
        {
            if (providerRequestStarted) await WriteTraceAsync(response, trace?.FailProvider("request_failed"), CancellationToken.None);
            await WriteTraceAsync(response, trace?.Fail("request_completed", "request_failed"), CancellationToken.None);
            await _debugTraceStore.SaveAsync(user, request.SessionId, trace, CancellationToken.None);
            await WriteSseAsync(response, new AgentStreamEvent
            {
                Type = "error",
                Content = ToClientError(exception, request)
            }, cancellationToken);
        }
    }

    [HttpPost("attachments/images")]
    [Consumes("multipart/form-data")]
    public async Task<ChatImageAttachmentDto> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return await _attachments.SaveAsync(user, file, cancellationToken);
    }

    [HttpPost("attachments/files")]
    [Consumes("multipart/form-data")]
    public async Task<ChatFileAttachmentDto> UploadFile(IFormFile file, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return await _fileAttachments.SaveAsync(user, file, cancellationToken);
    }

    [HttpDelete("attachments/{attachmentId}")]
    public async Task<object> DeleteImage([FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return new { ok = await _attachments.DeleteAsync(user, attachmentId, cancellationToken) };
    }

    [HttpGet("attachments/{attachmentId}/preview")]
    public async Task<IActionResult> GetTemporaryImage([FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var image = await _attachments.OpenTemporaryImageAsync(await RequireUser(cancellationToken), attachmentId, cancellationToken);
        return image == null
            ? new NotFoundResult()
            : new FileStreamResult(new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read), image.ContentType) { EnableRangeProcessing = true };
    }

    [HttpDelete("attachments/files/{attachmentId}")]
    public async Task<object> DeleteFile([FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return new { ok = await _fileAttachments.DeleteAsync(user, attachmentId, cancellationToken) };
    }

    [HttpGet("attachments/files/{attachmentId}/extraction")]
    public async Task<ChatFileExtractionPreviewDto> GetFileExtraction([FromRoute] string attachmentId, [FromQuery(Name = "session_id")] string? sessionId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return await _fileAttachments.ExtractPreviewAsync(user, sessionId, attachmentId, cancellationToken);
    }

    [HttpGet("uploads/mine")]
    public async Task<List<ChatUploadFileDto>> ListMyUploads([FromQuery] string? keyword, [FromQuery] string? kind, [FromQuery(Name = "session_id")] string? sessionId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        => await _uploadLibrary.ListAsync(await RequireUser(cancellationToken), null, keyword, kind, sessionId, limit, cancellationToken);

    [HttpGet("uploads/{attachmentId}/content")]
    public async Task<IActionResult> OpenMyUpload([FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var content = await _uploadLibrary.OpenAsync(await RequireUser(cancellationToken), null, attachmentId, cancellationToken);
        return content == null
            ? new NotFoundResult()
            : new FileStreamResult(new FileStream(content.Path, FileMode.Open, FileAccess.Read, FileShare.Read), content.ContentType) { EnableRangeProcessing = true };
    }

    [HttpPost("codex/heartbeat")]
    public async Task<object> CodexHeartbeat([FromBody] CodexRuntimeHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        await _codex.HeartbeatAsync(user, request, cancellationToken);
        return new { ok = true };
    }

    [HttpGet("attachments/{sessionId}/{attachmentId}")]
    public async Task<IActionResult> GetPersistedImage([FromRoute] string sessionId, [FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var image = await _attachments.OpenPersistedImageAsync(user, sessionId, attachmentId, cancellationToken);
        if (image == null) return new NotFoundResult();
        return new FileStreamResult(new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read), image.ContentType) { EnableRangeProcessing = true };
    }

    private async Task ResolveImageAttachmentsAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        if (request.AttachmentIds.Count == 0) return;
        if (!string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Image attachments are currently supported only when Codex local agent is selected.");
        }
        var model = _codexModelPolicy.ResolveModel(request.CodexModelId, request.CodexReasoningEffort);
        var imagePolicy = _imageOcrPolicy.GetPolicy();
        var imageEnabled = model.IsBuiltin
            ? imagePolicy.NativeImageInputEnabled
            : model.SupportsImageOcr && imagePolicy.Enabled;
        if (!imageEnabled)
        {
            throw new InvalidOperationException(model.IsBuiltin
                ? "The administrator has disabled native Codex image input."
                : "The selected Codex profile does not have PaddleOCR image recognition enabled.");
        }
        request.LocalImagePaths = (await _attachments.ResolveLocalAttachmentsAsync(user, request.SessionId, request.AttachmentIds, cancellationToken)).Select(item => item.LocalPath).ToList();
    }

    private async Task ResolveDocumentAttachmentsAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        if (request.DocumentAttachmentIds.Count == 0) return;
        if (!string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Document attachments are currently supported only when Codex local agent is selected.");
        }
        var attachments = await _fileAttachments.ResolveLocalAttachmentsAsync(user, request.SessionId, request.DocumentAttachmentIds, cancellationToken);
        request.ServerAttachmentContext = await _fileAttachments.ExtractContextAsync(attachments, cancellationToken);
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken) => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();

    private static async Task WriteSseAsync(HttpResponse response, AgentStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(streamEvent, JsonOptions);
        await response.WriteAsync($"event: {streamEvent.Type}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static Task WriteTraceAsync(HttpResponse response, AgentStreamEvent? streamEvent, CancellationToken cancellationToken)
    {
        return streamEvent is null ? Task.CompletedTask : WriteSseAsync(response, streamEvent, cancellationToken);
    }

    private static string ToClientError(Exception exception, ChatCompleteRequest request)
    {
        var isCodex = string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase);
        return isCodex && (exception.Message.StartsWith("Codex stopped after", StringComparison.Ordinal)
            || exception.Message.StartsWith("Codex exceeded", StringComparison.Ordinal))
            ? exception.Message
            : "Chat request failed.";
    }
}
