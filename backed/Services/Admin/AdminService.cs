using AiAgent.Backend.Dtos.Admin;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Usage;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Globalization;

namespace AiAgent.Backend.Services.Admin;

public interface IProjectAccessService
{
    bool CanAccess(AuthenticatedUser user, long projectId);
    List<long> GetAccessibleProjectIds(AuthenticatedUser user);
}

public sealed class ProjectAccessService : IProjectAccessService
{
    private readonly ISqlSugarClient _db;
    public ProjectAccessService(ISqlSugarClient db) => _db = db;

    public bool CanAccess(AuthenticatedUser user, long projectId)
    {
        if (user.IsAdministrator) return _db.Queryable<AiCodeProject>().Any(item => item.Id == projectId && !item.IsDeleted);
        return _db.Queryable<AiUserCodeProject>().Any(item => item.UserId == user.Id && item.CodeProjectId == projectId)
            && _db.Queryable<AiCodeProject>().Any(item => item.Id == projectId && !item.IsDeleted);
    }

    public List<long> GetAccessibleProjectIds(AuthenticatedUser user)
    {
        if (user.IsAdministrator)
            return _db.Queryable<AiCodeProject>().Where(item => !item.IsDeleted).Select(item => item.Id).ToList();
        return _db.Queryable<AiUserCodeProject>().Where(item => item.UserId == user.Id).Select(item => item.CodeProjectId).ToList();
    }
}

public interface IAdminService
{
    Task<List<AdminUserDto>> ListUsersAsync(AuthenticatedUser administrator, CancellationToken cancellationToken);
    Task<(AdminUserDto? User, string? Error)> CreateUserAsync(AuthenticatedUser administrator, AdminCreateUserRequest request, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> UpdateUserAliasAsync(AuthenticatedUser administrator, string userId, string? alias, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> ResetUserPasswordAsync(AuthenticatedUser administrator, string userId, string password, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> UpdateUserProjectsAsync(AuthenticatedUser administrator, string userId, IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> UpdateUserCodeCommitPermissionAsync(AuthenticatedUser administrator, string userId, bool canCommitCode, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> UpdateUserStatusAsync(AuthenticatedUser administrator, string userId, bool isDisabled, CancellationToken cancellationToken);
    Task<List<AdminSessionSummaryDto>> ListSessionsAsync(AuthenticatedUser administrator, string? userId, int limit, CancellationToken cancellationToken);
    Task<ChatSessionDetailDto?> GetSessionAsync(AuthenticatedUser administrator, string userId, string sessionId, CancellationToken cancellationToken);
    Task<AdminUsageReportDto> GetUsageReportAsync(AuthenticatedUser administrator, string period, int days, string? userId, CancellationToken cancellationToken, string? startDate = null, string? endDate = null, long? projectId = null);
}

public sealed class AdminService : IAdminService
{
    private readonly ISqlSugarClient _db;
    private readonly IAuthService _auth;

    public AdminService(ISqlSugarClient db, IAuthService auth) => (_db, _auth) = (db, auth);

    public Task<List<AdminUserDto>> ListUsersAsync(AuthenticatedUser administrator, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var users = _db.Queryable<AiUser>().OrderBy(item => item.CreatedAt).ToList();
        // Historical assignments may still reference soft-deleted projects. Do not
        // return those IDs to the editor, or a save of an old account resubmits an
        // invalid project ID and fails validation.
        var activeProjectIds = _db.Queryable<AiCodeProject>()
            .Where(item => !item.IsDeleted)
            .Select(item => item.Id)
            .ToList()
            .ToHashSet();
        var permissions = _db.Queryable<AiUserCodeProject>().ToList()
            .Where(item => activeProjectIds.Contains(item.CodeProjectId))
            .GroupBy(item => item.UserId).ToDictionary(group => group.Key, group => group.Select(item => item.CodeProjectId).Distinct().OrderBy(item => item).ToList());
        return Task.FromResult(users.Select(item => ToUserDto(item, permissions.GetValueOrDefault(item.Id) ?? [])).ToList());
    }

    public async Task<(AdminUserDto? User, string? Error)> CreateUserAsync(AuthenticatedUser administrator, AdminCreateUserRequest request, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        var projectIds = NormalizeProjectIds(request.ProjectIds);
        if (!AllProjectsExist(projectIds)) return (null, "One or more selected projects do not exist.");
        _db.Ado.BeginTran();
        try
        {
            var (user, error) = await _auth.CreateUserAsync(request.Username, request.Password, request.Alias, cancellationToken);
            if (user == null)
            {
                _db.Ado.RollbackTran();
                return (null, error);
            }
            if (request.CanCommitCode)
            {
                _db.Updateable<AiUser>().SetColumns(item => item.CanCommitCode == true).Where(item => item.Id == user.Id).ExecuteCommand();
                user.CanCommitCode = true;
            }
            if (projectIds.Count > 0)
                _db.Insertable(projectIds.Select(projectId => new AiUserCodeProject { UserId = user.Id, CodeProjectId = projectId }).ToList()).ExecuteCommand();
            _db.Ado.CommitTran();
            return (ToUserDto(user, projectIds), null);
        }
        catch
        {
            _db.Ado.RollbackTran();
            throw;
        }
    }

    public Task<(bool Succeeded, string? Error)> UpdateUserAliasAsync(AuthenticatedUser administrator, string userId, string? alias, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        if (normalized?.Length > 64) return Task.FromResult((false, (string?)"Alias must not exceed 64 characters."));
        var updated = _db.Updateable<AiUser>()
            .SetColumns(item => item.Alias == normalized)
            .SetColumns(item => item.UpdatedAt == DateTime.UtcNow)
            .Where(item => item.Id == userId)
            .ExecuteCommand();
        return Task.FromResult(updated > 0 ? (true, (string?)null) : (false, (string?)"The user does not exist."));
    }

    public Task<(bool Succeeded, string? Error)> ResetUserPasswordAsync(AuthenticatedUser administrator, string userId, string password, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        return _auth.ResetPasswordAsync(userId, password, cancellationToken);
    }

    public Task<(bool Succeeded, string? Error)> UpdateUserProjectsAsync(AuthenticatedUser administrator, string userId, IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var user = _db.Queryable<AiUser>().First(item => item.Id == userId);
        if (user == null) return Task.FromResult((false, (string?)"The user does not exist."));
        if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)) return Task.FromResult((false, (string?)"Administrators already have access to every project."));
        var normalized = NormalizeProjectIds(projectIds);
        if (!AllProjectsExist(normalized)) return Task.FromResult((false, (string?)"One or more selected projects do not exist."));

        _db.Ado.BeginTran();
        try
        {
            _db.Deleteable<AiUserCodeProject>().Where(item => item.UserId == user.Id).ExecuteCommand();
            if (normalized.Count > 0)
                _db.Insertable(normalized.Select(projectId => new AiUserCodeProject { UserId = user.Id, CodeProjectId = projectId }).ToList()).ExecuteCommand();
            _db.Ado.CommitTran();
            return Task.FromResult((true, (string?)null));
        }
        catch
        {
            _db.Ado.RollbackTran();
            throw;
        }
    }

    public Task<(bool Succeeded, string? Error)> UpdateUserCodeCommitPermissionAsync(AuthenticatedUser administrator, string userId, bool canCommitCode, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var user = _db.Queryable<AiUser>().First(item => item.Id == userId);
        if (user == null) return Task.FromResult((false, (string?)"The user does not exist."));
        if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult((true, (string?)null));

        _db.Updateable<AiUser>()
            .SetColumns(item => item.CanCommitCode == canCommitCode)
            .SetColumns(item => item.UpdatedAt == DateTime.UtcNow)
            .Where(item => item.Id == user.Id)
            .ExecuteCommand();
        return Task.FromResult((true, (string?)null));
    }

    public Task<(bool Succeeded, string? Error)> UpdateUserStatusAsync(AuthenticatedUser administrator, string userId, bool isDisabled, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var user = _db.Queryable<AiUser>().First(item => item.Id == userId);
        if (user == null) return Task.FromResult((false, (string?)"The user does not exist."));
        if (isDisabled && user.Id == administrator.Id) return Task.FromResult((false, (string?)"You cannot disable your own account."));
        if (isDisabled && string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)
            && _db.Queryable<AiUser>().Count(item => item.Role == "admin" && !item.IsDisabled) <= 1)
            return Task.FromResult((false, (string?)"The last active administrator cannot be disabled."));

        var now = DateTime.UtcNow;
        _db.Updateable<AiUser>().SetColumns(item => item.IsDisabled == isDisabled).SetColumns(item => item.UpdatedAt == now)
            .Where(item => item.Id == user.Id).ExecuteCommand();
        if (isDisabled)
            _db.Updateable<AiUserSession>().SetColumns(item => item.RevokedAt == now)
                .Where(item => item.UserId == user.Id && item.RevokedAt == null).ExecuteCommand();
        return Task.FromResult((true, (string?)null));
    }

    public Task<List<AdminSessionSummaryDto>> ListSessionsAsync(AuthenticatedUser administrator, string? userId, int limit, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var users = _db.Queryable<AiUser>().ToList().ToDictionary(item => item.Id, item => item.Username);
        var query = _db.Queryable<AiChatSession>().Where(item => !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(item => item.UserId == userId.Trim());
        var sessions = query.OrderByDescending(item => item.UpdatedAt).Take(Math.Clamp(limit, 1, 200)).ToList();
        var sessionIds = sessions.Select(item => item.Id).ToList();
        var messages = sessionIds.Count == 0 ? [] : _db.Queryable<AiChatMessage>().Where(item => sessionIds.Contains(item.SessionId)).ToList();
        var projectIds = sessions.Where(item => item.CodeProjectId.HasValue).Select(item => item.CodeProjectId!.Value).Distinct().ToList();
        var projects = projectIds.Count == 0 ? new Dictionary<long, string>() : _db.Queryable<AiCodeProject>().Where(item => projectIds.Contains(item.Id) && !item.IsDeleted).ToList().ToDictionary(item => item.Id, item => item.DisplayName);
        return Task.FromResult(sessions.Select(item => new AdminSessionSummaryDto
        {
            Id = item.Id,
            UserId = item.UserId,
            Username = users.GetValueOrDefault(item.UserId) ?? "Unknown user",
            Title = item.Title,
            CreatedAt = AsUtc(item.CreatedAt),
            UpdatedAt = AsUtc(item.UpdatedAt),
            MessageCount = messages.Count(message => message.SessionId == item.Id),
            LastMessage = messages.Where(message => message.SessionId == item.Id).OrderByDescending(message => message.Id).FirstOrDefault()?.Content ?? string.Empty,
            ProjectId = item.CodeProjectId,
            ProjectName = item.CodeProjectId.HasValue ? projects.GetValueOrDefault(item.CodeProjectId.Value) : null,
            SortOrder = item.SortOrder ?? 0,
            Priority = item.Priority ?? "normal",
            IsPinned = item.IsPinned ?? false
        }).ToList());
    }

    public Task<ChatSessionDetailDto?> GetSessionAsync(AuthenticatedUser administrator, string userId, string sessionId, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var session = _db.Queryable<AiChatSession>().First(item => item.Id == sessionId && item.UserId == userId && !item.IsDeleted);
        if (session == null) return Task.FromResult<ChatSessionDetailDto?>(null);
        var messages = _db.Queryable<AiChatMessage>().Where(item => item.SessionId == session.Id).OrderBy(item => item.Id).ToList();
        var projectName = session.CodeProjectId.HasValue
            ? _db.Queryable<AiCodeProject>().Where(item => item.Id == session.CodeProjectId.Value && !item.IsDeleted).Select(item => item.DisplayName).First()
            : null;
        return Task.FromResult<ChatSessionDetailDto?>(new ChatSessionDetailDto
        {
            Id = session.Id,
            Title = session.Title,
            CreatedAt = AsUtc(session.CreatedAt),
            UpdatedAt = AsUtc(session.UpdatedAt),
            MessageCount = messages.Count,
            LastMessage = messages.LastOrDefault()?.Content ?? string.Empty,
            ProjectId = session.CodeProjectId,
            ProjectName = projectName,
            SortOrder = session.SortOrder ?? 0,
            Priority = session.Priority ?? "normal",
            IsPinned = session.IsPinned ?? false,
            Messages = messages.Select(item => new ChatSessionMessageDto
            {
                Id = item.Id,
                Role = item.Role,
                Content = item.Content,
                Thinking = item.Thinking,
                CreatedAt = AsUtc(item.CreatedAt)
            }).ToList()
        });
    }

    public Task<AdminUsageReportDto> GetUsageReportAsync(AuthenticatedUser administrator, string period, int days, string? userId, CancellationToken cancellationToken, string? startDate = null, string? endDate = null, long? projectId = null)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        period = NormalizePeriod(period);
        var to = DateTime.UtcNow.Date.AddDays(1);
        var from = to.AddDays(-Math.Clamp(days, 1, 3650));
        if (startDate != null || endDate != null)
        {
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from)
                || !DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last)
                || last < from || (last - from).TotalDays >= 3650 || last >= DateTime.MaxValue.Date)
                throw new ArgumentException("日期须为 yyyy-MM-dd，结束不早于开始，最多 3650 天。");
            to = last.AddDays(1);
        }
        if (projectId < 0) throw new ArgumentException("项目 ID 无效。");
        var query = _db.Queryable<AiUsageRecord>().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(item => item.UserId == userId.Trim());
        // Match both owner and session: an orphan ledger row must never borrow another user's project.
        var rowsQuery = query.LeftJoin<AiChatSession>((record, session) => record.SessionId == session.Id && record.UserId == session.UserId);
        if (projectId == 0) rowsQuery = rowsQuery.Where((record, session) => session.CodeProjectId == null);
        else if (projectId.HasValue) rowsQuery = rowsQuery.Where((record, session) => session.CodeProjectId == projectId);
        var rows = rowsQuery.Select((record, session) => new UsageReportRow
        {
            UserId = record.UserId, SessionId = record.SessionId, ProjectId = session.CodeProjectId,
            CreatedAt = record.CreatedAt, TotalTokens = record.TotalTokens,
            PromptTokens = record.PromptTokens, CompletionTokens = record.CompletionTokens
        }).ToList();
        var userMap = _db.Queryable<AiUser>().Select(item => new { item.Id, item.Username }).ToList().ToDictionary(item => item.Id, item => item.Username);
        var projectMap = _db.Queryable<AiCodeProject>().Select(item => new { item.Id, item.DisplayName, item.IsDeleted }).ToList()
            .ToDictionary(item => item.Id, item => item.DisplayName + (item.IsDeleted ? "（已删除）" : ""));
        var periodDays = (int)(to - from).TotalDays;
        AdminUsageMetricsDto Metrics(IEnumerable<UsageReportRow> source, int denominator)
        {
            var list = source.ToList();
            return new AdminUsageMetricsDto
            {
                SessionCount = list.Where(item => !string.IsNullOrWhiteSpace(item.SessionId)).Select(item => (item.UserId, item.SessionId)).Distinct().Count(),
                ActiveDays = list.Select(item => item.CreatedAt.Date).Distinct().Count(),
                DailyTurns = Math.Round((double)list.Count / denominator, 2)
            };
        }
        var grouped = rows.GroupBy(item => GetBucket(item.CreatedAt, period).Key).ToDictionary(group => group.Key, group => group.ToList());
        var buckets = Enumerable.Range(0, periodDays).Select(offset => from.AddDays(offset)).GroupBy(date => GetBucket(date, period))
            .Select(group =>
            {
                var items = grouped.GetValueOrDefault(group.Key.Key) ?? [];
                return new AdminUsageBucketDto { Key = group.Key.Key, Label = group.Key.Label,
                    TotalTokens = items.Sum(item => (long)item.TotalTokens), TurnCount = items.Count, Metrics = Metrics(items, group.Count()) };
            }).ToList();
        return Task.FromResult(new AdminUsageReportDto
        {
            Period = period, From = AsUtc(from), To = AsUtc(to), Metrics = Metrics(rows, periodDays),
            TotalTokens = rows.Sum(item => (long)item.TotalTokens), TurnCount = rows.Count, Buckets = buckets,
            Projects = projectMap.Select(item => new AdminUsageProjectDto { Id = item.Key, Name = item.Value }).OrderBy(item => item.Name).ToList(),
            UserProjects = rows.GroupBy(item => (item.UserId, item.ProjectId)).Select(group => new AdminUsageUserProjectDto
            {
                UserId = group.Key.UserId, Username = userMap.GetValueOrDefault(group.Key.UserId) ?? "已删除",
                ProjectId = group.Key.ProjectId, ProjectName = group.Key.ProjectId.HasValue ? projectMap.GetValueOrDefault(group.Key.ProjectId.Value) ?? "已删除" : "未关联 / 无法归属",
                TurnCount = group.Count(), TotalTokens = group.Sum(item => (long)item.TotalTokens), Metrics = Metrics(group, periodDays)
            }).OrderByDescending(item => item.TurnCount).ThenBy(item => item.UserId).ThenBy(item => item.ProjectId).ToList(),
            Users = rows.GroupBy(item => item.UserId).Select(group => new AdminUsageUserDto
            {
                UserId = group.Key, Username = userMap.GetValueOrDefault(group.Key) ?? "已删除",
                TotalTokens = group.Sum(item => (long)item.TotalTokens), PromptTokens = group.Sum(item => (long)item.PromptTokens),
                CompletionTokens = group.Sum(item => (long)item.CompletionTokens), TurnCount = group.Count()
            }).OrderByDescending(item => item.TotalTokens).ToList()
        });
    }

    private sealed class UsageReportRow
    {
        public string UserId { get; set; } = "";
        public string? SessionId { get; set; }
        public long? ProjectId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalTokens { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    private bool AllProjectsExist(List<long> projectIds)
        => projectIds.Count == 0 || _db.Queryable<AiCodeProject>().Where(item => projectIds.Contains(item.Id) && !item.IsDeleted).Count() == projectIds.Count;

    private static List<long> NormalizeProjectIds(IEnumerable<long>? projectIds)
        => (projectIds ?? []).Where(item => item > 0).Distinct().OrderBy(item => item).ToList();

    private static AdminUserDto ToUserDto(AiUser user, List<long> projectIds) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Alias = user.Alias,
        Role = user.Role,
        IsDisabled = user.IsDisabled,
        CanCommitCode = string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase) || user.CanCommitCode,
        CreatedAt = AsUtc(user.CreatedAt),
        ProjectIds = projectIds
    };

    private static void RequireAdministrator(AuthenticatedUser user)
    {
        if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
    }

    // Chat and user timestamps are written as UTC. SQL Server returns DateTime
    // without its Kind, so restore UTC before JSON serialization emits the value.
    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string NormalizePeriod(string value) => value.Trim().ToLowerInvariant() switch
    {
        "week" => "week",
        "month" => "month",
        "year" => "year",
        _ => "day"
    };

    private static (string Key, string Label) GetBucket(DateTime value, string period) => period switch
    {
        "week" => ($"{ISOWeek.GetYear(value):D4}-W{ISOWeek.GetWeekOfYear(value):D2}", $"{ISOWeek.GetYear(value)} W{ISOWeek.GetWeekOfYear(value):D2}"),
        "month" => (value.ToString("yyyy-MM", CultureInfo.InvariantCulture), value.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
        "year" => (value.ToString("yyyy", CultureInfo.InvariantCulture), value.ToString("yyyy", CultureInfo.InvariantCulture)),
        _ => (value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.ToString("MM-dd", CultureInfo.InvariantCulture))
    };
}
