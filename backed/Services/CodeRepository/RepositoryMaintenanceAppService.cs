using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.CodeRepository;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/projects/{projectId:long}/maintenance")]
public sealed class RepositoryMaintenanceAppService(RepositoryMaintenanceService service, IAuthService auth, IHttpContextAccessor http) : IDynamicApiController
{
    [HttpGet] public Task<IActionResult> Get(long projectId, CancellationToken token) => Act(user => service.Get(user, projectId), token);
    [HttpPut] public Task<IActionResult> Save(long projectId, [FromBody] MaintenanceSettings settings, CancellationToken token) => Act(user => service.Save(user, projectId, settings), token);
    [HttpGet("runs")] public Task<IActionResult> Runs(long projectId, CancellationToken token) => Act(user => service.List(user, projectId), token);
    [HttpPost("runs")] public Task<IActionResult> Run(long projectId, CancellationToken token) => Act(user => service.Enqueue(user, projectId), token);
    [HttpPost("runs/{runId}/cancel")] public Task<IActionResult> Cancel(long projectId, string runId, CancellationToken token) => Act(user => { service.Cancel(user, projectId, runId); return new { ok = true }; }, token);
    [HttpPost("runs/{runId}/push")] public Task<IActionResult> Push(long projectId, string runId, CancellationToken token) => Act(user => service.RequestPush(user, projectId, runId), token);
    private async Task<IActionResult> Act<T>(Func<AuthenticatedUser, T> action, CancellationToken token)
    {
        var user = await auth.TryGetCurrentUserAsync(http.HttpContext!, token);
        if (user == null) return new UnauthorizedResult();
        try { return new OkObjectResult(action(user)); }
        catch (UnauthorizedAccessException ex) { return new ObjectResult(new { message = ex.Message }) { StatusCode = 403 }; }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
    }
}
