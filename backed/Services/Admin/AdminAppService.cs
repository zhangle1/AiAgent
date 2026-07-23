using AiAgent.Backend.Dtos.Admin;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Admin;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/admin")]
public sealed class AdminAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _auth;
    private readonly IAdminService _admin;

    public AdminAppService(IHttpContextAccessor httpContextAccessor, IAuthService auth, IAdminService admin)
        => (_httpContextAccessor, _auth, _admin) = (httpContextAccessor, auth, admin);

    [HttpGet("users")]
    public async Task<List<AdminUserDto>> ListUsers(CancellationToken cancellationToken) => await _admin.ListUsersAsync(await RequireAdministrator(cancellationToken), cancellationToken);

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request, CancellationToken cancellationToken)
    {
        var (user, error) = await _admin.CreateUserAsync(await RequireAdministrator(cancellationToken), request, cancellationToken);
        return user == null ? new BadRequestObjectResult(new { message = error }) : new OkObjectResult(user);
    }

    [HttpPut("users/{userId}/projects")]
    public async Task<IActionResult> UpdateUserProjects(string userId, [FromBody] AdminUpdateUserProjectsRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.UpdateUserProjectsAsync(await RequireAdministrator(cancellationToken), userId, request.ProjectIds, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpGet("sessions")]
    public async Task<List<AdminSessionSummaryDto>> ListSessions([FromQuery(Name = "user_id")] string? userId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        => await _admin.ListSessionsAsync(await RequireAdministrator(cancellationToken), userId, limit, cancellationToken);

    [HttpGet("users/{userId}/sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string userId, string sessionId, CancellationToken cancellationToken)
    {
        var result = await _admin.GetSessionAsync(await RequireAdministrator(cancellationToken), userId, sessionId, cancellationToken);
        return result == null ? new NotFoundResult() : new OkObjectResult(result);
    }

    [HttpGet("usage")]
    public async Task<AdminUsageReportDto> Usage([FromQuery] string period = "day", [FromQuery] int days = 365, [FromQuery(Name = "user_id")] string? userId = null, CancellationToken cancellationToken = default)
        => await _admin.GetUsageReportAsync(await RequireAdministrator(cancellationToken), period, days, userId, cancellationToken);

    private async Task<AuthenticatedUser> RequireAdministrator(CancellationToken cancellationToken)
    {
        var user = await _auth.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        return user;
    }
}
