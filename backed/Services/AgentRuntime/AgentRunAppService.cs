using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.AgentRuntime;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/agent-runs")]
public sealed class AgentRunAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _http;
    private readonly IAuthService _auth;
    private readonly IAgentRunStore _store;
    private readonly IRunCoordinator _coordinator;

    public AgentRunAppService(IHttpContextAccessor http, IAuthService auth, IAgentRunStore store, IRunCoordinator coordinator)
        => (_http, _auth, _store, _coordinator) = (http, auth, store, coordinator);

    [HttpGet("session/{sessionId}")]
    public async Task<object> List(string sessionId, [FromQuery] int limit = 30, CancellationToken cancellationToken = default)
        => new { runs = _store.List(await User(cancellationToken), sessionId.Trim(), limit) };

    [HttpGet("{runId}")]
    public async Task<IActionResult> Get(string runId, CancellationToken cancellationToken = default)
    {
        var detail = _store.Get(await User(cancellationToken), runId.Trim());
        return detail == null ? new NotFoundObjectResult(new { message = "运行记录不存在。" }) : new OkObjectResult(detail);
    }

    [HttpPost("{runId}/cancel")]
    public async Task<IActionResult> Cancel(string runId, CancellationToken cancellationToken = default)
    {
        var user = await User(cancellationToken);
        if (_store.Get(user, runId.Trim()) == null) return new NotFoundObjectResult(new { message = "运行记录不存在。" });
        return _coordinator.Cancel(runId.Trim(), user.Id)
            ? new AcceptedResult(string.Empty, new { accepted = true })
            : new ConflictObjectResult(new { message = "运行已结束或不在当前服务实例执行。" });
    }

    private async Task<AuthenticatedUser> User(CancellationToken token)
        => await _auth.TryGetCurrentUserAsync(_http.HttpContext!, token) ?? throw new UnauthorizedAccessException();
}

