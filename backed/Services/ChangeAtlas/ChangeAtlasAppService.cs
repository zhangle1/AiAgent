using AiAgent.Backend.Dtos.ChangeAtlas;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.ChangeAtlas;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/projects")]
public sealed class ChangeAtlasAppService(ChangeAtlasService service, ChangeAtlasAnalysisService analysis, IAuthService auth, IHttpContextAccessor http) : IDynamicApiController
{
    [HttpPost("{projectId:long}/change-atlas/snapshots")]
    public Task<IActionResult> Create(long projectId, [FromBody] AtlasCreateRequest request, CancellationToken token) => Act(user => service.CreateAsync(user, projectId, request, token), token);
    [HttpGet("{projectId:long}/change-atlas/snapshots/{snapshotId}")]
    public Task<IActionResult> Get(long projectId, string snapshotId, CancellationToken token) => Act(user => service.GetAsync(user, projectId, snapshotId, token), token);
    [HttpGet("{projectId:long}/change-atlas/snapshots/{snapshotId}/evidence")]
    public Task<IActionResult> Evidence(long projectId, string snapshotId, [FromQuery(Name = "commit_id")] string commitId, [FromQuery(Name = "file_id")] string fileId, CancellationToken token) => Act(user => service.EvidenceAsync(user, projectId, snapshotId, commitId, fileId, token), token);
    [HttpPost("{projectId:long}/change-atlas/snapshots/{snapshotId}/analysis")]
    public Task<IActionResult> Analyze(long projectId, string snapshotId, [FromBody] AtlasAnalysisRequest request, CancellationToken token) => Act(user => analysis.AnalyzeAsync(user, projectId, snapshotId, request, token), token);
    private async Task<IActionResult> Act<T>(Func<AuthenticatedUser, Task<T>> action, CancellationToken token)
    {
        var user = await auth.TryGetCurrentUserAsync(http.HttpContext!, token);
        if (user == null) return new UnauthorizedResult();
        try { return new OkObjectResult(await action(user)); }
        catch (UnauthorizedAccessException) { return new ObjectResult(new { message = "无权访问该项目或地图。" }) { StatusCode = 403 }; }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { return new ConflictObjectResult(new { message = "地图缓存暂不可读取，请刷新地图后重试。" }); }
    }
}
