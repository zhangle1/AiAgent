using AiAgent.Backend.Dtos.Admin;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
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
    private readonly IChatUploadLibraryService _uploads;

    public AdminAppService(IHttpContextAccessor httpContextAccessor, IAuthService auth, IAdminService admin, IChatUploadLibraryService uploads)
        => (_httpContextAccessor, _auth, _admin, _uploads) = (httpContextAccessor, auth, admin, uploads);

    [HttpGet("users")]
    public async Task<List<AdminUserDto>> ListUsers(CancellationToken cancellationToken) => await _admin.ListUsersAsync(await RequireAdministrator(cancellationToken), cancellationToken);

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request, CancellationToken cancellationToken)
    {
        var (user, error) = await _admin.CreateUserAsync(await RequireAdministrator(cancellationToken), request, cancellationToken);
        return user == null ? new BadRequestObjectResult(new { message = error }) : new OkObjectResult(user);
    }

    [HttpPut("users/{userId}/alias")]
    public async Task<IActionResult> UpdateUserAlias(string userId, [FromBody] AdminUpdateUserAliasRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.UpdateUserAliasAsync(await RequireAdministrator(cancellationToken), userId, request.Alias, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpPost("users/{userId}/reset-password")]
    public async Task<IActionResult> ResetUserPassword(string userId, [FromBody] AdminResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.ResetUserPasswordAsync(await RequireAdministrator(cancellationToken), userId, request.Password, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpPut("users/{userId}/projects")]
    public async Task<IActionResult> UpdateUserProjects(string userId, [FromBody] AdminUpdateUserProjectsRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.UpdateUserProjectsAsync(await RequireAdministrator(cancellationToken), userId, request.ProjectIds, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpPut("users/{userId}/code-commit-permission")]
    public async Task<IActionResult> UpdateUserCodeCommitPermission(string userId, [FromBody] AdminUpdateUserCodeCommitPermissionRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.UpdateUserCodeCommitPermissionAsync(await RequireAdministrator(cancellationToken), userId, request.CanCommitCode, cancellationToken);
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

    [HttpGet("uploads")]
    public async Task<List<ChatUploadFileDto>> ListUploads([FromQuery(Name = "user_id")] string? userId, [FromQuery] string? keyword, [FromQuery] string? kind, [FromQuery(Name = "session_id")] string? sessionId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var administrator = await RequireAdministrator(cancellationToken);
        var uploads = await _uploads.ListAsync(administrator, userId, keyword, kind, sessionId, limit, cancellationToken);
        var users = (await _admin.ListUsersAsync(administrator, cancellationToken)).ToDictionary(item => item.Id, item => item.Alias ?? item.Username, StringComparer.Ordinal);
        foreach (var upload in uploads) upload.UploaderName = upload.UploaderId != null && users.TryGetValue(upload.UploaderId, out var name) ? name : upload.UploaderId;
        return uploads;
    }

    [HttpGet("uploads/{attachmentId}/content")]
    public async Task<IActionResult> OpenUpload([FromRoute] string attachmentId, [FromQuery(Name = "user_id")] string? userId, CancellationToken cancellationToken)
    {
        var content = await _uploads.OpenAsync(await RequireAdministrator(cancellationToken), userId, attachmentId, cancellationToken);
        return content == null
            ? new NotFoundResult()
            : new FileStreamResult(new FileStream(content.Path, FileMode.Open, FileAccess.Read, FileShare.Read), content.ContentType) { EnableRangeProcessing = true };
    }

    private async Task<AuthenticatedUser> RequireAdministrator(CancellationToken cancellationToken)
    {
        var user = await _auth.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        return user;
    }
}
