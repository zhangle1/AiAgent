using AiAgent.Backend.Dtos.Admin;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using ClosedXML.Excel;
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

    [HttpPut("users/{userId}/status")]
    public async Task<IActionResult> UpdateUserStatus(string userId, [FromBody] AdminUpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.UpdateUserStatusAsync(await RequireAdministrator(cancellationToken), userId, request.IsDisabled, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpGet("users/import-template")]
    public async Task<IActionResult> DownloadUserImportTemplate(CancellationToken cancellationToken)
    {
        await RequireAdministrator(cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("用户导入模板");
        var headers = new[] { "账号", "用户别名", "初始密码", "可提交代码", "项目ID列表" };
        for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Cell(2, 1).Value = "zhangsan";
        sheet.Cell(2, 2).Value = "张三";
        sheet.Cell(2, 3).Value = "ChangeMe123";
        sheet.Cell(2, 4).Value = "否";
        sheet.Cell(2, 5).Value = "1,2";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("DBEAFE");
        sheet.Columns().AdjustToContents();
        sheet.Column(5).Width = Math.Max(sheet.Column(5).Width, 18);
        sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new FileContentResult(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") { FileDownloadName = "用户导入模板.xlsx" };
    }

    [HttpPost("users/import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportUsers([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var administrator = await RequireAdministrator(cancellationToken);
        if (file == null || file.Length == 0 || !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return new BadRequestObjectResult(new { message = "Please upload a non-empty .xlsx file." });
        if (file.Length > 5 * 1024 * 1024) return new BadRequestObjectResult(new { message = "The import file must not exceed 5 MB." });

        var result = new AdminUserImportResultDto();
        try
        {
            await using var input = file.OpenReadStream();
            using var workbook = new XLWorkbook(input);
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("The workbook does not contain a worksheet.");
            var headerMap = sheet.Row(1).CellsUsed().ToDictionary(cell => cell.GetString().Trim(), cell => cell.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
            if (!headerMap.TryGetValue("账号", out var usernameColumn) && !headerMap.TryGetValue("username", out usernameColumn))
                return new BadRequestObjectResult(new { message = "The template must contain the 账号 (or username) column." });
            headerMap.TryGetValue("用户别名", out var aliasColumn); if (aliasColumn == 0) headerMap.TryGetValue("alias", out aliasColumn);
            headerMap.TryGetValue("初始密码", out var passwordColumn); if (passwordColumn == 0) headerMap.TryGetValue("password", out passwordColumn);
            headerMap.TryGetValue("可提交代码", out var commitColumn); if (commitColumn == 0) headerMap.TryGetValue("can_commit_code", out commitColumn);
            headerMap.TryGetValue("项目ID列表", out var projectColumn); if (projectColumn == 0) headerMap.TryGetValue("project_ids", out projectColumn);
            if (passwordColumn == 0) return new BadRequestObjectResult(new { message = "The template must contain the 初始密码 (or password) column." });

            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var username = row.Cell(usernameColumn).GetString().Trim();
                if (string.IsNullOrWhiteSpace(username)) continue;
                var projects = projectColumn == 0 ? [] : row.Cell(projectColumn).GetString().Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => long.TryParse(value, out var id) ? id : -1).ToList();
                if (projects.Any(id => id <= 0)) { result.Errors.Add($"第 {row.RowNumber()} 行：项目ID列表格式不正确。"); continue; }
                var commit = commitColumn != 0 && IsTrue(row.Cell(commitColumn).GetString());
                var request = new AdminCreateUserRequest { Username = username, Alias = aliasColumn == 0 ? null : row.Cell(aliasColumn).GetString(), Password = row.Cell(passwordColumn).GetString(), ProjectIds = projects, CanCommitCode = commit };
                var (created, error) = await _admin.CreateUserAsync(administrator, request, cancellationToken);
                if (created == null) result.Errors.Add($"第 {row.RowNumber()} 行（{username}）：{error}"); else result.CreatedCount++;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            return new BadRequestObjectResult(new { message = "Unable to read the Excel file. Please use the downloaded template.", detail = exception.Message });
        }
        return new OkObjectResult(result);
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

    private static bool IsTrue(string value) => value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "y" or "是" or "启用";
}
