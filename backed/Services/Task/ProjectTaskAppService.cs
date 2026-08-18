using System.Net.Http.Headers;
using System.Text.Json;
using AiAgent.Backend.Dtos.Task;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Entities.Task;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace AiAgent.Backend.Services.Task;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/project-tasks")]
public sealed class ProjectTaskAppService : IDynamicApiController
{
    private readonly ISqlSugarClient _db; private readonly IHttpContextAccessor _context; private readonly IAuthService _auth; private readonly IDataProtector _protector; private readonly IHttpClientFactory _http;
    public ProjectTaskAppService(ISqlSugarClient db, IHttpContextAccessor context, IAuthService auth, IDataProtectionProvider protection, IHttpClientFactory http) => (_db, _context, _auth, _protector, _http) = (db, context, auth, protection.CreateProtector("AiAgent.GitAccounts.AccessToken.v1"), http);

    [HttpGet]
    public async Task<object> List([FromQuery] long? projectId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        var rows = _db.Queryable<AiProjectTask>().Where(x => x.UserId == user.Id && !x.IsDeleted && (!projectId.HasValue || x.CodeProjectId == projectId)).OrderByDescending(x => x.UpdatedAt).ToList();
        var projectIds = rows.Where(x => x.CodeProjectId.HasValue).Select(x => x.CodeProjectId!.Value).Distinct().ToList();
        var names = projectIds.Count == 0 ? new Dictionary<long, string>() : _db.Queryable<AiCodeProject>().Where(x => projectIds.Contains(x.Id) && !x.IsDeleted).ToList().ToDictionary(x => x.Id, x => x.DisplayName);
        return new { tasks = rows.Select(x => Dto(x, names.TryGetValue(x.CodeProjectId ?? 0, out var name) ? name : null)) };
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectTaskRequest request, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 512) return new BadRequestObjectResult(new { message = "任务标题不能为空且最多 512 个字符。" });
        if (request.ProjectId.HasValue && !_db.Queryable<AiCodeProject>().Any(x => x.Id == request.ProjectId && !x.IsDeleted)) return new BadRequestObjectResult(new { message = "所选项目不存在。" });
        var task = new AiProjectTask { UserId = user.Id, CodeProjectId = request.ProjectId, Title = request.Title.Trim(), Description = Trim(request.Description, 20000), Status = "open", Source = "local", UpdatedAt = DateTime.UtcNow };
        _db.Insertable(task).ExecuteCommand();
        return new OkObjectResult(new { task = Dto(task, request.ProjectId.HasValue ? _db.Queryable<AiCodeProject>().Where(x => x.Id == request.ProjectId).Select(x => x.DisplayName).First() : null) });
    }

    [HttpPost("sync/gitee")]
    public async Task<IActionResult> SyncGitee([FromQuery] long projectId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (!_db.Queryable<AiCodeProject>().Any(x => x.Id == projectId && !x.IsDeleted)) return new NotFoundObjectResult(new { message = "所选项目不存在。" });
        var account = _db.Queryable<AiGitAccount>().First(x => x.UserId == user.Id && x.Provider == "gitee" && x.IsActive && !x.IsDeleted);
        if (account is null || string.IsNullOrWhiteSpace(account.AccessTokenProtected)) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用含令牌的 Gitee 账户。" });
        string token; try { token = _protector.Unprotect(account.AccessTokenProtected); } catch { return new BadRequestObjectResult(new { message = "Gitee 令牌无法读取，请重新保存该账户。" }); }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gitee.com/api/v5/user/issues?state=open&sort=updated&direction=desc&per_page=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token); request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
        using var response = await _http.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return new ObjectResult(new { message = $"Gitee 返回 HTTP {(int)response.StatusCode}，请检查令牌的任务读取权限。" }) { StatusCode = 502 };
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var count = 0;
        foreach (var issue in json.RootElement.EnumerateArray())
        {
            var id = Text(issue, "id"); if (string.IsNullOrEmpty(id)) continue;
            var existing = _db.Queryable<AiProjectTask>().First(x => x.UserId == user.Id && x.CodeProjectId == projectId && x.Source == "gitee" && x.ExternalId == id && !x.IsDeleted);
            if (existing is null) { existing = new AiProjectTask { UserId = user.Id, CodeProjectId = projectId, Source = "gitee", ExternalId = id, CreatedAt = DateTime.UtcNow }; _db.Insertable(existing).ExecuteCommand(); }
            existing.Title = Text(issue, "title") ?? "未命名任务"; existing.Description = Text(issue, "body"); existing.Status = Text(issue, "state") ?? "open"; existing.Assignee = issue.TryGetProperty("assignee", out var a) ? Text(a, "name") ?? Text(a, "login") : null; existing.ExternalUrl = Text(issue, "html_url"); existing.ExternalUpdatedAt = Date(issue, "updated_at"); existing.UpdatedAt = DateTime.UtcNow;
            _db.Updateable(existing).UpdateColumns(x => new { x.Title, x.Description, x.Status, x.Assignee, x.ExternalUrl, x.ExternalUpdatedAt, x.UpdatedAt }).ExecuteCommand(); count++;
        }
        return new OkObjectResult(new { synced = count });
    }
    private async Task<AuthenticatedUser> User(CancellationToken token) => await _auth.TryGetCurrentUserAsync(_context.HttpContext!, token) ?? throw new UnauthorizedAccessException();
    private static ProjectTaskDto Dto(AiProjectTask x, string? name) => new() { Id=x.Id, ProjectId=x.CodeProjectId, ProjectName=name, Source=x.Source, ExternalId=x.ExternalId, Title=x.Title, Description=x.Description, Status=x.Status, Assignee=x.Assignee, ExternalUrl=x.ExternalUrl, ExternalUpdatedAt=x.ExternalUpdatedAt, UpdatedAt=x.UpdatedAt };
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static DateTime? Date(JsonElement e, string name) => DateTime.TryParse(Text(e,name), out var value) ? value : null;
    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(max, value.Trim().Length)];
}
