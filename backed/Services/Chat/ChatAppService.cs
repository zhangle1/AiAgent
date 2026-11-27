using AiAgent.Backend.Services.Chat.Orchestration;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using System.Text;
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
    private readonly IAuthService _authService;
    private readonly IChatDispatcher _dispatcher;
    private readonly IChatImageAttachmentService _attachments;
    private readonly IChatFileAttachmentService _fileAttachments;
    private readonly IChatUploadLibraryService _uploadLibrary;
    private readonly ICodexChatService _codex;

    public ChatAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IChatDispatcher dispatcher,
        IChatImageAttachmentService attachments, IChatFileAttachmentService fileAttachments,
        IChatUploadLibraryService uploadLibrary, ICodexChatService codex)
    {
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
        _dispatcher = dispatcher;
        _attachments = attachments;
        _fileAttachments = fileAttachments;
        _uploadLibrary = uploadLibrary;
        _codex = codex;
    }

    /// <summary>
    /// 读取当前用户可用的聊天技能。
    /// </summary>
    [HttpGet("skills")]
    public Task<ActionResult<IReadOnlyList<JsonElement>>> Skills(CancellationToken cancellationToken)
        => ReadBootstrapAsync((_, _) => Task.FromResult(ChatSkillCatalog.All), cancellationToken);

    [HttpPost("complete")]
    public Task<ChatCompleteResponse> Complete([FromBody] ChatCompleteRequest request, CancellationToken cancellationToken)
        => _dispatcher.CompleteAsync(request, RequireUser, cancellationToken);

    /// <summary>
    /// 执行一次流式聊天完成，通过 SSE 推送 label、工具、思考、内容和完成事件。
    /// </summary>
    [HttpPost("complete/stream")]
    public async Task CompleteStream([FromBody] ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available.");
        var connection = new WebRequestAuthentication(context, cancellationToken);
        if (connection.Disconnected) return;
        var response = context.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        var trace = ChatDebugTrace.Create(request);
        try
        {
            await WriteTraceAsync(response, trace?.Complete("backend_received"), cancellationToken);
            await WriteTraceAsync(response, trace?.Start("auth_session_context"), cancellationToken);
            var user = await connection.LookupAsync(_authService.TryGetCurrentUserAsync);
            if (connection.Disconnected) return;
            if (user == null) throw new UnauthorizedAccessException();
            await _dispatcher.DispatchStreamAsync(user, request, trace,
                (value, token) => WriteSseAsync(response, value, token), RequireUser, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteSseAsync(response, new AgentStreamEvent { Type = "error", Content = ChatExecutionErrors.ToClientError(error, request) }, cancellationToken);
        }
    }

    [HttpPost("commands/{commandId}/cancel")]
    public async Task<object> CancelCommand(string commandId, [FromQuery(Name = "session_id")] string sessionId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return new { accepted = await _dispatcher.CancelAsync(user, sessionId, commandId, cancellationToken) };
    }

    [HttpGet("active-sessions")]
    public Task<ActionResult<List<string>>> ActiveSessions(CancellationToken cancellationToken)
        => ReadBootstrapAsync((user, _) => Task.FromResult(
            _httpContextAccessor.HttpContext!.RequestServices.GetRequiredService<ChatDeliveryService>().ActiveSessions(user)), cancellationToken);

    [HttpGet("sessions/{sessionId}/deliveries")]
    public Task<ActionResult<List<ChatDeliveryDto>>> Deliveries([FromRoute] string sessionId, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext!;
        return new WebRequestAuthentication(context, cancellationToken).ReadAsync<List<ChatDeliveryDto>>(_authService, async (user, token) =>
        {
            var deliveries = context.RequestServices.GetRequiredService<ChatDeliveryService>();
            try { return await deliveries.ListAsync(user, sessionId, token); }
            catch (UnauthorizedAccessException) { return new StatusCodeResult(StatusCodes.Status403Forbidden); }
        });
    }

    [HttpPost("attachments/images")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ChatImageAttachmentDto>> UploadImage(IFormFile? file, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        if (file is not { Length: > 0 }) return new BadRequestObjectResult(new { message = "Image file is empty." });

        try { return await _attachments.SaveAsync(user, file, cancellationToken); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    [HttpPost("attachments/files")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ChatFileAttachmentDto>> UploadFile(IFormFile? file, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        if (file is not { Length: > 0 }) return new BadRequestObjectResult(new { message = "Attachment file is empty." });

        try { return await _fileAttachments.SaveAsync(user, file, cancellationToken); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (DecoderFallbackException) { return new BadRequestObjectResult(new { message = "文本附件编码无法识别，请另存为 UTF-8 后重新上传。" }); }
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
    {
        var user = await RequireUser(cancellationToken);
        return await _uploadLibrary.ListAsync(user, user.Id, keyword, kind, sessionId, limit, cancellationToken);
    }

    [HttpGet("uploads/{attachmentId}/content")]
    public async Task<IActionResult> OpenMyUpload([FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var content = await _uploadLibrary.OpenAsync(user, user.Id, attachmentId, cancellationToken);
        return content == null
            ? new NotFoundResult()
            : new FileStreamResult(new FileStream(content.Path, FileMode.Open, FileAccess.Read, FileShare.Read), content.ContentType) { EnableRangeProcessing = true };
    }

    [HttpPost("codex/heartbeat")]
    public Task<ActionResult<object>> CodexHeartbeat([FromBody] CodexRuntimeHeartbeatRequest request, CancellationToken cancellationToken)
        => ReadBootstrapAsync<object>(async (user, token) =>
        {
            await _codex.HeartbeatAsync(user, request, token);
            return new { ok = true };
        }, cancellationToken);

    [HttpGet("attachments/{sessionId}/{attachmentId}")]
    public async Task<IActionResult> GetPersistedImage([FromRoute] string sessionId, [FromRoute] string attachmentId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var image = await _attachments.OpenPersistedImageAsync(user, sessionId, attachmentId, cancellationToken);
        if (image == null) return new NotFoundResult();
        return new FileStreamResult(new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read), image.ContentType) { EnableRangeProcessing = true };
    }

    [HttpGet("codex/quota")]
    public Task<ActionResult<CodexQuotaResponse>> GetCodexQuota(CancellationToken cancellationToken)
        => ReadBootstrapAsync((_, token) => _codex.ReadQuotaAsync(token), cancellationToken);

    private Task<ActionResult<T>> ReadBootstrapAsync<T>(Func<AuthenticatedUser, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
        => new WebRequestAuthentication(_httpContextAccessor.HttpContext!, cancellationToken)
            .ReadAsync<T>(_authService, async (user, token) => new ActionResult<T>(await read(user, token)));

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

}
