using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Usage;
using AiAgent.Backend.Services.Auth;
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
    private readonly IUsageStatisticsService _usage;

    /// <summary>
    /// 初始化聊天 API 服务。
    /// </summary>
    public ChatAppService(IHttpContextAccessor httpContextAccessor, IChatOrchestrator orchestrator, IAuthService authService, IChatSessionService sessions, IChatImageAttachmentService attachments, IUsageStatisticsService usage)
    {
        _httpContextAccessor = httpContextAccessor;
        _orchestrator = orchestrator;
        _authService = authService;
        _sessions = sessions;
        _attachments = attachments;
        _usage = usage;
    }

    /// <summary>
    /// 执行一次聊天完成：创建 Agent 上下文、调用工具、调用 LLM，并返回最终回答。
    /// </summary>
    [HttpPost("complete")]
    public async Task<ChatCompleteResponse> Complete([FromBody] ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        await ResolveImageAttachmentsAsync(user, request, cancellationToken);
        await _sessions.RecordUserMessageAsync(user, request, cancellationToken);
        var result = await _orchestrator.CompleteAsync(request, cancellationToken);
        await _sessions.RecordAssistantMessageAsync(user, request, result.Content, null, result.Citations, result.ModelId, result.Model, cancellationToken);
        await _usage.RecordAsync(user, request, result, cancellationToken);
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
        var user = await RequireUser(cancellationToken);
        await ResolveImageAttachmentsAsync(user, request, cancellationToken);
        await _sessions.RecordUserMessageAsync(user, request, cancellationToken);
        var content = new System.Text.StringBuilder();
        var thinking = new System.Text.StringBuilder();
        object? citations = null;
        string? modelId = null;
        string? model = null;

        try
        {
            var result = await _orchestrator.CompleteStreamingAsync(request, async (streamEvent, token) =>
            {
                if (streamEvent.Type == "content") content.Append(streamEvent.Content);
                if (streamEvent.Type == "thinking") thinking.Append(streamEvent.Content);
                if (streamEvent.Type == "sources") citations = streamEvent.Citations;
                modelId ??= streamEvent.ModelId;
                model ??= streamEvent.Model;
                await WriteSseAsync(response, streamEvent, token);
            }, cancellationToken);
            var finalContent = content.Length > 0 ? content.ToString() : result.Content;
            var finalModelId = modelId ?? result.ModelId;
            var finalModel = model ?? result.Model;
            await _sessions.RecordAssistantMessageAsync(user, request, finalContent, thinking.ToString(), citations ?? result.Citations, finalModelId, finalModel, cancellationToken);
            await _usage.RecordAsync(user, request, result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser stopped the streaming request; no terminal error should be emitted.
        }
        catch (Exception ex)
        {
            await WriteSseAsync(response, new AgentStreamEvent
            {
                Type = "error",
                Content = ex.Message
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

    [HttpDelete("attachments/{attachmentId}")]
    public async Task<object> DeleteImage([FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return new { ok = await _attachments.DeleteAsync(user, attachmentId, cancellationToken) };
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
        request.LocalImagePaths = (await _attachments.ResolveLocalAttachmentsAsync(user, request.SessionId, request.AttachmentIds, cancellationToken)).Select(item => item.LocalPath).ToList();
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken) => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();

    private static async Task WriteSseAsync(HttpResponse response, AgentStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(streamEvent, JsonOptions);
        await response.WriteAsync($"event: {streamEvent.Type}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
