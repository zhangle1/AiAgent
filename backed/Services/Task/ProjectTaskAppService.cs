using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Dtos.Task;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Entities.Task;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace AiAgent.Backend.Services.ProjectTasks;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/project-tasks")]
public sealed class ProjectTaskAppService : IDynamicApiController
{
    private readonly ISqlSugarClient _db; private readonly IHttpContextAccessor _context; private readonly IAuthService _auth; private readonly IProjectAccessService _projectAccess; private readonly IDataProtector _protector; private readonly IHttpClientFactory _http;
    public ProjectTaskAppService(ISqlSugarClient db, IHttpContextAccessor context, IAuthService auth, IProjectAccessService projectAccess, IDataProtectionProvider protection, IHttpClientFactory http) => (_db, _context, _auth, _projectAccess, _protector, _http) = (db, context, auth, projectAccess, protection.CreateProtector("AiAgent.GitAccounts.AccessToken.v1"), http);

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
    private AiGitAccount? ActiveGiteeAccount(AuthenticatedUser user) => _db.Queryable<AiGitAccount>().First(x => x.UserId == user.Id && x.Provider == "gitee" && x.IsActive && !x.IsDeleted && !string.IsNullOrEmpty(x.AccessTokenProtected));
    private async Task<(List<JsonElement>? Items, IActionResult? Error)> GetGiteeArray(string url, AiGitAccount account, CancellationToken cancellationToken)
    {
        string token; try { token = _protector.Unprotect(account.AccessTokenProtected); } catch { return (null, new BadRequestObjectResult(new { message = "Gitee 令牌无法读取，请重新保存该账户。" })); }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token); request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
        using var response = await _http.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return (null, new ObjectResult(new { message = $"Gitee 返回 HTTP {(int)response.StatusCode}，请检查令牌的项目与 Issue 读取权限。" }) { StatusCode = 502 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.ValueKind == JsonValueKind.Array ? (json.RootElement.EnumerateArray().Select(item => item.Clone()).ToList(), null) : (null, new ObjectResult(new { message = "Gitee 返回了无法识别的列表数据。" }) { StatusCode = 502 });
    }
    private static int NormalizePage(int page) => Math.Max(1, page);
    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);
    private static bool IsGiteeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase);
    private static ProjectTaskDto Dto(AiProjectTask x, string? name) => new() { Id=x.Id, ProjectId=x.CodeProjectId, ProjectName=name, Source=x.Source, ExternalId=x.ExternalId, WorkItemId=x.WorkItemId, WorkItemType=x.WorkItemType, Title=x.Title, Description=x.Description, Status=x.Status, Creator=x.Creator, Assignee=x.Assignee, Collaborators=x.Collaborators, Priority=x.Priority, Labels=x.Labels, ExternalUrl=x.ExternalUrl, ExternalCreatedAt=x.ExternalCreatedAt, ExternalUpdatedAt=x.ExternalUpdatedAt, UpdatedAt=x.UpdatedAt };
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.String ? p.GetString() : p.ValueKind == JsonValueKind.Number ? p.GetRawText() : null : null;
    private static DateTime? Date(JsonElement e, string name) => DateTime.TryParse(Text(e,name), out var value) ? value : null;
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
                    SaveImportBatch(user, projectId, batch, result);
                    batch.Clear();
                }
            }
            if (batch.Count > 0) SaveImportBatch(user, projectId, batch, result);
            _db.Ado.CommitTran();
            return new { total_rows = result.TotalRows, inserted = result.Inserted, updated = result.Updated, skipped = result.Skipped, warnings = result.Warnings };
        }
        catch
        {
            _db.Ado.RollbackTran();
            throw;
        }
    }

    private void SaveImportBatch(AuthenticatedUser user, long projectId, IReadOnlyList<ImportedTaskRow> input, CsvImportResult result)
    {
        var uniqueInput = input.GroupBy(x => x.WorkItemId, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToList();
        var ids = uniqueInput.Select(x => x.WorkItemId).ToList();
        var externalIds = ids.Select(x => $"gitee-work-item:{x}").ToList();
        var legacyExternalIds = ids.Select(x => $"gitee:{x}").ToList();
        var existingRows = _db.Queryable<AiProjectTask>()
            .Where(x => x.UserId == user.Id && x.CodeProjectId == projectId && !x.IsDeleted && ((x.WorkItemId != null && ids.Contains(x.WorkItemId)) || (x.ExternalId != null && (externalIds.Contains(x.ExternalId) || legacyExternalIds.Contains(x.ExternalId)))))
            .ToList();
        var existing = new Dictionary<string, AiProjectTask>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in existingRows)
        {
            if (!string.IsNullOrWhiteSpace(row.WorkItemId)) existing.TryAdd(row.WorkItemId, row);
            if (!string.IsNullOrWhiteSpace(row.ExternalId) && row.ExternalId.StartsWith("gitee-work-item:", StringComparison.OrdinalIgnoreCase)) existing.TryAdd(row.ExternalId[16..], row);
            if (!string.IsNullOrWhiteSpace(row.ExternalId) && row.ExternalId.StartsWith("gitee:", StringComparison.OrdinalIgnoreCase)) existing.TryAdd(row.ExternalId[6..], row);
        }

        var inserts = new List<AiProjectTask>();
        var updates = new List<AiProjectTask>();
        foreach (var item in uniqueInput)
        {
            if (existing.TryGetValue(item.WorkItemId, out var task))
            {
                ApplyImportedTask(task, item);
                updates.Add(task);
                result.Updated++;
                continue;
            }
            task = NewImportedTask(user.Id, projectId, item);
            existing[item.WorkItemId] = task;
            inserts.Add(task);
            result.Inserted++;
        }
        if (inserts.Count > 0) _db.Insertable(inserts).ExecuteCommand();
        if (updates.Count > 0)
        {
            _db.Updateable(updates).UpdateColumns(x => new { x.WorkItemId, x.WorkItemType, x.ExternalId, x.Source, x.Title, x.Description, x.Status, x.Creator, x.Assignee, x.Collaborators, x.Priority, x.Labels, x.ExternalCreatedAt, x.ExternalUpdatedAt, x.UpdatedAt }).ExecuteCommand();
        }
    }

    private static AiProjectTask NewImportedTask(string userId, long projectId, ImportedTaskRow item) => new()
    {
        UserId = userId,
        CodeProjectId = projectId,
        WorkItemId = item.WorkItemId,
        ExternalId = $"gitee-work-item:{item.WorkItemId}",
        Source = "gitee_enterprise_csv",
        Title = item.Title ?? $"工作项 #{item.WorkItemId}",
        Description = item.Description,
        Status = item.Status,
        WorkItemType = item.WorkItemType,
        Creator = item.Creator,
        Assignee = item.Assignee,
        Collaborators = item.Collaborators,
        Priority = item.Priority,
        Labels = item.Labels,
        ExternalCreatedAt = item.ExternalCreatedAt,
        ExternalUpdatedAt = item.ExternalUpdatedAt,
        UpdatedAt = DateTime.UtcNow
    };

    private static void ApplyImportedTask(AiProjectTask task, ImportedTaskRow item)
    {
        task.WorkItemId = item.WorkItemId;
        task.ExternalId = $"gitee-work-item:{item.WorkItemId}";
        task.Source = "gitee_enterprise_csv";
        if (item.Title is not null) task.Title = item.Title;
        if (item.Description is not null) task.Description = item.Description;
        if (item.Status is not null) task.Status = item.Status;
        if (item.WorkItemType is not null) task.WorkItemType = item.WorkItemType;
        if (item.Creator is not null) task.Creator = item.Creator;
        if (item.Assignee is not null) task.Assignee = item.Assignee;
        if (item.Collaborators is not null) task.Collaborators = item.Collaborators;
        if (item.Priority is not null) task.Priority = item.Priority;
        if (item.Labels is not null) task.Labels = item.Labels;
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
        return new ImportedTaskRow(workItemId, Value("title", 512), Value("description", 20000), Value("status", 32), Value("work_item_type", 64), Value("creator", 64), Value("assignee", 64), Value("collaborators", 1024), Value("priority", 32), Value("labels", 512), DateValue("created_at"), DateValue("updated_at"));
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

    private sealed record ImportedTaskRow(string WorkItemId, string? Title, string? Description, string? Status, string? WorkItemType, string? Creator, string? Assignee, string? Collaborators, string? Priority, string? Labels, DateTime? ExternalCreatedAt, DateTime? ExternalUpdatedAt);
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
