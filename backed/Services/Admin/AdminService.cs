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
    Task<List<AdminSessionSummaryDto>> ListSessionsAsync(AuthenticatedUser administrator, string? userId, int limit, CancellationToken cancellationToken);
    Task<ChatSessionDetailDto?> GetSessionAsync(AuthenticatedUser administrator, string userId, string sessionId, CancellationToken cancellationToken);
    Task<AdminUsageReportDto> GetUsageReportAsync(AuthenticatedUser administrator, string period, int days, string? userId, CancellationToken cancellationToken);
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
        var permissions = _db.Queryable<AiUserCodeProject>().ToList()
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
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
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
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
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
                CreatedAt = item.CreatedAt
            }).ToList()
        });
    }

    public Task<AdminUsageReportDto> GetUsageReportAsync(AuthenticatedUser administrator, string period, int days, string? userId, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        period = NormalizePeriod(period);
        var periodDays = Math.Clamp(days, 1, 3650);
        var to = DateTime.UtcNow.Date.AddDays(1);
        var from = to.AddDays(-periodDays);
        var query = _db.Queryable<AiUsageRecord>().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(item => item.UserId == userId.Trim());
        var rows = query.ToList();
        var users = _db.Queryable<AiUser>().ToList();
        var userMap = users.ToDictionary(item => item.Id, item => item.Username);
        var visibleUsers = string.IsNullOrWhiteSpace(userId) ? users : users.Where(item => item.Id == userId).ToList();

        return Task.FromResult(new AdminUsageReportDto
        {
            Period = period,
            From = from,
            To = to,
            TotalTokens = rows.Sum(item => (long)item.TotalTokens),
            TurnCount = rows.Count,
            Buckets = rows.GroupBy(item => GetBucket(item.CreatedAt, period))
                .Select(group => new AdminUsageBucketDto { Key = group.Key.Key, Label = group.Key.Label, TotalTokens = group.Sum(item => (long)item.TotalTokens), TurnCount = group.Count() })
                .OrderBy(item => item.Key).ToList(),
            Users = visibleUsers.Select(user =>
            {
                var userRows = rows.Where(item => item.UserId == user.Id).ToList();
                return new AdminUsageUserDto
                {
                    UserId = user.Id,
                    Username = userMap.GetValueOrDefault(user.Id) ?? user.Username,
                    TotalTokens = userRows.Sum(item => (long)item.TotalTokens),
                    PromptTokens = userRows.Sum(item => (long)item.PromptTokens),
                    CompletionTokens = userRows.Sum(item => (long)item.CompletionTokens),
                    TurnCount = userRows.Count
                };
            }).OrderByDescending(item => item.TotalTokens).ThenBy(item => item.Username).ToList()
        });
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
        CreatedAt = user.CreatedAt,
        ProjectIds = projectIds
    };

    private static void RequireAdministrator(AuthenticatedUser user)
    {
        if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
    }

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
