using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IChatSessionService
{
    Task RecordUserMessageAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken cancellationToken);
    Task RecordAssistantMessageAsync(AuthenticatedUser user, ChatCompleteRequest request, string content, string? thinking, object? citations, string? modelId, string? model, CancellationToken cancellationToken);
    Task<List<ChatSessionSummaryDto>> ListAsync(AuthenticatedUser user, int limit, CancellationToken cancellationToken);
    Task<ChatSessionDetailDto?> GetAsync(AuthenticatedUser user, string sessionId, CancellationToken cancellationToken);
    Task<bool> RenameAsync(AuthenticatedUser user, string sessionId, string title, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(AuthenticatedUser user, string sessionId, CancellationToken cancellationToken);
    Task<bool> ReorderAsync(AuthenticatedUser user, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken);
    Task<bool> UpdateMetadataAsync(AuthenticatedUser user, string sessionId, UpdateChatSessionMetaRequest request, CancellationToken cancellationToken);
    Task<List<ChatProjectPreferenceDto>> ListProjectPreferencesAsync(AuthenticatedUser user, CancellationToken cancellationToken);
    Task<bool> UpdateProjectPreferenceAsync(AuthenticatedUser user, long projectId, UpdateChatProjectPreferenceRequest request, CancellationToken cancellationToken);
}

public sealed class ChatSessionService : IChatSessionService
{
    private readonly ISqlSugarClient _db;
    public ChatSessionService(ISqlSugarClient db) => _db = db;

    public Task RecordUserMessageAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        var session = EnsureSession(user, request);
        _db.Insertable(new AiChatMessage { SessionId = session.Id, Role = "user", Content = request.Message.Trim() }).ExecuteCommand();
        session.UpdatedAt = DateTime.UtcNow;
        session.SortOrder = NextSortOrder(user.Id);
        _db.Updateable(session).UpdateColumns(x => new { x.Title, x.PreferencesJson, x.CodeProjectId, x.SortOrder, x.UpdatedAt }).ExecuteCommand();
        return Task.CompletedTask;
    }

    public Task RecordAssistantMessageAsync(AuthenticatedUser user, ChatCompleteRequest request, string content, string? thinking, object? citations, string? modelId, string? model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId)) return Task.CompletedTask;
        var session = _db.Queryable<AiChatSession>().First(x => x.Id == request.SessionId && x.UserId == user.Id && !x.IsDeleted);
        if (session == null) return Task.CompletedTask;
        _db.Insertable(new AiChatMessage
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = content,
            Thinking = string.IsNullOrWhiteSpace(thinking) ? null : thinking,
            CitationsJson = citations == null ? null : JsonSerializer.Serialize(citations),
            MetadataJson = JsonSerializer.Serialize(new { model_id = modelId, model })
        }).ExecuteCommand();
        _db.Updateable<AiChatSession>().SetColumns(x => x.UpdatedAt == DateTime.UtcNow).Where(x => x.Id == session.Id).ExecuteCommand();
        return Task.CompletedTask;
    }

    public Task<List<ChatSessionSummaryDto>> ListAsync(AuthenticatedUser user, int limit, CancellationToken cancellationToken)
    {
        var sessions = _db.Queryable<AiChatSession>().Where(x => x.UserId == user.Id && !x.IsDeleted).OrderByDescending(x => x.IsPinned).OrderByDescending(x => x.SortOrder).OrderByDescending(x => x.UpdatedAt).Take(limit).ToList();
        var ids = sessions.Select(x => x.Id).ToList();
        var messages = ids.Count == 0 ? new List<AiChatMessage>() : _db.Queryable<AiChatMessage>().Where(x => ids.Contains(x.SessionId)).OrderByDescending(x => x.Id).ToList();
        var projectIds = sessions.Where(x => x.CodeProjectId.HasValue).Select(x => x.CodeProjectId!.Value).Distinct().ToList();
        var projects = projectIds.Count == 0 ? new Dictionary<long, AiCodeProject>() : _db.Queryable<AiCodeProject>().Where(x => projectIds.Contains(x.Id) && !x.IsDeleted).ToList().ToDictionary(x => x.Id);
        return Task.FromResult(sessions.Select(session => ToSummary(session, messages.Where(x => x.SessionId == session.Id).ToList(), projects.GetValueOrDefault(session.CodeProjectId ?? 0))).ToList());
    }

    public Task<ChatSessionDetailDto?> GetAsync(AuthenticatedUser user, string sessionId, CancellationToken cancellationToken)
    {
        var session = _db.Queryable<AiChatSession>().First(x => x.Id == sessionId && x.UserId == user.Id && !x.IsDeleted);
        if (session == null) return Task.FromResult<ChatSessionDetailDto?>(null);
        var messages = _db.Queryable<AiChatMessage>().Where(x => x.SessionId == session.Id).OrderBy(x => x.Id).ToList();
        var dto = new ChatSessionDetailDto
        {
            Id = session.Id,
            Title = session.Title,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            MessageCount = messages.Count,
            LastMessage = messages.LastOrDefault()?.Content ?? string.Empty,
            ProjectId = session.CodeProjectId,
            ProjectName = session.CodeProjectId.HasValue ? _db.Queryable<AiCodeProject>().Where(x => x.Id == session.CodeProjectId.Value && !x.IsDeleted).Select(x => x.DisplayName).First() : null,
            SortOrder = session.SortOrder ?? 0,
            Priority = session.Priority ?? "normal",
            IsPinned = session.IsPinned ?? false,
            Preferences = DeserializeObject(session.PreferencesJson),
            Messages = messages.Select(x => new ChatSessionMessageDto { Id = x.Id, Role = x.Role, Content = x.Content, Thinking = x.Thinking, Citations = DeserializeValue(x.CitationsJson), Metadata = DeserializeValue(x.MetadataJson), CreatedAt = x.CreatedAt }).ToList()
        };
        return Task.FromResult<ChatSessionDetailDto?>(dto);
    }

    public Task<bool> RenameAsync(AuthenticatedUser user, string sessionId, string title, CancellationToken cancellationToken)
    {
        title = title.Trim();
        if (title.Length == 0) return Task.FromResult(false);
        var normalizedTitle = title[..Math.Min(title.Length, 160)];
        var updatedAt = DateTime.UtcNow;
        var count = _db.Updateable<AiChatSession>()
            .SetColumns(x => x.Title == normalizedTitle)
            .SetColumns(x => x.UpdatedAt == updatedAt)
            .Where(x => x.Id == sessionId && x.UserId == user.Id && !x.IsDeleted)
            .ExecuteCommand();
        return Task.FromResult(count > 0);
    }

    public Task<bool> DeleteAsync(AuthenticatedUser user, string sessionId, CancellationToken cancellationToken)
    {
        var count = _db.Updateable<AiChatSession>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == sessionId && x.UserId == user.Id && !x.IsDeleted).ExecuteCommand();
        return Task.FromResult(count > 0);
    }

    public Task<bool> ReorderAsync(AuthenticatedUser user, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        var normalized = sessionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Take(100).ToList();
        if (normalized.Count == 0) return Task.FromResult(false);
        var owned = _db.Queryable<AiChatSession>().Where(x => x.UserId == user.Id && normalized.Contains(x.Id) && !x.IsDeleted).ToList();
        if (owned.Count != normalized.Count) return Task.FromResult(false);
        var lookup = owned.ToDictionary(x => x.Id, StringComparer.Ordinal);
        for (var index = 0; index < normalized.Count; index++)
        {
            var session = lookup[normalized[index]];
            session.SortOrder = normalized.Count - index;
            _db.Updateable(session).UpdateColumns(x => x.SortOrder).ExecuteCommand();
        }
        return Task.FromResult(true);
    }

    public Task<bool> UpdateMetadataAsync(AuthenticatedUser user, string sessionId, UpdateChatSessionMetaRequest request, CancellationToken cancellationToken)
    {
        var session = _db.Queryable<AiChatSession>().First(x => x.Id == sessionId && x.UserId == user.Id && !x.IsDeleted);
        if (session == null) return Task.FromResult(false);

        if (request.Priority != null)
        {
            var priority = request.Priority.Trim().ToLowerInvariant();
            if (priority is not ("high" or "normal" or "low")) return Task.FromResult(false);
            session.Priority = priority;
        }
        if (request.IsPinned.HasValue) session.IsPinned = request.IsPinned.Value;

        session.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(session).UpdateColumns(x => new { x.Priority, x.IsPinned, x.UpdatedAt }).ExecuteCommand();
        return Task.FromResult(true);
    }

    public Task<List<ChatProjectPreferenceDto>> ListProjectPreferencesAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        var preferences = _db.Queryable<AiChatProjectPreference>().Where(x => x.UserId == user.Id).ToList();
        return Task.FromResult(preferences.Select(x => new ChatProjectPreferenceDto { ProjectId = x.CodeProjectId, IsPinned = x.IsPinned, SortMode = x.SortMode }).ToList());
    }

    public Task<bool> UpdateProjectPreferenceAsync(AuthenticatedUser user, long projectId, UpdateChatProjectPreferenceRequest request, CancellationToken cancellationToken)
    {
        if (!_db.Queryable<AiCodeProject>().Any(x => x.Id == projectId && !x.IsDeleted)) return Task.FromResult(false);
        var preference = _db.Queryable<AiChatProjectPreference>().First(x => x.UserId == user.Id && x.CodeProjectId == projectId);
        var isNew = preference == null;
        if (preference == null)
        {
            preference = new AiChatProjectPreference { UserId = user.Id, CodeProjectId = projectId };
        }
        if (request.IsPinned.HasValue) preference.IsPinned = request.IsPinned.Value;
        if (request.SortMode != null)
        {
            var sortMode = request.SortMode.Trim().ToLowerInvariant();
            if (sortMode is not ("updated" or "priority" or "manual")) return Task.FromResult(false);
            preference.SortMode = sortMode;
        }
        preference.UpdatedAt = DateTime.UtcNow;
        if (isNew) _db.Insertable(preference).ExecuteCommand();
        else _db.Updateable(preference).UpdateColumns(x => new { x.IsPinned, x.SortMode, x.UpdatedAt }).ExecuteCommand();
        return Task.FromResult(true);
    }

    private AiChatSession EnsureSession(AuthenticatedUser user, ChatCompleteRequest request)
    {
        var sessionId = request.SessionId?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64) sessionId = Guid.NewGuid().ToString("N");
        request.SessionId = sessionId;
        var session = _db.Queryable<AiChatSession>().First(x => x.Id == sessionId && !x.IsDeleted);
        if (session != null && session.UserId == user.Id)
        {
            session.CodeProjectId = ResolveProjectId(request.CodeProjectId);
            session.PreferencesJson = SerializePreferences(request);
            return session;
        }
        if (session != null) {
            sessionId = Guid.NewGuid().ToString("N");
            request.SessionId = sessionId;
        }
        session = new AiChatSession { Id = sessionId, UserId = user.Id, Title = MakeTitle(request.Message), CodeProjectId = ResolveProjectId(request.CodeProjectId), SortOrder = NextSortOrder(user.Id), PreferencesJson = SerializePreferences(request) };
        _db.Insertable(session).ExecuteCommand();
        return session;
    }

    private long? ResolveProjectId(long? projectId)
    {
        if (!projectId.HasValue) return null;
        if (!_db.Queryable<AiCodeProject>().Any(x => x.Id == projectId.Value && !x.IsDeleted)) throw new InvalidOperationException("The selected code project does not exist.");
        return projectId;
    }

    private int NextSortOrder(string userId) => (_db.Queryable<AiChatSession>().Where(x => x.UserId == userId && !x.IsDeleted).Max(x => x.SortOrder) ?? 0) + 1;
    private static string SerializePreferences(ChatCompleteRequest request) => JsonSerializer.Serialize(new { knowledge_base_names = request.KnowledgeBaseNames, code_project_id = request.CodeProjectId, code_repository_names = request.CodeRepositoryNames, model_id = request.ModelId, mode = request.Mode, agent = request.Agent });
    private static ChatSessionSummaryDto ToSummary(AiChatSession session, List<AiChatMessage> messages, AiCodeProject? project) => new() { Id = session.Id, Title = session.Title, CreatedAt = session.CreatedAt, UpdatedAt = session.UpdatedAt, MessageCount = messages.Count, LastMessage = messages.FirstOrDefault()?.Content ?? string.Empty, ProjectId = session.CodeProjectId, ProjectName = project?.DisplayName, SortOrder = session.SortOrder ?? 0, Priority = session.Priority ?? "normal", IsPinned = session.IsPinned ?? false };
    private static string MakeTitle(string message) => string.IsNullOrWhiteSpace(message) ? "新会话" : message.Trim().Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(message.Trim().Replace('\r', ' ').Replace('\n', ' ').Length, 40)];
    private static Dictionary<string, object?> DeserializeObject(string? value) => string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<Dictionary<string, object?>>(value) ?? [];
    private static object? DeserializeValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<object>(value);
}
