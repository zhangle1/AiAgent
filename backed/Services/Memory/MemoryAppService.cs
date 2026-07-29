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
    private readonly IMemoryCandidateService _candidates;

    public MemoryAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IMemoryService memory, IMemoryCandidateService candidates)
        => (_httpContextAccessor, _authService, _memory, _candidates) = (httpContextAccessor, authService, memory, candidates);

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

    [HttpGet("candidates")]
    public async Task<object> ListCandidates([FromQuery(Name = "project_id")] long? projectId, [FromQuery] string? status = "pending", [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        => new { items = await _candidates.ListAsync(await RequireUser(cancellationToken), projectId, status, limit, cancellationToken) };

    [HttpPost("candidates/generate")]
    public async Task<IActionResult> GenerateCandidates([FromBody] GenerateMemoryCandidatesRequest request, CancellationToken cancellationToken)
    {
        var result = await _candidates.GenerateForSessionAsync(await RequireUser(cancellationToken), request.SessionId, cancellationToken);
        return result.Message is "A valid session id is required." or "The chat session is unavailable."
            ? new BadRequestObjectResult(new { message = result.Message })
            : new OkObjectResult(result);
    }

    [HttpPost("candidates/{candidateId:long}/approve")]
    public async Task<IActionResult> ApproveCandidate(long candidateId, [FromBody] ApproveMemoryCandidateRequest request, CancellationToken cancellationToken)
    {
        var result = await _candidates.ApproveAsync(await RequireUser(cancellationToken), candidateId, request, cancellationToken);
        return result.Item != null ? new OkObjectResult(result.Item) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpPost("candidates/{candidateId:long}/reject")]
    public async Task<IActionResult> RejectCandidate(long candidateId, [FromBody] RejectMemoryCandidateRequest request, CancellationToken cancellationToken)
    {
        var result = await _candidates.RejectAsync(await RequireUser(cancellationToken), candidateId, request, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { rejected = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken)
        => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
}
