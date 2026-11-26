using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.KunBuddy;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Knowledge;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/knowledge-resources")]
public sealed class KnowledgeResourceAppService(KnowledgeResourceService resources, KunBuddySessionService sessions, IAuthService auth, IHttpContextAccessor httpContextAccessor) : IDynamicApiController
{
    [HttpGet("tree")]
    public List<KnowledgeResourceNodeDto> Tree() => resources.Tree();
    [HttpGet("processing")]
    public List<KnowledgeResourceProcessingDto> Processing() => resources.Processing();
    [HttpGet("read")]
    public Task<KnowledgeResourceReadDto> Read([FromQuery] string uri, CancellationToken ct) => resources.ReadAsync(uri, ct);
    [HttpPost("directories")]
    public Task<KnowledgeResourceNodeDto> CreateDirectory([FromBody] KnowledgeDirectoryRequest request, CancellationToken ct) => resources.CreateDirectoryAsync(request, ct);
    [HttpPost("upload")]
    public Task<KnowledgeResourceImportDto> Upload([FromForm] KnowledgeResourceUploadRequest request, CancellationToken ct) => resources.UploadAsync(request, ct);
    [HttpPost("parse")]
    public KnowledgeCompilationJobDto Parse([FromBody] KnowledgeResourceRequest request) => resources.Parse(request.Uri);
    [HttpGet("task")]
    public KnowledgeCompilationJobDto? TaskStatus([FromQuery] string uri) => resources.TaskStatus(uri);
    [HttpPost("tasks/{id:long}/cancel")]
    public KnowledgeCompilationJobDto Cancel([FromRoute] long id, [FromBody] KnowledgeResourceRequest request) => resources.Cancel(request.Uri, id);
    [HttpPost("ask")]
    public Task<KnowledgeResourceAnswerDto> Ask([FromBody] KnowledgeResourceRequest request, CancellationToken ct) => resources.AskAsync(request, ct);
    [HttpGet("platform-sessions")]
    public async Task<PlatformSessionImportOptionsDto> PlatformSessions([FromQuery] int limit = 100, CancellationToken ct = default)
        => await sessions.ListPlatformSessionsAsync(await RequireUser(ct), limit, ct);
    [HttpPost("platform-sessions/import")]
    public async Task<PlatformSessionImportResultDto> ImportPlatformSessions([FromBody] PlatformSessionImportRequest request, CancellationToken ct)
        => await sessions.ImportPlatformSessionsAsync(await RequireUser(ct), request.SessionIds, ct);
    [HttpGet("file")]
    public IActionResult File([FromQuery] string uri, [FromQuery] bool download = false, [FromQuery(Name = "parsed_document_id")] long? parsedDocumentId = null)
    {
        try
        {
            var file = resources.File(uri, parsedDocumentId);
            return new PhysicalFileResult(file.Path, file.ContentType) { FileDownloadName = download ? file.Name : null, EnableRangeProcessing = true };
        }
        catch (UnauthorizedAccessException) when (parsedDocumentId.HasValue) { return new StatusCodeResult(403); }
        catch (KeyNotFoundException) when (parsedDocumentId.HasValue) { return new NotFoundResult(); }
        catch (InvalidOperationException) when (parsedDocumentId.HasValue) { return new ConflictResult(); }
        catch (ArgumentException) when (parsedDocumentId.HasValue) { return new BadRequestResult(); }
    }

    [HttpDelete("file")]
    public async Task<IActionResult> DeleteFile([FromQuery] string uri, [FromQuery(Name = "document_id")] long documentId, CancellationToken ct)
    {
        AuthenticatedUser? user = null;
        try
        {
            user = await RequireUser(ct);
            return new OkObjectResult(await resources.DeleteAsync(uri, documentId, user, ct));
        }
        catch (UnauthorizedAccessException ex) { return new ObjectResult(new { message = ex.Message }) { StatusCode = user is null ? 401 : 403 }; }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new StatusCodeResult(499); }
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await auth.TryGetCurrentUserAsync(httpContextAccessor.HttpContext!, ct)
            ?? throw new UnauthorizedAccessException();
    }
}
