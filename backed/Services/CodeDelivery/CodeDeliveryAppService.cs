using System.Collections.Concurrent;
using System.Text.Json;
using AiAgent.Backend.Dtos.CodeDelivery;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Task;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Git;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace AiAgent.Backend.Services.CodeDelivery;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/code-deliveries")]
public sealed class CodeDeliveryAppService : IDynamicApiController
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> Gates = new();
    private readonly ISqlSugarClient _db;
    private readonly IHttpContextAccessor _http;
    private readonly IAuthService _auth;
    private readonly IProjectAccessService _access;
    private readonly ICodeRepositoryGitService _git;
    public CodeDeliveryAppService(ISqlSugarClient db, IHttpContextAccessor http, IAuthService auth, IProjectAccessService access, ICodeRepositoryGitService git)
        => (_db, _http, _auth, _access, _git) = (db, http, auth, access, git);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? projectId, [FromQuery] long? taskId, CancellationToken token)
    {
        var user = await User(token);
        var allowed = _access.GetAccessibleProjectIds(user);
        var query = _db.Queryable<AiCodeChangeSet>().Where(x => x.IsDeleted != true && x.ProjectId != null && allowed.Contains(x.ProjectId.Value));
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        if (taskId.HasValue) query = query.Where(x => x.TaskId == taskId);
        var rows = query.OrderByDescending(x => x.CreatedAt).Take(100).ToList();
        return new OkObjectResult(new { change_sets = rows.Select(Dto).ToList() });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id, CancellationToken token)
    {
        var row = FindVisible(await User(token), id);
        return row is null ? new NotFoundObjectResult(new { message = "变更集不存在。" }) : new OkObjectResult(Detail(row));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCodeChangeSetRequest request, CancellationToken token)
    {
        var user = await User(token);
        if (!_access.CanAccess(user, request.ProjectId)) return new ForbidResult();
        var task = request.TaskId.HasValue ? _db.Queryable<AiProjectTask>().First(x => x.Id == request.TaskId && x.CodeProjectId == request.ProjectId && !x.IsDeleted) : null;
        if (request.TaskId.HasValue && task is null) return new BadRequestObjectResult(new { message = "关联任务不存在或不属于该项目。" });
        var row = new AiCodeChangeSet
        {
            ProjectId = request.ProjectId, TaskId = request.TaskId, UserId = user.Id,
            Title = Trim(request.Title, 256) ?? task?.Title ?? "AI 代码变更",
            CommitMessage = Trim(request.CommitMessage, 200) ?? $"feat: {Trim(task?.Title, 180) ?? "deliver AI changes"}",
            Status = "draft", UpdatedAt = DateTime.UtcNow
        };
        _db.Insertable(row).ExecuteCommand();
        await ValidateCore(row, token);
        return new OkObjectResult(Detail(row));
    }

    [HttpPost("{id}/validate")]
    public async Task<IActionResult> Validate(long id, CancellationToken token)
    {
        var row = FindVisible(await User(token), id);
        if (row is null) return new NotFoundObjectResult(new { message = "变更集不存在。" });
        if (row.Status is "approved" or "delivering" or "delivered") return new ConflictObjectResult(new { message = "已批准或已交付的变更集不能重新生成快照。" });
        if (_db.Queryable<AiRepositoryMaintenanceRun>().Any(x => x.ChangeSetId == id))
            return new ConflictObjectResult(new { message = "养护变更集的编译快照由养护任务生成，请重新执行养护。" });
        await ValidateCore(row, token);
        return new OkObjectResult(Detail(row));
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(long id, [FromBody] CodeChangeSetApprovalRequest request, CancellationToken token)
    {
        var user = await User(token);
        if (!user.CanCommitCode) return new ForbidResult();
        var row = FindVisible(user, id);
        if (row is null) return new NotFoundObjectResult(new { message = "变更集不存在。" });
        if (row.Status != "pending_approval") return new ConflictObjectResult(new { message = "仅待审批变更集可审批。" });
        if (_db.Queryable<AiRepositoryMaintenanceRun>().Any(x => x.ChangeSetId == id))
            return new ConflictObjectResult(new { message = "请在代码库养护页面批准并推送，或保留为待审批状态。" });
        row.Status = request.Approved ? "approved" : "rejected";
        row.ApprovedBy = user.Id;
        row.ApprovalComment = Trim(request.Comment, 1000);
        row.ApprovedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(row).ExecuteCommand();
        return new OkObjectResult(Detail(row));
    }

    [HttpPost("{id}/deliver")]
    public async Task<IActionResult> Deliver(long id, CancellationToken token)
    {
        var user = await User(token);
        if (!user.CanCommitCode) return new ForbidResult();
        var row = FindVisible(user, id);
        if (row is null) return new NotFoundObjectResult(new { message = "变更集不存在。" });
        if (row.Status != "approved") return new ConflictObjectResult(new { message = "变更集尚未批准。" });
        if (_db.Queryable<AiRepositoryMaintenanceRun>().Any(x => x.ChangeSetId == id))
            return new ConflictObjectResult(new { message = "养护变更集使用独立副本，请在代码库养护页面批准并推送。" });
        var gate = Gates.GetOrAdd(row.ProjectId!.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var items = Repositories(row.Id);
            foreach (var item in items)
            {
                var current = await _git.ValidateDeliveryAsync(item.RepositoryName!, token);
                if (!string.Equals(current.SnapshotSha256, item.SnapshotSha256, StringComparison.OrdinalIgnoreCase))
                    return new ConflictObjectResult(new { message = $"仓库 {item.DisplayName} 在审批后发生变化，请重新创建并审批变更集。" });
            }
            row.Status = "delivering"; row.UpdatedAt = DateTime.UtcNow; _db.Updateable(row).ExecuteCommand();
            var failed = false;
            foreach (var item in items)
            {
                var result = await _git.CommitAndPushAsync(item.RepositoryName!, row.CommitMessage, token);
                item.DeliveryStatus = result.Ok ? "delivered" : "failed";
                item.CommitSha = result.CommitSha;
                item.SafeOutput = Trim(result.Output, 8000);
                item.UpdatedAt = DateTime.UtcNow;
                failed |= !result.Ok;
                _db.Updateable(item).ExecuteCommand();
            }
            row.Status = failed ? "delivery_failed" : "delivered";
            row.ErrorSummary = failed ? "至少一个仓库提交或推送失败。" : null;
            row.DeliveredAt = failed ? null : DateTime.UtcNow;
            row.UpdatedAt = DateTime.UtcNow;
            _db.Updateable(row).ExecuteCommand();
            if (!failed && row.TaskId.HasValue)
                _db.Updateable<AiProjectTask>().SetColumns(x => new AiProjectTask { Status = "done", UpdatedAt = DateTime.UtcNow }).Where(x => x.Id == row.TaskId && !x.IsDeleted).ExecuteCommand();
            return new OkObjectResult(Detail(row));
        }
        finally { gate.Release(); }
    }

    private async Task ValidateCore(AiCodeChangeSet row, CancellationToken token)
    {
        var gate = Gates.GetOrAdd(row.ProjectId!.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            row.Status = "validating"; row.ErrorSummary = null; row.UpdatedAt = DateTime.UtcNow; _db.Updateable(row).ExecuteCommand();
            _db.Deleteable<AiCodeChangeSetRepository>().Where(x => x.ChangeSetId == row.Id).ExecuteCommand();
            var repositories = _db.Queryable<AiCodeRepository>().Where(x => x.ProjectId == row.ProjectId && !x.IsDeleted).OrderBy(x => x.DisplayName).ToList();
            var accepted = new List<AiCodeChangeSetRepository>();
            foreach (var repository in repositories)
            {
                var validation = await _git.ValidateDeliveryAsync(repository.Name, token);
                if (!validation.Files.Any() && validation.Checks.FirstOrDefault(x => x.Name == "changes")?.Passed != true) continue;
                accepted.Add(new AiCodeChangeSetRepository
                {
                    ChangeSetId = row.Id, RepositoryId = repository.Id, RepositoryName = repository.Name, DisplayName = repository.DisplayName,
                    Branch = validation.Branch, SnapshotSha256 = validation.SnapshotSha256,
                    FilesJson = JsonSerializer.Serialize(validation.Files), ValidationJson = JsonSerializer.Serialize(validation.Checks),
                    ValidationStatus = validation.IsValid ? "passed" : "failed", DeliveryStatus = "pending", UpdatedAt = DateTime.UtcNow
                });
            }
            if (accepted.Count > 0) _db.Insertable(accepted).ExecuteCommand();
            var passed = accepted.Count > 0 && accepted.All(x => x.ValidationStatus == "passed");
            row.Status = passed ? "pending_approval" : "validation_failed";
            row.ValidatedAt = DateTime.UtcNow;
            row.Summary = accepted.Count == 0 ? "未发现可交付变更。" : $"涉及 {accepted.Count} 个仓库、{accepted.Sum(x => Read<List<string>>(x.FilesJson).Count)} 个文件。\n" + string.Join("\n", accepted.Select(x => $"- {x.DisplayName} ({x.Branch})：{Read<List<string>>(x.FilesJson).Count} 个文件，校验{x.ValidationStatus}"));
            row.ErrorSummary = passed ? null : accepted.Count == 0 ? "未发现可交付变更。" : "自动校验未全部通过。";
            row.UpdatedAt = DateTime.UtcNow;
            _db.Updateable(row).ExecuteCommand();
            if (passed && row.TaskId.HasValue)
                _db.Updateable<AiProjectTask>().SetColumns(x => new AiProjectTask { Status = "in_review", UpdatedAt = DateTime.UtcNow }).Where(x => x.Id == row.TaskId && !x.IsDeleted).ExecuteCommand();
        }
        finally { gate.Release(); }
    }

    private AiCodeChangeSet? FindVisible(AuthenticatedUser user, long id)
    {
        var row = _db.Queryable<AiCodeChangeSet>().First(x => x.Id == id && x.IsDeleted != true);
        return row?.ProjectId is long projectId && _access.CanAccess(user, projectId) ? row : null;
    }
    private List<AiCodeChangeSetRepository> Repositories(long id) => _db.Queryable<AiCodeChangeSetRepository>().Where(x => x.ChangeSetId == id).OrderBy(x => x.DisplayName).ToList();
    private CodeChangeSetDto Detail(AiCodeChangeSet row) { var dto = Dto(row); dto.Repositories = Repositories(row.Id).Select(RepositoryDto).ToList(); return dto; }
    private static CodeChangeSetDto Dto(AiCodeChangeSet x) => new() { Id=x.Id, ProjectId=x.ProjectId, TaskId=x.TaskId, Title=x.Title, CommitMessage=x.CommitMessage, Status=x.Status, Summary=x.Summary, ErrorSummary=x.ErrorSummary, ApprovedBy=x.ApprovedBy, ApprovalComment=x.ApprovalComment, ApprovedAt=x.ApprovedAt, ValidatedAt=x.ValidatedAt, DeliveredAt=x.DeliveredAt, CreatedAt=x.CreatedAt };
    private static CodeChangeSetRepositoryDto RepositoryDto(AiCodeChangeSetRepository x) => new() { RepositoryId=x.RepositoryId, RepositoryName=x.RepositoryName, DisplayName=x.DisplayName, Branch=x.Branch, SnapshotSha256=x.SnapshotSha256, Files=Read<List<string>>(x.FilesJson), Checks=Read<List<GitDeliveryCheck>>(x.ValidationJson).Select(c => new CodeDeliveryCheckDto(c.Name,c.Passed,c.Message)).ToList(), ValidationStatus=x.ValidationStatus, DeliveryStatus=x.DeliveryStatus, CommitSha=x.CommitSha, SafeOutput=x.SafeOutput };
    private async Task<AuthenticatedUser> User(CancellationToken token) => await _auth.TryGetCurrentUserAsync(_http.HttpContext!, token) ?? throw new UnauthorizedAccessException();
    private static T Read<T>(string? json) where T : new() { try { return string.IsNullOrWhiteSpace(json) ? new T() : JsonSerializer.Deserialize<T>(json) ?? new T(); } catch { return new T(); } }
    private static string? Trim(string? value, int max) { value=value?.Trim(); return string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max]; }
}
