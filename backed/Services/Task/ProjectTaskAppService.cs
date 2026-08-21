using System.Net.Http.Headers;
using System.Globalization;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Task;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Entities.Task;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace AiAgent.Backend.Services.ProjectTasks;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/project-tasks")]
public sealed class ProjectTaskAppService : IDynamicApiController
{
    private readonly ISqlSugarClient _db; private readonly IHttpContextAccessor _context; private readonly IAuthService _auth; private readonly IProjectAccessService _projectAccess; private readonly IDataProtector _protector; private readonly IHttpClientFactory _http; private readonly IChatImageAttachmentService _chatImages; private readonly IChatSessionService _sessions;
    private const string GiteeEnterprise = "yun_kun";
    private static readonly ConcurrentDictionary<string, TaskChatHandoff> ChatHandoffs = new(StringComparer.Ordinal);
    public ProjectTaskAppService(ISqlSugarClient db, IHttpContextAccessor context, IAuthService auth, IProjectAccessService projectAccess, IDataProtectionProvider protection, IHttpClientFactory http, IChatImageAttachmentService chatImages, IChatSessionService sessions) => (_db, _context, _auth, _projectAccess, _protector, _http, _chatImages, _sessions) = (db, context, auth, projectAccess, protection.CreateProtector("AiAgent.GitAccounts.AccessToken.v1"), http, chatImages, sessions);

    [HttpGet]
    public async Task<object> List([FromQuery] long? projectId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (projectId.HasValue && !_projectAccess.CanAccess(user, projectId.Value)) return new ForbidResult();
        var allowedProjectIds = _projectAccess.GetAccessibleProjectIds(user);
        if (allowedProjectIds.Count == 0) return new { tasks = Array.Empty<ProjectTaskDto>() };
        var taskQuery = _db.Queryable<AiProjectTask>()
            .Where(x => x.UserId == user.Id && x.IsDeleted == false)
            .Where(x => x.CodeProjectId != null)
            .Where(x => allowedProjectIds.Contains(x.CodeProjectId.Value));
        if (projectId.HasValue) taskQuery = taskQuery.Where(x => x.CodeProjectId == projectId.Value);
        var rows = taskQuery.OrderByDescending(x => x.UpdatedAt).ToList();
        var projectIds = rows.Where(x => x.CodeProjectId.HasValue).Select(x => x.CodeProjectId!.Value).Distinct().ToList();
        var names = projectIds.Count == 0 ? new Dictionary<long, string>() : _db.Queryable<AiCodeProject>().Where(x => projectIds.Contains(x.Id) && !x.IsDeleted).ToList().ToDictionary(x => x.Id, x => x.DisplayName);
        return new { tasks = rows.Select(x => Dto(x, names.TryGetValue(x.CodeProjectId ?? 0, out var name) ? name : null)) };
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectTaskRequest request, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (!request.ProjectId.HasValue) return new BadRequestObjectResult(new { message = "请先选择 AiAgent 项目。" });
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 512) return new BadRequestObjectResult(new { message = "任务标题不能为空且最多 512 个字符。" });
        if (!_projectAccess.CanAccess(user, request.ProjectId.Value)) return new ForbidResult();
        var link = request.GiteeIssue;
        if (link is not null && (string.IsNullOrWhiteSpace(link.Id) || !IsGiteeUrl(link.Url))) return new BadRequestObjectResult(new { message = "关联的 Gitee 任务信息不完整，请重新选择。" });
        var task = new AiProjectTask { UserId = user.Id, CodeProjectId = request.ProjectId, Title = request.Title.Trim(), Description = Trim(request.Description, 20000), Status = "todo", Source = "local", ExternalId = link is null ? null : $"gitee:{link.Id}", ExternalUrl = link?.Url, ExternalUpdatedAt = link?.UpdatedAt, Assignee = Trim(link?.Assignee, 64), UpdatedAt = DateTime.UtcNow };
        _db.Insertable(task).ExecuteCommand();
        return new OkObjectResult(new { task = Dto(task, _db.Queryable<AiCodeProject>().Where(x => x.Id == request.ProjectId).Select(x => x.DisplayName).First()) });
    }

    [HttpPatch("{taskId}")]
    public async Task<IActionResult> Update(long taskId, [FromBody] UpdateProjectTaskRequest request, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (!request.ProjectId.HasValue || request.ProjectId.Value <= 0) return new BadRequestObjectResult(new { message = "请选择关联的 AiAgent 项目。" });
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 512) return new BadRequestObjectResult(new { message = "任务标题不能为空且最多 512 个字符。" });
        if (!_projectAccess.CanAccess(user, request.ProjectId.Value)) return new ForbidResult();
        var task = _db.Queryable<AiProjectTask>().First(x => x.Id == taskId && x.UserId == user.Id && !x.IsDeleted);
        if (task is null) return new NotFoundObjectResult(new { message = "任务不存在或已删除。" });
        if (!task.CodeProjectId.HasValue || !_projectAccess.CanAccess(user, task.CodeProjectId.Value)) return new ForbidResult();
        task.CodeProjectId = request.ProjectId.Value;
        task.Title = request.Title.Trim();
        task.Description = Trim(request.Description, 20000);
        task.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(task).UpdateColumns(x => new { x.CodeProjectId, x.Title, x.Description, x.UpdatedAt }).ExecuteCommand();
        var projectName = _db.Queryable<AiCodeProject>().Where(x => x.Id == task.CodeProjectId && !x.IsDeleted).Select(x => x.DisplayName).First();
        return new OkObjectResult(new { task = Dto(task, projectName) });
    }

    [HttpPatch("{taskId}/status")]
    public async Task<IActionResult> UpdateStatus(long taskId, [FromBody] UpdateProjectTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        var status = NormalizeBoardStatus(request.Status);
        if (status is null) return new BadRequestObjectResult(new { message = "任务状态仅支持 todo、in_progress、in_review 或 done。" });
        var task = _db.Queryable<AiProjectTask>().First(x => x.Id == taskId && x.UserId == user.Id && !x.IsDeleted);
        if (task is null) return new NotFoundObjectResult(new { message = "任务不存在或已删除。" });
        if (!task.CodeProjectId.HasValue || !_projectAccess.CanAccess(user, task.CodeProjectId.Value)) return new ForbidResult();
        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(task).UpdateColumns(x => new { x.Status, x.UpdatedAt }).ExecuteCommand();
        return new OkObjectResult(new { task = Dto(task, _db.Queryable<AiCodeProject>().Where(x => x.Id == task.CodeProjectId).Select(x => x.DisplayName).First()) });
    }

    [HttpPost("{taskId}/chat-images")]
    public async Task<IActionResult> PrepareChatImages(long taskId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        var task = await TaskForUserAsync(user, taskId, cancellationToken);
        if (task.Result is not null) return task.Result;
        var images = await PrepareTaskChatImagesAsync(user, task.Task!, cancellationToken);
        return new OkObjectResult(new { attachments = images.Attachments.Select(AttachmentDto), warnings = images.Warnings });
    }

    [HttpPost("{taskId}/chat-handoff")]
    public async Task<IActionResult> CreateChatHandoff(long taskId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        var task = await TaskForUserAsync(user, taskId, cancellationToken);
        if (task.Result is not null) return task.Result;
        var item = task.Task!;
        var images = await PrepareTaskChatImagesAsync(user, item, cancellationToken);
        PruneChatHandoffs();
        var handoffId = Guid.NewGuid().ToString("N");
        ChatHandoffs[handoffId] = new TaskChatHandoff(user.Id, item.CodeProjectId!.Value, BuildChatPrompt(item), images.Attachments, images.Warnings, DateTime.UtcNow.AddMinutes(15));
        return new OkObjectResult(new { handoff_id = handoffId });
    }

    [HttpPost("chat-sessions")]
    public async Task<IActionResult> CreateChatSessions([FromBody] CreateTaskChatSessionsRequest request, CancellationToken cancellationToken)
    {
        var taskIds = request.TaskIds.Distinct().Take(50).ToList();
        if (taskIds.Count == 0) return new BadRequestObjectResult(new { message = "请至少选择一个任务。" });
        if (request.TaskIds.Distinct().Count() > taskIds.Count) return new BadRequestObjectResult(new { message = "一次最多从 50 个任务创建会话。" });

        var user = await User(cancellationToken);
        var tasks = new List<AiProjectTask>(taskIds.Count);
        foreach (var taskId in taskIds)
        {
            var result = await TaskForUserAsync(user, taskId, cancellationToken);
            if (result.Result is not null) return result.Result;
            tasks.Add(result.Task!);
        }

        var projectIds = tasks.Select(item => item.CodeProjectId!.Value).Distinct().ToList();
        var projectNames = _db.Queryable<AiCodeProject>().Where(item => projectIds.Contains(item.Id) && !item.IsDeleted).ToList().ToDictionary(item => item.Id, item => item.DisplayName);
        var created = new List<object>(tasks.Count);
        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectId = task.CodeProjectId!.Value;
            var projectName = projectNames.GetValueOrDefault(projectId);
            var images = await PrepareTaskChatImagesAsync(user, task, cancellationToken);
            var content = BuildChatPrompt(task, projectName);
            var session = await _sessions.CreateDraftAsync(user, projectId, $"任务：{task.Title}", content, images.Attachments, string.Join(" ", images.Warnings), cancellationToken);
            created.Add(new { task_id = task.Id, session, image_warning = string.Join(" ", images.Warnings) });
        }
        return new OkObjectResult(new { sessions = created });
    }

    [HttpGet("chat-handoffs/{handoffId}")]
    public async Task<IActionResult> GetChatHandoff(string handoffId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        PruneChatHandoffs();
        if (!ChatHandoffs.TryGetValue(handoffId, out var handoff) || !string.Equals(handoff.UserId, user.Id, StringComparison.Ordinal) || !_projectAccess.CanAccess(user, handoff.ProjectId)) return new NotFoundObjectResult(new { message = "工作项聊天交接已过期，请返回任务列表重新点击处理。" });
        return new OkObjectResult(new { handoff_id = handoffId, project_id = handoff.ProjectId, content = handoff.Content, image_attachments = handoff.Attachments.Select(AttachmentDto), image_warning = string.Join(" ", handoff.Warnings) });
    }

    [HttpDelete("{taskId}")]
    public async Task<IActionResult> Delete(long taskId, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        var task = _db.Queryable<AiProjectTask>().First(x => x.Id == taskId && x.UserId == user.Id && !x.IsDeleted);
        if (task is null) return new NotFoundObjectResult(new { message = "任务不存在或已删除。" });
        if (!task.CodeProjectId.HasValue || !_projectAccess.CanAccess(user, task.CodeProjectId.Value)) return new ForbidResult();
        var affected = _db.Updateable<AiProjectTask>().SetColumns(x => new AiProjectTask { IsDeleted = true, UpdatedAt = DateTime.UtcNow }).Where(x => x.Id == taskId && x.UserId == user.Id && !x.IsDeleted).ExecuteCommand();
        return affected > 0 ? new OkObjectResult(new { deleted = true }) : new NotFoundObjectResult(new { message = "任务不存在或已删除。" });
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm(Name = "project_id")] long projectId, [FromForm(Name = "field_mappings")] string? fieldMappings, IFormFile? file, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (file is null || file.Length == 0) return new BadRequestObjectResult(new { message = "请选择需要导入的 CSV 文件。" });
        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase)) return new BadRequestObjectResult(new { message = "仅支持导入 CSV 文件。" });
        if (file.Length > 100L * 1024 * 1024) return new BadRequestObjectResult(new { message = "CSV 文件不能超过 100 MB。" });
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();

        try
        {
            var mappings = ParseImportMappings(fieldMappings);
            return new OkObjectResult(ImportCsv(user, projectId, file, mappings, cancellationToken));
        }
        catch (CsvImportException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (DecoderFallbackException)
        {
            return new BadRequestObjectResult(new { message = "CSV 文件编码无法识别，请使用 UTF-8（可带 BOM）后重试。" });
        }
    }

    [HttpGet("gitee/enterprise/issues")]
    public async Task<IActionResult> ListEnterpriseIssues([FromQuery] string? state, [FromQuery] string? query, [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var user = await User(cancellationToken);
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用具备企业 Issue 读取权限的 Gitee 账户。" });
        var normalizedState = NormalizeEnterpriseState(state);
        if (state is not null && normalizedState is null) return new BadRequestObjectResult(new { message = "企业工作项状态仅支持 open、progressing、closed 或 rejected。" });
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var url = $"https://gitee.com/api/v5/enterprises/{GiteeEnterprise}/issues?page={normalizedPage}&per_page={normalizedPageSize}&sort=created&direction=desc";
        if (normalizedState is not null) url += $"&state={normalizedState}";
        var result = await GetGiteeArray(url, account, cancellationToken);
        if (result.Error is not null) return result.Error;
        var term = query?.Trim();
        var items = result.Items!.Select(ToEnterpriseIssueListItem).Where(item => item is not null).Cast<EnterpriseIssueListItem>();
        if (!string.IsNullOrWhiteSpace(term)) items = items.Where(item => item.Number.Contains(term, StringComparison.OrdinalIgnoreCase) || item.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || (item.Assignee?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        return new OkObjectResult(new { enterprise = GiteeEnterprise, page = normalizedPage, page_size = normalizedPageSize, has_more = result.Items.Count == normalizedPageSize, items = items.Select(item => new { number = item.Number, title = item.Title, status = item.Status, work_item_type = item.WorkItemType, assignee = item.Assignee, creator = item.Creator, updated_at = item.UpdatedAt, external_url = item.ExternalUrl }) });
    }

    [HttpGet("gitee/enterprise/issues/{number}")]
    public async Task<IActionResult> GetEnterpriseIssue(string number, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        var issueNumber = Trim(number, 64);
        if (issueNumber is null) return new BadRequestObjectResult(new { message = "工作项编号不能为空。" });
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用具备企业 Issue 读取权限的 Gitee 账户。" });
        var result = await GetGiteeObject($"https://gitee.com/api/v5/enterprises/{GiteeEnterprise}/issues/{Uri.EscapeDataString(issueNumber)}", account, cancellationToken);
        if (result.Error is not null) return result.Error;
        var detail = ToEnterpriseIssueDetail(result.Item!.Value);
        return new OkObjectResult(new { number = detail.Number, title = detail.Title, description = detail.Description, status = detail.Status, work_item_type = detail.WorkItemType, assignee = detail.Assignee, creator = detail.Creator, collaborators = detail.Collaborators, priority = detail.Priority, labels = detail.Labels, created_at = detail.CreatedAt, updated_at = detail.UpdatedAt, external_url = detail.ExternalUrl, attachments = detail.Attachments.Select(attachment => new { name = attachment.Name, url = attachment.Url, is_image = attachment.IsImage }) });
    }

    [HttpGet("gitee/enterprise/attachment")]
    public async Task<IActionResult> GetEnterpriseAttachment([FromQuery] string? url, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (!TryGiteeAttachmentUri(url, out var source)) return new BadRequestObjectResult(new { message = "附件地址无效，仅允许读取 Gitee HTTPS 附件。" });
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用具备企业 Issue 读取权限的 Gitee 账户。" });
        string token; try { token = _protector.Unprotect(account.AccessTokenProtected); } catch { return new BadRequestObjectResult(new { message = "Gitee 令牌无法读取，请重新保存该账户。" }); }
        EnterpriseAttachmentContent content;
        try { content = await DownloadGiteeAttachmentAsync(token, source, 20L * 1024 * 1024, cancellationToken); }
        catch (HttpRequestException ex) { return new ObjectResult(new { message = ex.Message }) { StatusCode = 502 }; }
        catch (InvalidOperationException ex) { return new ObjectResult(new { message = ex.Message }) { StatusCode = 413 }; }
        var mediaType = content.ContentType;
        if (string.IsNullOrWhiteSpace(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) mediaType = DetectImageContentType(content.Bytes) ?? mediaType;
        return new FileContentResult(content.Bytes, mediaType ?? "application/octet-stream");
    }

    [HttpPost("gitee/enterprise/link")]
    public async Task<IActionResult> LinkEnterpriseIssue([FromBody] EnterpriseIssueLinkRequest request, CancellationToken cancellationToken)
    {
        var user = await User(cancellationToken);
        if (request.ProjectId <= 0) return new BadRequestObjectResult(new { message = "请选择要关联的 AiAgent 项目。" });
        if (!_projectAccess.CanAccess(user, request.ProjectId)) return new ForbidResult();
        var issueNumber = Trim(request.Number, 64);
        if (issueNumber is null) return new BadRequestObjectResult(new { message = "工作项编号不能为空。" });
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用具备企业 Issue 读取权限的 Gitee 账户。" });
        var result = await GetGiteeObject($"https://gitee.com/api/v5/enterprises/{GiteeEnterprise}/issues/{Uri.EscapeDataString(issueNumber)}", account, cancellationToken);
        if (result.Error is not null) return result.Error;
        var item = ToEnterpriseWorkItem(result.Item!.Value);
        if (item is null) return new BadRequestObjectResult(new { message = "Gitee 工作项缺少可识别的编号，无法关联。" });
        var importResult = new CsvImportResult();
        _db.Ado.BeginTran();
        try { SaveImportBatch(user, request.ProjectId, [item], importResult, "gitee_enterprise_api"); _db.Ado.CommitTran(); }
        catch { _db.Ado.RollbackTran(); throw; }
        var task = _db.Queryable<AiProjectTask>().First(x => x.UserId == user.Id && x.CodeProjectId == request.ProjectId && x.WorkItemId == item.WorkItemId && !x.IsDeleted);
        var projectName = _db.Queryable<AiCodeProject>().Where(x => x.Id == request.ProjectId && !x.IsDeleted).Select(x => x.DisplayName).First();
        return new OkObjectResult(new { task = Dto(task, projectName), inserted = importResult.Inserted, updated = importResult.Updated });
    }

    [HttpGet("gitee/projects")]
    public async Task<IActionResult> ListGiteeProjects([FromQuery] string? query, [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var user = await User(cancellationToken);
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用含令牌的 Gitee 账户。" });
        var result = await GetGiteeArray($"https://gitee.com/api/v5/user/repos?page={NormalizePage(page)}&per_page={NormalizePageSize(pageSize)}&sort=updated&direction=desc", account, cancellationToken);
        if (result.Error is not null) return result.Error;
        var term = query?.Trim();
        var items = result.Items!.Where(item => string.IsNullOrWhiteSpace(term) || (Text(item, "full_name") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) || (Text(item, "name") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).Select(item => new { owner = item.TryGetProperty("owner", out var owner) ? Text(owner, "login") : null, name = Text(item, "path") ?? Text(item, "name"), display_name = Text(item, "full_name") ?? Text(item, "name") }).Where(item => !string.IsNullOrWhiteSpace(item.owner) && !string.IsNullOrWhiteSpace(item.name));
        return new OkObjectResult(new { items, page = NormalizePage(page), page_size = NormalizePageSize(pageSize), has_more = result.Items!.Count == NormalizePageSize(pageSize) });
    }

    [HttpGet("gitee/issues")]
    public async Task<IActionResult> ListGiteeIssues([FromQuery] string owner, [FromQuery] string repository, [FromQuery] string? assignee, [FromQuery] string? state, [FromQuery] string? query, [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository)) return new BadRequestObjectResult(new { message = "请先选择 Gitee 项目。" });
        var user = await User(cancellationToken);
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用含令牌的 Gitee 账户。" });
        var normalizedState = state is "closed" or "all" ? state : "open";
        var url = $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner.Trim())}/{Uri.EscapeDataString(repository.Trim())}/issues?state={normalizedState}&page={NormalizePage(page)}&per_page={NormalizePageSize(pageSize)}&sort=updated&direction=desc";
        if (!string.IsNullOrWhiteSpace(assignee)) url += $"&assignee={Uri.EscapeDataString(assignee.Trim())}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&search={Uri.EscapeDataString(query.Trim())}";
        var result = await GetGiteeArray(url, account, cancellationToken);
        if (result.Error is not null) return result.Error;
        var term = query?.Trim();
        var items = result.Items!.Where(item => string.IsNullOrWhiteSpace(term) || (Text(item, "title") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) || (Text(item, "number") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).Select(item => new { id = Text(item, "id"), number = Text(item, "number"), title = Text(item, "title"), state = Text(item, "state"), assignee = item.TryGetProperty("assignee", out var a) ? Text(a, "name") ?? Text(a, "login") : null, url = Text(item, "html_url"), updated_at = Date(item, "updated_at") }).Where(item => !string.IsNullOrWhiteSpace(item.id) && !string.IsNullOrWhiteSpace(item.url));
        return new OkObjectResult(new { items, page = NormalizePage(page), page_size = NormalizePageSize(pageSize), has_more = result.Items!.Count == NormalizePageSize(pageSize) });
    }

    [HttpGet("gitee/members")]
    public async Task<IActionResult> ListGiteeMembers([FromQuery] string owner, [FromQuery] string repository, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository)) return new BadRequestObjectResult(new { message = "请先选择 Gitee 项目。" });
        var user = await User(cancellationToken);
        var account = ActiveGiteeAccount(user);
        if (account is null) return new BadRequestObjectResult(new { message = "请先在 Git 管理中配置并启用含令牌的 Gitee 账户。" });
        var url = $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner.Trim())}/{Uri.EscapeDataString(repository.Trim())}/collaborators?page=1&per_page=100";
        var result = await GetGiteeArray(url, account, cancellationToken);
        if (result.Error is not null) return result.Error;
        var items = result.Items!.Select(item => new { login = Text(item, "login"), display_name = Text(item, "name") ?? Text(item, "login") }).Where(item => !string.IsNullOrWhiteSpace(item.login));
        return new OkObjectResult(new { items });
    }

    private async Task<AuthenticatedUser> User(CancellationToken token) => await _auth.TryGetCurrentUserAsync(_context.HttpContext!, token) ?? throw new UnauthorizedAccessException();
    private Task<(AiProjectTask? Task, IActionResult? Result)> TaskForUserAsync(AuthenticatedUser user, long taskId, CancellationToken cancellationToken)
    {
        var task = _db.Queryable<AiProjectTask>().First(x => x.Id == taskId && x.UserId == user.Id && !x.IsDeleted);
        if (task is null) return Task.FromResult<(AiProjectTask?, IActionResult?)>((null, new NotFoundObjectResult(new { message = "任务不存在或已删除。" })));
        if (!task.CodeProjectId.HasValue || !_projectAccess.CanAccess(user, task.CodeProjectId.Value)) return Task.FromResult<(AiProjectTask?, IActionResult?)>((null, new ForbidResult()));
        return Task.FromResult<(AiProjectTask?, IActionResult?)>((task, null));
    }
    private async Task<TaskChatImagePreparation> PrepareTaskChatImagesAsync(AuthenticatedUser user, AiProjectTask task, CancellationToken cancellationToken)
    {
        var sources = new List<EnterpriseIssueAttachment>();
        ExtractMarkdownImageAttachments(sources, task.Description);
        var attachments = new List<ChatImageAttachmentDto>();
        var warnings = new List<string>();
        if (sources.Count == 0) return new TaskChatImagePreparation(attachments, warnings);
        var account = ActiveGiteeAccount(user);
        if (account is null) return new TaskChatImagePreparation(attachments, ["未配置具备 Gitee 附件读取权限的账户，任务图片未携带。"]);
        string token;
        try { token = _protector.Unprotect(account.AccessTokenProtected); }
        catch { return new TaskChatImagePreparation(attachments, ["Gitee 令牌无法读取，任务图片未携带。"]); }
        foreach (var source in sources.Take(4))
        {
            if (!TryGiteeAttachmentUri(source.Url, out var uri)) { warnings.Add($"“{source.Name}”不是可下载的 Gitee 图片地址，已跳过。"); continue; }
            try
            {
                var content = await DownloadGiteeAttachmentAsync(token, uri, 10L * 1024 * 1024, cancellationToken);
                var imageType = DetectImageContentType(content.Bytes);
                if (imageType is null) { warnings.Add($"“{source.Name}”不是支持的 PNG、JPEG、WebP 或 GIF 图片，已跳过。"); continue; }
                await using var stream = new MemoryStream(content.Bytes, writable: false);
                var file = new FormFile(stream, 0, stream.Length, "file", SafeAttachmentFileName(source.Name, uri)) { Headers = new HeaderDictionary(), ContentType = imageType };
                attachments.Add(await _chatImages.SaveAsync(user, file, cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                warnings.Add($"“{source.Name}”下载失败：{ex.Message}");
            }
        }
        if (sources.Count > 4) warnings.Add("工作项图片超过 4 张，仅携带前 4 张到本次聊天。");
        return new TaskChatImagePreparation(attachments, warnings);
    }
    private static string BuildChatPrompt(AiProjectTask task, string? projectName = null) => $"请处理以下工作项，并先结合当前项目代码评估实施方案。\n\n项目上下文：{projectName ?? "当前关联项目"}\n任务：{task.Title}{(string.IsNullOrWhiteSpace(task.WorkItemId) ? string.Empty : $"\n工作项 ID：{task.WorkItemId}")}{(string.IsNullOrWhiteSpace(task.Description) ? string.Empty : $"\n\n任务详情：\n{task.Description}")}{(string.IsNullOrWhiteSpace(task.ExternalUrl) ? string.Empty : $"\n原始链接：{task.ExternalUrl}")}";
    private static object AttachmentDto(ChatImageAttachmentDto attachment) => new { id = attachment.Id, file_name = attachment.FileName, content_type = attachment.ContentType, size_bytes = attachment.SizeBytes };
    private static void PruneChatHandoffs()
    {
        var now = DateTime.UtcNow;
        foreach (var item in ChatHandoffs) if (item.Value.ExpiresAt <= now) ChatHandoffs.TryRemove(item.Key, out _);
    }
    private AiGitAccount? ActiveGiteeAccount(AuthenticatedUser user) => _db.Queryable<AiGitAccount>().First(x => x.UserId == user.Id && x.Provider == "gitee" && x.IsActive && !x.IsDeleted && !string.IsNullOrEmpty(x.AccessTokenProtected));
    private async Task<(List<JsonElement>? Items, IActionResult? Error)> GetGiteeArray(string url, AiGitAccount account, CancellationToken cancellationToken)
    {
        string token; try { token = _protector.Unprotect(account.AccessTokenProtected); } catch { return (null, new BadRequestObjectResult(new { message = "Gitee 令牌无法读取，请重新保存该账户。" })); }
        var requestUri = new UriBuilder(url);
        var separator = string.IsNullOrEmpty(requestUri.Query) ? string.Empty : "&";
        requestUri.Query = $"{requestUri.Query.TrimStart('?')}{separator}access_token={Uri.EscapeDataString(token)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri.Uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
        using var response = await _http.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return (null, new ObjectResult(new { message = $"Gitee 返回 HTTP {(int)response.StatusCode}，请检查令牌的项目与 Issue 读取权限。" }) { StatusCode = 502 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.ValueKind == JsonValueKind.Array ? (json.RootElement.EnumerateArray().Select(item => item.Clone()).ToList(), null) : (null, new ObjectResult(new { message = "Gitee 返回了无法识别的列表数据。" }) { StatusCode = 502 });
    }
    private async Task<(JsonElement? Item, IActionResult? Error)> GetGiteeObject(string url, AiGitAccount account, CancellationToken cancellationToken)
    {
        string token; try { token = _protector.Unprotect(account.AccessTokenProtected); } catch { return (null, new BadRequestObjectResult(new { message = "Gitee 令牌无法读取，请重新保存该账户。" })); }
        var requestUri = new UriBuilder(url);
        var separator = string.IsNullOrEmpty(requestUri.Query) ? string.Empty : "&";
        requestUri.Query = $"{requestUri.Query.TrimStart('?')}{separator}access_token={Uri.EscapeDataString(token)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri.Uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
        using var response = await _http.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return (null, new ObjectResult(new { message = $"Gitee 返回 HTTP {(int)response.StatusCode}，请检查令牌的项目与 Issue 读取权限。" }) { StatusCode = 502 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.ValueKind == JsonValueKind.Object ? (json.RootElement.Clone(), null) : (null, new ObjectResult(new { message = "Gitee 返回了无法识别的工作项详情。" }) { StatusCode = 502 });
    }
    private static int NormalizePage(int page) => Math.Max(1, page);
    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);
    private static string? NormalizeEnterpriseState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var state = value.Trim().ToLowerInvariant();
        return state is "open" or "progressing" or "closed" or "rejected" ? state : null;
    }
    private static string? NormalizeBoardStatus(string? value)
    {
        var status = value?.Trim().ToLowerInvariant();
        return status is "todo" or "in_progress" or "in_review" or "done" ? status : null;
    }
    private static bool IsGiteeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase);
    private static ProjectTaskDto Dto(AiProjectTask x, string? name) => new() { Id=x.Id, ProjectId=x.CodeProjectId, ProjectName=name, Source=x.Source, ExternalId=x.ExternalId, WorkItemId=x.WorkItemId, WorkItemType=x.WorkItemType, Title=x.Title, Description=x.Description, Status=x.Status, Creator=x.Creator, Assignee=x.Assignee, Collaborators=x.Collaborators, Priority=x.Priority, Labels=x.Labels, ExternalUrl=x.ExternalUrl, ExternalCreatedAt=x.ExternalCreatedAt, ExternalUpdatedAt=x.ExternalUpdatedAt, UpdatedAt=x.UpdatedAt };
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.String ? p.GetString() : p.ValueKind == JsonValueKind.Number ? p.GetRawText() : null : null;
    private static DateTime? Date(JsonElement e, string name) => DateTime.TryParse(Text(e,name), out var value) ? value : null;
    private static string? NestedText(JsonElement e, string property, params string[] names) => e.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.Object ? names.Select(name => Text(nested, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) : null;
    private static string? Person(JsonElement e, string property) => NestedText(e, property, "remark", "name", "login", "username");
    private static string? ListText(JsonElement e, string property)
    {
        if (!e.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return null;
        var items = values.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ValueKind == JsonValueKind.Object ? Text(item, "name") ?? Text(item, "title") ?? Text(item, "login") : null).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return items.Count == 0 ? null : Trim(string.Join("、", items), 1024);
    }
    private static ImportedTaskRow? ToEnterpriseWorkItem(JsonElement item)
    {
        var workItemId = Trim(Text(item, "ident") ?? Text(item, "number") ?? Text(item, "id"), 64);
        if (workItemId is null) return null;
        return new ImportedTaskRow(workItemId, Trim(Text(item, "title"), 512), Trim(Text(item, "body") ?? Text(item, "description"), 20000), Trim(NestedText(item, "issue_state", "title") ?? Text(item, "state"), 32), Trim(NestedText(item, "issue_type", "title") ?? Text(item, "issue_type"), 64), Trim(Person(item, "creator"), 64), Trim(Person(item, "assignee"), 64), ListText(item, "collaborators"), Trim(NestedText(item, "priority", "title", "name") ?? Text(item, "priority"), 32), ListText(item, "labels"), Date(item, "created_at"), Date(item, "updated_at"), Trim(Text(item, "html_url"), 1024));
    }
    private static EnterpriseIssueListItem? ToEnterpriseIssueListItem(JsonElement item)
    {
        var number = Trim(Text(item, "number") ?? Text(item, "ident") ?? Text(item, "id"), 64);
        if (number is null) return null;
        return new EnterpriseIssueListItem(number, Trim(Text(item, "title"), 512) ?? $"工作项 #{number}", Trim(NestedText(item, "issue_state", "title") ?? Text(item, "state"), 32), Trim(NestedText(item, "issue_type", "title") ?? Text(item, "issue_type"), 64), Trim(Person(item, "assignee"), 64), Trim(Person(item, "creator"), 64), Date(item, "updated_at"), Trim(Text(item, "html_url"), 1024));
    }
    private static EnterpriseIssueDetail ToEnterpriseIssueDetail(JsonElement item)
    {
        var number = Trim(Text(item, "number") ?? Text(item, "ident") ?? Text(item, "id"), 64) ?? string.Empty;
        var description = Trim(Text(item, "body") ?? Text(item, "description"), 20000);
        return new EnterpriseIssueDetail(number, Trim(Text(item, "title"), 512) ?? $"工作项 #{number}", description, Trim(NestedText(item, "issue_state", "title") ?? Text(item, "state"), 32), Trim(NestedText(item, "issue_type", "title") ?? Text(item, "issue_type"), 64), Trim(Person(item, "assignee"), 64), Trim(Person(item, "creator"), 64), ListText(item, "collaborators"), Trim(NestedText(item, "priority", "title", "name") ?? Text(item, "priority"), 32), ListText(item, "labels"), Date(item, "created_at"), Date(item, "updated_at"), Trim(Text(item, "html_url"), 1024), ExtractAttachments(item, description));
    }
    private static List<EnterpriseIssueAttachment> ExtractAttachments(JsonElement item, string? description)
    {
        var attachments = new List<EnterpriseIssueAttachment>();
        foreach (var property in new[] { "attachments", "attach_files", "files" })
        {
            if (!item.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) continue;
            foreach (var value in values.EnumerateArray())
            {
                var url = value.ValueKind == JsonValueKind.String ? value.GetString() : Text(value, "url") ?? Text(value, "download_url") ?? Text(value, "file_url") ?? Text(value, "html_url");
                var contentType = value.ValueKind == JsonValueKind.Object ? Text(value, "content_type") ?? Text(value, "mime_type") ?? Text(value, "media_type") ?? Text(value, "type") : null;
                AddAttachment(attachments, Text(value, "name") ?? Text(value, "filename"), url, contentType);
            }
        }
        ExtractMarkdownImageAttachments(attachments, description);
        return attachments;
    }
    private static void ExtractMarkdownImageAttachments(List<EnterpriseIssueAttachment> attachments, string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return;
        var start = 0;
        while (start < description.Length && (start = description.IndexOf("![", start, StringComparison.Ordinal)) >= 0)
        {
            var urlStart = description.IndexOf("](", start + 2, StringComparison.Ordinal);
            if (urlStart < 0) break;
            var end = description.IndexOf(')', urlStart + 2);
            if (end < 0) break;
            AddAttachment(attachments, description[(start + 2)..urlStart], MarkdownDestination(description[(urlStart + 2)..end]), imageHint: true);
            start = end + 1;
        }
    }
    private static string? MarkdownDestination(string destination)
    {
        var value = destination.Trim();
        if (value.Length == 0) return null;
        if (value[0] == '<')
        {
            var closingIndex = value.IndexOf('>');
            if (closingIndex > 1) return value[1..closingIndex].Trim();
        }
        var titleIndex = value.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        return titleIndex < 0 ? value : value[..titleIndex];
    }
    private async Task<EnterpriseAttachmentContent> DownloadGiteeAttachmentAsync(string token, Uri source, long maximumBytes, CancellationToken cancellationToken)
    {
        var requestUri = new UriBuilder(source);
        var separator = string.IsNullOrEmpty(requestUri.Query) ? string.Empty : "&";
        requestUri.Query = $"{requestUri.Query.TrimStart('?')}{separator}access_token={Uri.EscapeDataString(token)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri.Uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
        using var response = await _http.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gitee 附件返回 HTTP {(int)response.StatusCode}，请检查附件访问权限。");
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes) throw new InvalidOperationException($"附件超过 {maximumBytes / 1024 / 1024} MB，未读取。");
        var bytes = await ReadAttachmentWithLimitAsync(response.Content, maximumBytes, cancellationToken);
        return new EnterpriseAttachmentContent(bytes, response.Content.Headers.ContentType?.MediaType);
    }
    private static async Task<byte[]> ReadAttachmentWithLimitAsync(HttpContent content, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await using var target = new MemoryStream();
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) return target.ToArray();
            copied += read;
            if (copied > maximumBytes) throw new InvalidOperationException($"附件超过 {maximumBytes / 1024 / 1024} MB，未读取。");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
    private static bool TryGiteeAttachmentUri(string? value, out Uri source)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps && (string.Equals(parsed.Host, "gitee.com", StringComparison.OrdinalIgnoreCase) || parsed.Host.EndsWith(".gitee.com", StringComparison.OrdinalIgnoreCase))) { source = parsed; return true; }
        source = null!;
        return false;
    }
    private static string SafeAttachmentFileName(string? name, Uri source)
    {
        var candidate = string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(Path.GetExtension(name)) ? Path.GetFileName(source.AbsolutePath) : Path.GetFileName(name);
        return string.IsNullOrWhiteSpace(candidate) ? "gitee-image" : candidate[..Math.Min(candidate.Length, 160)];
    }
    private static void AddAttachment(List<EnterpriseIssueAttachment> attachments, string? name, string? url, string? contentType = null, bool imageHint = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || attachments.Any(item => string.Equals(item.Url, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))) return;
        var attachmentName = Trim(name, 256) ?? "附件";
        var isImage = imageHint || IsImageContentType(contentType) || IsImageName(uri.AbsolutePath) || IsImageName(attachmentName);
        attachments.Add(new EnterpriseIssueAttachment(attachmentName, uri.AbsoluteUri, isImage));
    }
    private static bool IsImageContentType(string? value) => !string.IsNullOrWhiteSpace(value) && (value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "image", StringComparison.OrdinalIgnoreCase));
    private static bool IsImageName(string value) => value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".avif", StringComparison.OrdinalIgnoreCase);
    private static string? DetectImageContentType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'8') return "image/gif";
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P') return "image/webp";
        return null;
    }
    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(max, value.Trim().Length)];

    private object ImportCsv(AuthenticatedUser user, long projectId, IFormFile file, IReadOnlyDictionary<string, string> mappings, CancellationToken cancellationToken)
    {
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var headers = ReadCsvRecord(reader, cancellationToken) ?? throw new CsvImportException("CSV 文件没有表头。" );
        if (headers.Count == 0) throw new CsvImportException("CSV 文件没有表头。" );
        headers[0] = headers[0].TrimStart('\ufeff');
        var headerIndexes = headers.Select((header, index) => new { Header = header.Trim(), Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Header))
            .GroupBy(x => x.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);
        var indexes = mappings.ToDictionary(pair => pair.Key, pair => headerIndexes.TryGetValue(pair.Value, out var index) ? index : throw new CsvImportException($"找不到映射列“{pair.Value}”。"), StringComparer.OrdinalIgnoreCase);
        if (!indexes.ContainsKey("work_item_id")) throw new CsvImportException("必须将 CSV 的“工作项 ID”映射到本地“工作项 ID”。" );

        var result = new CsvImportResult();
        var batch = new List<ImportedTaskRow>(500);
        _db.Ado.BeginTran();
        try
        {
            List<string>? record;
            while ((record = ReadCsvRecord(reader, cancellationToken)) is not null)
            {
                result.TotalRows++;
                if (record.All(string.IsNullOrWhiteSpace)) continue;
                var item = ReadImportedTask(record, indexes, result, result.TotalRows);
                if (item is null) continue;
                batch.Add(item);
                if (batch.Count >= 500)
                {
                    SaveImportBatch(user, projectId, batch, result, "gitee_enterprise_csv");
                    batch.Clear();
                }
            }
            if (batch.Count > 0) SaveImportBatch(user, projectId, batch, result, "gitee_enterprise_csv");
            _db.Ado.CommitTran();
            return new { total_rows = result.TotalRows, inserted = result.Inserted, updated = result.Updated, skipped = result.Skipped, warnings = result.Warnings };
        }
        catch
        {
            _db.Ado.RollbackTran();
            throw;
        }
    }

    private void SaveImportBatch(AuthenticatedUser user, long projectId, IReadOnlyList<ImportedTaskRow> input, CsvImportResult result, string source)
    {
        var uniqueInput = input.GroupBy(x => x.WorkItemId, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToList();
        var ids = uniqueInput.Select(x => x.WorkItemId).ToList();
        var externalIds = ids.Select(x => ExternalId(source, x)).ToList();
        var csvExternalIds = ids.Select(x => ExternalId("gitee_enterprise_csv", x)).ToList();
        var legacyExternalIds = ids.Select(x => $"gitee:{x}").ToList();
        var existingRows = _db.Queryable<AiProjectTask>()
            .Where(x => x.UserId == user.Id && x.CodeProjectId == projectId && !x.IsDeleted && ((x.WorkItemId != null && ids.Contains(x.WorkItemId)) || (x.ExternalId != null && (externalIds.Contains(x.ExternalId) || csvExternalIds.Contains(x.ExternalId) || legacyExternalIds.Contains(x.ExternalId)))))
            .ToList();
        var existing = new Dictionary<string, AiProjectTask>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in existingRows)
        {
            if (!string.IsNullOrWhiteSpace(row.WorkItemId)) existing.TryAdd(row.WorkItemId, row);
            if (!string.IsNullOrWhiteSpace(row.ExternalId) && row.ExternalId.StartsWith("gitee-work-item:", StringComparison.OrdinalIgnoreCase)) existing.TryAdd(row.ExternalId[16..], row);
            if (!string.IsNullOrWhiteSpace(row.ExternalId) && row.ExternalId.StartsWith("gitee-enterprise-work-item:", StringComparison.OrdinalIgnoreCase)) existing.TryAdd(row.ExternalId[27..], row);
            if (!string.IsNullOrWhiteSpace(row.ExternalId) && row.ExternalId.StartsWith("gitee:", StringComparison.OrdinalIgnoreCase)) existing.TryAdd(row.ExternalId[6..], row);
        }

        var inserts = new List<AiProjectTask>();
        var updates = new List<AiProjectTask>();
        foreach (var item in uniqueInput)
        {
            if (existing.TryGetValue(item.WorkItemId, out var task))
            {
                ApplyImportedTask(task, item, source);
                updates.Add(task);
                result.Updated++;
                continue;
            }
            task = NewImportedTask(user.Id, projectId, item, source);
            existing[item.WorkItemId] = task;
            inserts.Add(task);
            result.Inserted++;
        }
        if (inserts.Count > 0) _db.Insertable(inserts).ExecuteCommand();
        if (updates.Count > 0)
        {
            _db.Updateable(updates).UpdateColumns(x => new { x.WorkItemId, x.WorkItemType, x.ExternalId, x.Source, x.Title, x.Description, x.Status, x.Creator, x.Assignee, x.Collaborators, x.Priority, x.Labels, x.ExternalUrl, x.ExternalCreatedAt, x.ExternalUpdatedAt, x.UpdatedAt }).ExecuteCommand();
        }
    }

    private static AiProjectTask NewImportedTask(string userId, long projectId, ImportedTaskRow item, string source) => new()
    {
        UserId = userId,
        CodeProjectId = projectId,
        WorkItemId = item.WorkItemId,
        ExternalId = ExternalId(source, item.WorkItemId),
        Source = source,
        Title = item.Title ?? $"工作项 #{item.WorkItemId}",
        Description = item.Description,
        Status = item.Status,
        WorkItemType = item.WorkItemType,
        Creator = item.Creator,
        Assignee = item.Assignee,
        Collaborators = item.Collaborators,
        Priority = item.Priority,
        Labels = item.Labels,
        ExternalUrl = item.ExternalUrl,
        ExternalCreatedAt = item.ExternalCreatedAt,
        ExternalUpdatedAt = item.ExternalUpdatedAt,
        UpdatedAt = DateTime.UtcNow
    };

    private static void ApplyImportedTask(AiProjectTask task, ImportedTaskRow item, string source)
    {
        task.WorkItemId = item.WorkItemId;
        task.ExternalId = ExternalId(source, item.WorkItemId);
        task.Source = source;
        if (item.Title is not null) task.Title = item.Title;
        if (item.Description is not null) task.Description = item.Description;
        if (item.Status is not null) task.Status = item.Status;
        if (item.WorkItemType is not null) task.WorkItemType = item.WorkItemType;
        if (item.Creator is not null) task.Creator = item.Creator;
        if (item.Assignee is not null) task.Assignee = item.Assignee;
        if (item.Collaborators is not null) task.Collaborators = item.Collaborators;
        if (item.Priority is not null) task.Priority = item.Priority;
        if (item.Labels is not null) task.Labels = item.Labels;
        if (item.ExternalUrl is not null) task.ExternalUrl = item.ExternalUrl;
        if (item.ExternalCreatedAt.HasValue) task.ExternalCreatedAt = item.ExternalCreatedAt;
        if (item.ExternalUpdatedAt.HasValue) task.ExternalUpdatedAt = item.ExternalUpdatedAt;
        task.UpdatedAt = DateTime.UtcNow;
    }

    private static ImportedTaskRow? ReadImportedTask(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> indexes, CsvImportResult result, int rowNumber)
    {
        string? Value(string target, int max) => indexes.TryGetValue(target, out var index) && index < row.Count ? Trim(row[index], max) : null;
        var workItemId = Value("work_item_id", 64);
        if (workItemId is null)
        {
            result.Skip(rowNumber, "工作项 ID 为空。");
            return null;
        }
        DateTime? DateValue(string target)
        {
            var value = Value(target, 64);
            if (value is null) return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return date;
            result.Warn(rowNumber, $"{target} 日期无法识别，已忽略。");
            return null;
        }
        return new ImportedTaskRow(workItemId, Value("title", 512), Value("description", 20000), Value("status", 32), Value("work_item_type", 64), Value("creator", 64), Value("assignee", 64), Value("collaborators", 1024), Value("priority", 32), Value("labels", 512), DateValue("created_at"), DateValue("updated_at"), null);
    }

    private static IReadOnlyDictionary<string, string> ParseImportMappings(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new CsvImportException("请完成 CSV 字段映射。" );
        Dictionary<string, string>? mappings;
        try { mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(value); }
        catch (JsonException) { throw new CsvImportException("字段映射格式不正确，请重新选择。" ); }
        if (mappings is null || mappings.Count == 0) throw new CsvImportException("请至少映射工作项 ID。" );
        var allowed = new HashSet<string>(["work_item_id", "work_item_type", "title", "description", "status", "creator", "assignee", "collaborators", "priority", "labels", "created_at", "updated_at"], StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, source) in mappings)
        {
            if (!allowed.Contains(target)) throw new CsvImportException($"不支持映射到字段“{target}”。" );
            if (!string.IsNullOrWhiteSpace(source)) result[target] = source.Trim();
        }
        return result;
    }

    private static List<string>? ReadCsvRecord(TextReader reader, CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasContent = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = reader.Read();
            if (code < 0)
            {
                if (!hasContent && fields.Count == 0 && field.Length == 0) return null;
                if (inQuotes) throw new CsvImportException("CSV 存在未闭合的引号，请修正后重试。" );
                fields.Add(field.ToString());
                return fields;
            }
            var ch = (char)code;
            hasContent = true;
            if (inQuotes)
            {
                if (ch != '"') { field.Append(ch); continue; }
                if (reader.Peek() == '"') { reader.Read(); field.Append('"'); continue; }
                inQuotes = false;
                continue;
            }
            if (ch == '"' && field.Length == 0) { inQuotes = true; continue; }
            if (ch == ',') { fields.Add(field.ToString()); field.Clear(); continue; }
            if (ch == '\r')
            {
                if (reader.Peek() == '\n') reader.Read();
                fields.Add(field.ToString());
                return fields;
            }
            if (ch == '\n') { fields.Add(field.ToString()); return fields; }
            field.Append(ch);
        }
    }

    private static string ExternalId(string source, string workItemId) => source == "gitee_enterprise_api" ? $"gitee-enterprise-work-item:{workItemId}" : $"gitee-work-item:{workItemId}";
    private sealed record EnterpriseIssueListItem(string Number, string Title, string? Status, string? WorkItemType, string? Assignee, string? Creator, DateTime? UpdatedAt, string? ExternalUrl);
    private sealed record EnterpriseIssueAttachment(string Name, string Url, bool IsImage);
    private sealed record EnterpriseAttachmentContent(byte[] Bytes, string? ContentType);
    private sealed record TaskChatImagePreparation(List<ChatImageAttachmentDto> Attachments, List<string> Warnings);
    private sealed record TaskChatHandoff(string UserId, long ProjectId, string Content, List<ChatImageAttachmentDto> Attachments, List<string> Warnings, DateTime ExpiresAt);
    private sealed record EnterpriseIssueDetail(string Number, string Title, string? Description, string? Status, string? WorkItemType, string? Assignee, string? Creator, string? Collaborators, string? Priority, string? Labels, DateTime? CreatedAt, DateTime? UpdatedAt, string? ExternalUrl, List<EnterpriseIssueAttachment> Attachments);
    private sealed record ImportedTaskRow(string WorkItemId, string? Title, string? Description, string? Status, string? WorkItemType, string? Creator, string? Assignee, string? Collaborators, string? Priority, string? Labels, DateTime? ExternalCreatedAt, DateTime? ExternalUpdatedAt, string? ExternalUrl);
    private sealed class CsvImportResult
    {
        public int TotalRows { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Warnings { get; } = [];
        public void Skip(int row, string message) { Skipped++; Warn(row, message); }
        public void Warn(int row, string message) { if (Warnings.Count < 20) Warnings.Add($"第 {row + 1} 行：{message}"); }
    }
    private sealed class CsvImportException(string message) : Exception(message);
}
