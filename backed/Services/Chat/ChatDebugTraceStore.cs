using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IChatDebugTraceStore
{
    Task SaveAsync(AuthenticatedUser user, string? sessionId, ChatDebugTrace? trace, CancellationToken cancellationToken);
    Task<List<ChatDebugTraceRecordDto>> ListAsync(AuthenticatedUser user, string sessionId, CancellationToken cancellationToken);
}

/// <summary>Short-lived, owner-scoped storage for already-redacted chat timing traces.</summary>
public sealed class ChatDebugTraceStore : IChatDebugTraceStore
{
    private readonly ISqlSugarClient _db;
    private readonly int _retentionDays;
    private readonly ILogger<ChatDebugTraceStore> _logger;

    public ChatDebugTraceStore(ISqlSugarClient db, IConfiguration configuration, ILogger<ChatDebugTraceStore> logger)
    {
        _db = db;
        _retentionDays = Math.Clamp(configuration.GetValue<int?>("ChatDebugTrace:RetentionDays") ?? 7, 1, 30);
        _logger = logger;
    }

    public Task SaveAsync(AuthenticatedUser user, string? sessionId, ChatDebugTrace? trace, CancellationToken cancellationToken)
    {
        if (trace == null || string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64 || trace.Events.Count == 0) return Task.CompletedTask;
        try
        {
            var now = DateTime.UtcNow;
            CleanupExpired(now);
            var events = trace.Events.ToList();
            _db.Insertable(new AiChatDebugTrace
            {
                UserId = user.Id,
                SessionId = sessionId.Trim(),
                TraceId = trace.TraceId,
                Provider = events[0].Provider,
                Transport = events[0].Transport,
                EventsJson = JsonSerializer.Serialize(events),
                CreatedAt = now,
                ExpiresAt = now.AddDays(_retentionDays)
            }).ExecuteCommand();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Chat debug trace storage failed. TraceId={TraceId}", trace.TraceId);
        }
        return Task.CompletedTask;
    }

    public Task<List<ChatDebugTraceRecordDto>> ListAsync(AuthenticatedUser user, string sessionId, CancellationToken cancellationToken)
    {
        var normalizedSessionId = sessionId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSessionId) || normalizedSessionId.Length > 64) return Task.FromResult(new List<ChatDebugTraceRecordDto>());
        try
        {
            var now = DateTime.UtcNow;
            CleanupExpired(now);
            var ownsSession = _db.Queryable<AiChatSession>().Any(item => item.Id == normalizedSessionId && item.UserId == user.Id && !item.IsDeleted);
            if (!ownsSession) return Task.FromResult(new List<ChatDebugTraceRecordDto>());
            var records = _db.Queryable<AiChatDebugTrace>()
                .Where(item => item.UserId == user.Id && item.SessionId == normalizedSessionId && item.ExpiresAt > now)
                .OrderByDescending(item => item.CreatedAt)
                .Take(30)
                .ToList();
            return Task.FromResult(records.Select(item => new ChatDebugTraceRecordDto
            {
                TraceId = item.TraceId,
                Provider = item.Provider,
                Transport = item.Transport,
                CreatedAt = item.CreatedAt,
                ExpiresAt = item.ExpiresAt,
                Events = DeserializeEvents(item.EventsJson)
            }).ToList());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Chat debug trace query failed. SessionId={SessionId}", normalizedSessionId);
            return Task.FromResult(new List<ChatDebugTraceRecordDto>());
        }
    }

    private void CleanupExpired(DateTime now) => _db.Deleteable<AiChatDebugTrace>().Where(item => item.ExpiresAt <= now).ExecuteCommand();

    private static List<ChatDebugTraceEvent> DeserializeEvents(string? value)
    {
        try { return JsonSerializer.Deserialize<List<ChatDebugTraceEvent>>(value ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }
}
