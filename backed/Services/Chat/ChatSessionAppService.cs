using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Chat;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/sessions")]
public sealed class ChatSessionAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IChatSessionService _sessions;
    public ChatSessionAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IChatSessionService sessions) => (_httpContextAccessor, _authService, _sessions) = (httpContextAccessor, authService, sessions);

    [HttpGet("list")]
    public async Task<object> List([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await RequireUser(cancellationToken);
        return new { sessions = await _sessions.ListAsync(user, Math.Clamp(limit, 1, 100), cancellationToken) };
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> Get(string sessionId, CancellationToken cancellationToken)
    {
        var detail = await _sessions.GetAsync(await RequireUser(cancellationToken), sessionId, cancellationToken);
        return detail == null ? new NotFoundObjectResult(new { message = "会话不存在。" }) : new OkObjectResult(detail);
    }

    [HttpPatch("{sessionId}")]
    public async Task<IActionResult> Rename(string sessionId, [FromBody] RenameChatSessionRequest request, CancellationToken cancellationToken)
        => await _sessions.RenameAsync(await RequireUser(cancellationToken), sessionId, request.Title, cancellationToken) ? new OkObjectResult(new { ok = true }) : new NotFoundObjectResult(new { message = "会话不存在。" });

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderChatSessionsRequest request, CancellationToken cancellationToken)
        => await _sessions.ReorderAsync(await RequireUser(cancellationToken), request.SessionIds, cancellationToken) ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = "会话排序数据无效。" });

    [HttpPatch("{sessionId}/meta")]
    public async Task<IActionResult> UpdateMetadata(string sessionId, [FromBody] UpdateChatSessionMetaRequest request, CancellationToken cancellationToken)
        => await _sessions.UpdateMetadataAsync(await RequireUser(cancellationToken), sessionId, request, cancellationToken) ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = "会话属性无效或不存在。" });

    [HttpGet("project-preferences")]
    public async Task<object> ListProjectPreferences(CancellationToken cancellationToken)
        => new { preferences = await _sessions.ListProjectPreferencesAsync(await RequireUser(cancellationToken), cancellationToken) };

    [HttpPatch("projects/{projectId:long}/preference")]
    public async Task<IActionResult> UpdateProjectPreference(long projectId, [FromBody] UpdateChatProjectPreferenceRequest request, CancellationToken cancellationToken)
        => await _sessions.UpdateProjectPreferenceAsync(await RequireUser(cancellationToken), projectId, request, cancellationToken) ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = "项目会话偏好无效或项目不存在。" });

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> Delete(string sessionId, CancellationToken cancellationToken)
        => await _sessions.DeleteAsync(await RequireUser(cancellationToken), sessionId, cancellationToken) ? new OkObjectResult(new { deleted = true }) : new NotFoundObjectResult(new { message = "会话不存在。" });

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken) => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
}
