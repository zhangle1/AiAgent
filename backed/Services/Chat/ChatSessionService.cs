using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
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
        _db.Updateable(session).UpdateColumns(x => new { x.Title, x.PreferencesJson, x.UpdatedAt }).ExecuteCommand();
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
        var sessions = _db.Queryable<AiChatSession>().Where(x => x.UserId == user.Id && !x.IsDeleted).OrderByDescending(x => x.UpdatedAt).Take(limit).ToList();
        var ids = sessions.Select(x => x.Id).ToList();
        var messages = ids.Count == 0 ? new List<AiChatMessage>() : _db.Queryable<AiChatMessage>().Where(x => ids.Contains(x.SessionId)).OrderByDescending(x => x.Id).ToList();
        return Task.FromResult(sessions.Select(session => ToSummary(session, messages.Where(x => x.SessionId == session.Id).ToList())).ToList());
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

    private AiChatSession EnsureSession(AuthenticatedUser user, ChatCompleteRequest request)
    {
        var sessionId = request.SessionId?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64) sessionId = Guid.NewGuid().ToString("N");
        request.SessionId = sessionId;
        var session = _db.Queryable<AiChatSession>().First(x => x.Id == sessionId && !x.IsDeleted);
        if (session != null && session.UserId == user.Id) return session;
        if (session != null) {
            sessionId = Guid.NewGuid().ToString("N");
            request.SessionId = sessionId;
        }
        session = new AiChatSession { Id = sessionId, UserId = user.Id, Title = MakeTitle(request.Message), PreferencesJson = JsonSerializer.Serialize(new { knowledge_base_names = request.KnowledgeBaseNames, code_repository_names = request.CodeRepositoryNames, model_id = request.ModelId, mode = request.Mode }) };
        _db.Insertable(session).ExecuteCommand();
        return session;
    }

    private static ChatSessionSummaryDto ToSummary(AiChatSession session, List<AiChatMessage> messages) => new() { Id = session.Id, Title = session.Title, CreatedAt = session.CreatedAt, UpdatedAt = session.UpdatedAt, MessageCount = messages.Count, LastMessage = messages.FirstOrDefault()?.Content ?? string.Empty };
    private static string MakeTitle(string message) => string.IsNullOrWhiteSpace(message) ? "新会话" : message.Trim().Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(message.Trim().Replace('\r', ' ').Replace('\n', ' ').Length, 40)];
    private static Dictionary<string, object?> DeserializeObject(string? value) => string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<Dictionary<string, object?>>(value) ?? [];
    private static object? DeserializeValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<object>(value);
}
