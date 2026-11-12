using AiAgent.Backend.Dtos.ChangeAtlas;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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
    [HttpGet("{projectId:long}/change-atlas/snapshots/{snapshotId}/imports")]
    public Task<IActionResult> Imports(long projectId, string snapshotId, [FromQuery(Name = "repository_id")] long repositoryId, CancellationToken token) => Act(user => service.ImportsAsync(user, projectId, snapshotId, repositoryId, token), token);
    [HttpPost("{projectId:long}/change-atlas/snapshots/{snapshotId}/analysis/stream")]
    public async Task AnalyzeStream(long projectId, string snapshotId, [FromBody] AtlasAnalysisRequest request, CancellationToken token)
    {
        var context = http.HttpContext!;
        var user = await auth.TryGetCurrentUserAsync(context, token);
        if (user == null) { context.Response.StatusCode = 401; return; }
        // Check access before opening the stream, just as for the JSON endpoint.
        var access = await Act(current => service.GetAsync(current, projectId, snapshotId, token), token);
        if (access is not OkObjectResult) { await access.ExecuteResultAsync(new ControllerContext { HttpContext = context }); return; }
        var response = context.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        using var writer = new SemaphoreSlim(1, 1);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task Send(string type, object value, CancellationToken ct)
        {
            await writer.WaitAsync(ct);
            try {
                await response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(value)}\n\n", ct);
                await response.Body.FlushAsync(ct);
            } finally { writer.Release(); }
        }
        async Task Heartbeat()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            try { while (await timer.WaitForNextTickAsync(heartbeatStop.Token)) await Send("heartbeat", new { }, heartbeatStop.Token); }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
        }
        await Send("status", new { message = "正在准备当前范围的 AI 解读…" }, token);
        var heartbeat = Heartbeat();
        try {
            var result = await Act(current => analysis.AnalyzeAsync(current, projectId, snapshotId, request, token,
                status => Send("status", new { message = status }, token)), token);
            if (result is OkObjectResult ok) await Send("result", ok.Value!, token);
            else if (result is ObjectResult error) await Send("error", error.Value!, token);
            else await Send("error", new { message = "AI 解读未完成，请重试。" }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { heartbeatStop.Cancel(); await heartbeat; }
    }
    private async Task<IActionResult> Act<T>(Func<AuthenticatedUser, Task<T>> action, CancellationToken token)
    {
        var user = await auth.TryGetCurrentUserAsync(http.HttpContext!, token);
        if (user == null) return new UnauthorizedResult();
        try { return new OkObjectResult(await action(user)); }
        catch (UnauthorizedAccessException) { return new ObjectResult(new { message = "无权访问该项目或地图。" }) { StatusCode = 403 }; }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new ConflictObjectResult(new { message = "关系读取超过时间预算，请缩小仓库范围后重试。" }); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { return new ConflictObjectResult(new { message = "地图缓存暂不可读取，请刷新地图后重试。" }); }
    }
}
