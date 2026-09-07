using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Chat.Llm;
using SqlSugar;

namespace AiAgent.Backend.Services.AgentRuntime;

public sealed record NativeThreadHistory(IReadOnlyList<LlmMessage> Messages, bool WasCompacted);

public interface INativeThreadHistoryStore
{
    NativeThreadHistory Load(RuntimeTurnRequest request);
}

/// <summary>
/// Reuses the owned chat-session transcript as model history. It deliberately
/// excludes thinking, metadata, attachment paths, and the just-persisted input.
/// </summary>
public sealed class NativeThreadHistoryStore : INativeThreadHistoryStore
{
    private const int MaximumMessages = 16;
    private const int MaximumCharacters = 18_000;
    private readonly ISqlSugarClient _db;

    public NativeThreadHistoryStore(ISqlSugarClient db) => _db = db;

    public NativeThreadHistory Load(RuntimeTurnRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ThreadId)) return new NativeThreadHistory([], false);
        var ownsSession = _db.Queryable<AiChatSession>().Any(session => session.Id == request.ThreadId && session.UserId == request.UserId && !session.IsDeleted);
        if (!ownsSession) return new NativeThreadHistory([], false);

        var rows = _db.Queryable<AiChatMessage>().Where(message => message.SessionId == request.ThreadId && (message.Role == "user" || message.Role == "assistant"))
            .OrderByDescending(message => message.Id).Take(MaximumMessages + 1).ToList();
        var newestFirst = rows.ToList();
        if (newestFirst.Count > 0 && string.Equals(newestFirst[0].Role, "user", StringComparison.OrdinalIgnoreCase)
            && string.Equals(newestFirst[0].Content, request.Input.Content, StringComparison.Ordinal))
            newestFirst.RemoveAt(0);

        var compacted = newestFirst.Count > MaximumMessages;
        var selectedNewestFirst = new List<LlmMessage>();
        var remaining = MaximumCharacters;
        foreach (var row in newestFirst)
        {
            var content = row.Content ?? string.Empty;
            if (content.Length > remaining)
            {
                compacted = true;
                continue;
            }
            selectedNewestFirst.Add(new LlmMessage { Role = row.Role, Content = content });
            remaining -= content.Length;
        }
        selectedNewestFirst.Reverse();
        return new NativeThreadHistory(selectedNewestFirst, compacted);
    }
}
