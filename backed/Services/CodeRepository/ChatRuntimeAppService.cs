using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.CodeRepository;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/code-runtime/projects/{projectId:long}/chat-runs")]
public sealed class ChatRuntimeAppService(ChatRuntimeService runtime, IAuthService auth, IProjectAccessService access, IHttpContextAccessor context) : IDynamicApiController
{
    // The controller route already contains {projectId}; an explicit empty action
    // template prevents Furion from appending an automatic /list/{projectId} segment.
    [HttpGet("")]
    public async Task<IActionResult> List([FromRoute] long projectId, CancellationToken token)
        => await Execute(projectId, () => runtime.List(projectId), token);

    // Keep the prepare endpoint at the controller route for the same reason.
    [HttpPost("")]
    public async Task<IActionResult> Prepare([FromRoute] long projectId, [FromBody] ChatRuntimePrepareRequest request, CancellationToken token)
        => await Execute(projectId, () => runtime.Prepare(projectId, request), token);

    [HttpPost("{requestId}/visit")]
    public async Task<IActionResult> Visit([FromRoute] long projectId, [FromRoute] string requestId, CancellationToken token)
        => await Execute(projectId, () => { runtime.Visit(projectId, requestId); return new { ok = true }; }, token);

    [HttpPost("{requestId}/stop")]
    public async Task<IActionResult> Stop([FromRoute] long projectId, [FromRoute] string requestId, CancellationToken token)
        => await Execute(projectId, () => { runtime.Stop(projectId, requestId); return new { ok = true }; }, token);

    private async Task<IActionResult> Execute<T>(long projectId, Func<T> action, CancellationToken token)
    {
        var user = await auth.TryGetCurrentUserAsync(context.HttpContext!, token);
        if (user is null) return new UnauthorizedResult();
        if (!access.CanAccess(user, projectId)) return new StatusCodeResult(403);
        try { return new OkObjectResult(action()); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
    }
}
