using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Chat;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/chat/sessions/{sessionId}/goal")]
public sealed class ChatGoalAppService(IHttpContextAccessor context, IAuthService auth, IChatGoalControl goals) : IDynamicApiController
{
    [HttpGet("")]
    public async Task<IActionResult> Get([FromRoute] string sessionId, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        return new OkObjectResult(new { goal = goals.GetGoal(user.Id, sessionId) });
    }

    [HttpPost("{goalAction}")]
    public async Task<IActionResult> Control([FromRoute] string sessionId, [FromRoute] string goalAction, [FromBody] GoalControlRequest request, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        if (goalAction is not ("pause" or "end")) return new BadRequestObjectResult(new { message = "未知目标操作。" });
        try { return new OkObjectResult(new { goal = goals.ControlGoal(user.Id, sessionId, goalAction, request.ExpectedVersion) }); }
        catch (InvalidOperationException error) { return new ConflictObjectResult(new { message = error.Message }); }
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken token) =>
        await auth.TryGetCurrentUserAsync(context.HttpContext ?? throw new UnauthorizedAccessException(), token)
        ?? throw new UnauthorizedAccessException();
}

public sealed class GoalControlRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("expected_version")]
    public long ExpectedVersion { get; set; }
}
