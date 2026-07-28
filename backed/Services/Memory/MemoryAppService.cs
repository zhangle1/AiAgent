using AiAgent.Backend.Dtos.Memory;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Memory;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/memory")]
public sealed class MemoryAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IMemoryService _memory;

    public MemoryAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IMemoryService memory)
        => (_httpContextAccessor, _authService, _memory) = (httpContextAccessor, authService, memory);

    [HttpGet("items")]
    public async Task<object> List([FromQuery(Name = "project_id")] long? projectId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        => new { items = await _memory.ListAsync(await RequireUser(cancellationToken), projectId, limit, cancellationToken) };

    [HttpPost("items")]
    public async Task<IActionResult> Create([FromBody] CreateMemoryItemRequest request, CancellationToken cancellationToken)
    {
        var result = await _memory.CreateAsync(await RequireUser(cancellationToken), request, cancellationToken);
        return result.Item != null ? new OkObjectResult(result.Item) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpPost("items/{memoryId:long}/archive")]
    public async Task<IActionResult> Archive(long memoryId, CancellationToken cancellationToken)
        => await _memory.ArchiveAsync(await RequireUser(cancellationToken), memoryId, cancellationToken)
            ? new OkObjectResult(new { archived = true })
            : new NotFoundObjectResult(new { message = "记忆不存在或已归档。" });

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken)
        => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
}
