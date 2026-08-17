using AiAgent.Backend.Dtos.Push;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Push;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/admin/push")]
public sealed class PushAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IProjectPushService _pushes;

    public PushAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IProjectPushService pushes)
        => (_httpContextAccessor, _authService, _pushes) = (httpContextAccessor, authService, pushes);

    [HttpGet("channels")]
    public async Task<List<PushChannelDto>> ListChannels(CancellationToken cancellationToken) => await _pushes.ListChannelsAsync(await RequireAdministrator(cancellationToken), cancellationToken);

    [HttpPost("channels")]
    public async Task<PushChannelDto> CreateChannel([FromBody] PushChannelUpsertRequest request, CancellationToken cancellationToken) => await _pushes.CreateChannelAsync(await RequireAdministrator(cancellationToken), request, cancellationToken);

    [HttpPut("channels/{channelId:long}")]
    public async Task<IActionResult> UpdateChannel(long channelId, [FromBody] PushChannelUpsertRequest request, CancellationToken cancellationToken)
    {
        var channel = await _pushes.UpdateChannelAsync(await RequireAdministrator(cancellationToken), channelId, request, cancellationToken);
        return channel == null ? new NotFoundResult() : new OkObjectResult(channel);
    }

    [HttpDelete("channels/{channelId:long}")]
    public async Task<IActionResult> DeleteChannel(long channelId, CancellationToken cancellationToken)
        => await _pushes.DeleteChannelAsync(await RequireAdministrator(cancellationToken), channelId, cancellationToken) ? new OkObjectResult(new { deleted = true }) : new NotFoundResult();

    [HttpPost("channels/{channelId:long}/test")]
    public async Task<IActionResult> TestChannel(long channelId, CancellationToken cancellationToken)
    {
        var result = await _pushes.TestChannelAsync(await RequireAdministrator(cancellationToken), channelId, cancellationToken);
        return result == null ? new NotFoundResult() : new OkObjectResult(result);
    }

    [HttpPost("channels/{channelId:long}/stream-test")]
    public async Task<IActionResult> TestStreamChannel(long channelId, CancellationToken cancellationToken)
    {
        var result = await _pushes.TestStreamChannelAsync(await RequireAdministrator(cancellationToken), channelId, cancellationToken);
        return result == null ? new NotFoundResult() : new OkObjectResult(result);
    }

    [HttpGet("project-bindings")]
    public async Task<List<ProjectPushBindingDto>> ListBindings(CancellationToken cancellationToken) => await _pushes.ListBindingsAsync(await RequireAdministrator(cancellationToken), cancellationToken);

    [HttpPost("project-bindings")]
    public async Task<ProjectPushBindingDto> CreateBinding([FromBody] ProjectPushBindingUpsertRequest request, CancellationToken cancellationToken) => await _pushes.CreateBindingAsync(await RequireAdministrator(cancellationToken), request, cancellationToken);

    [HttpPut("project-bindings/{bindingId:long}")]
    public async Task<IActionResult> UpdateBinding(long bindingId, [FromBody] ProjectPushBindingUpsertRequest request, CancellationToken cancellationToken)
    {
        var binding = await _pushes.UpdateBindingAsync(await RequireAdministrator(cancellationToken), bindingId, request, cancellationToken);
        return binding == null ? new NotFoundResult() : new OkObjectResult(binding);
    }

    [HttpDelete("project-bindings/{bindingId:long}")]
    public async Task<IActionResult> DeleteBinding(long bindingId, CancellationToken cancellationToken)
        => await _pushes.DeleteBindingAsync(await RequireAdministrator(cancellationToken), bindingId, cancellationToken) ? new OkObjectResult(new { deleted = true }) : new NotFoundResult();

    [HttpGet("audit")]
    public async Task<List<PushAuditDto>> ListAudit([FromQuery] int limit = 100, CancellationToken cancellationToken = default) => await _pushes.ListAuditAsync(await RequireAdministrator(cancellationToken), limit, cancellationToken);

    private async Task<AuthenticatedUser> RequireAdministrator(CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        return user;
    }
}
