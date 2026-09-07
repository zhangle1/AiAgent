using System.Text.Json;
using System.Text.Json.Serialization;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using SqlSugar;

namespace AiAgent.Backend.Services.AgentRuntime;

public sealed record AgentRunSummaryDto(
    [property: JsonPropertyName("run_id")] string RunId, [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("runtime_kind")] string RuntimeKind, [property: JsonPropertyName("runtime_version")] string RuntimeVersion,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion, [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("model_id")] string? ModelId, [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens, [property: JsonPropertyName("total_tokens")] int TotalTokens,
    [property: JsonPropertyName("tool_calls")] int ToolCalls, [property: JsonPropertyName("file_changes")] int FileChanges,
    [property: JsonPropertyName("error_code")] string? ErrorCode, [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt, [property: JsonPropertyName("completed_at")] DateTime? CompletedAt);

public sealed record AgentRunEventDto([property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("event_type")] string EventType, [property: JsonPropertyName("item_id")] string? ItemId,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt, [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("content_preview")] string? ContentPreview,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, object?> Metadata);

public sealed record AgentRunDetailDto([property: JsonPropertyName("run")] AgentRunSummaryDto Run,
    [property: JsonPropertyName("events")] IReadOnlyList<AgentRunEventDto> Events);

public interface IAgentRunStore
{
    void Create(RuntimeTurnRequest request, IRuntimeEngine engine);
    void Append(RuntimeEvent runtimeEvent);
    void UpdateStatus(string runId, TurnRunStatus status, string? errorCode = null);
    void Complete(string runId, RuntimeTurnResult result);
    IReadOnlyList<AgentRunSummaryDto> List(AuthenticatedUser user, string sessionId, int limit);
    AgentRunDetailDto? Get(AuthenticatedUser user, string runId);
}

public sealed class AgentRunStore : IAgentRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqlSugarClient _db;

    public AgentRunStore(ISqlSugarClient db) => _db = db;

    public void Create(RuntimeTurnRequest request, IRuntimeEngine engine)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ThreadId))
            throw new InvalidOperationException("A persisted run requires an authenticated owner and session.");
        var now = DateTime.UtcNow;
        _db.Insertable(new AiAgentRun
        {
            RunId = request.RunId, UserId = request.UserId, SessionId = request.ThreadId,
            RuntimeKind = request.RuntimeKind.ToString().ToLowerInvariant(), RuntimeVersion = engine.RuntimeVersion,
            ProtocolVersion = engine.ProtocolVersion, Status = "queued", ModelId = request.ModelId,
            PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0, ToolCalls = 0, FileChanges = 0,
            CreatedAt = now, UpdatedAt = now
        }).ExecuteCommand();
    }

    public void Append(RuntimeEvent runtimeEvent)
    {
        var metadata = SanitizeMetadata(runtimeEvent.Metadata);
        var payload = new StoredEventPayload(
            runtimeEvent.Status?.ToString().ToLowerInvariant(),
            null, metadata);
        _db.Insertable(new AiAgentRunEvent
        {
            RunId = runtimeEvent.RunId, Sequence = runtimeEvent.Sequence,
            EventType = runtimeEvent.Kind.ToString(), ItemId = runtimeEvent.ItemId,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions), CreatedAt = runtimeEvent.CreatedAt.UtcDateTime
        }).ExecuteCommand();

        var toolCalls = runtimeEvent.Kind == RuntimeEventKind.ToolCallStarted ? 1 : 0;
        var fileChanges = ReadInt(metadata, "file_changes");
        if (toolCalls > 0 || fileChanges > 0)
            _db.Updateable<AiAgentRun>()
                .SetColumns(x => new AiAgentRun { ToolCalls = (x.ToolCalls ?? 0) + toolCalls, FileChanges = (x.FileChanges ?? 0) + fileChanges })
                .Where(x => x.RunId == runtimeEvent.RunId).ExecuteCommand();
    }

    public void UpdateStatus(string runId, TurnRunStatus status, string? errorCode = null)
    {
        var now = DateTime.UtcNow;
        var terminal = status is TurnRunStatus.Completed or TurnRunStatus.Failed or TurnRunStatus.Cancelled or TurnRunStatus.Expired;
        _db.Updateable<AiAgentRun>().SetColumns(x => new AiAgentRun
        {
            Status = status.ToString().ToLowerInvariant(), UpdatedAt = now,
            CompletedAt = terminal ? now : x.CompletedAt, ErrorCode = NormalizeErrorCode(errorCode)
        }).Where(x => x.RunId == runId).ExecuteCommand();
    }

    public void Complete(string runId, RuntimeTurnResult result)
    {
        var now = DateTime.UtcNow;
        _db.Updateable<AiAgentRun>().SetColumns(x => new AiAgentRun
        {
            Status = "completed", ModelId = result.ModelId ?? x.ModelId,
            PromptTokens = result.Usage == null ? x.PromptTokens : result.Usage.PromptTokens,
            CompletionTokens = result.Usage == null ? x.CompletionTokens : result.Usage.CompletionTokens,
            TotalTokens = result.Usage == null ? x.TotalTokens : result.Usage.TotalTokens,
            UpdatedAt = now, CompletedAt = now
        }).Where(x => x.RunId == runId).ExecuteCommand();
    }

    public IReadOnlyList<AgentRunSummaryDto> List(AuthenticatedUser user, string sessionId, int limit)
    {
        if (!_db.Queryable<AiChatSession>().Any(x => x.Id == sessionId && x.UserId == user.Id && !x.IsDeleted)) return [];
        return _db.Queryable<AiAgentRun>().Where(x => x.UserId == user.Id && x.SessionId == sessionId)
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 100)).ToList().Select(ToSummary).ToList();
    }

    public AgentRunDetailDto? Get(AuthenticatedUser user, string runId)
    {
        var run = _db.Queryable<AiAgentRun>().First(x => x.RunId == runId && x.UserId == user.Id);
        if (run == null) return null;
        var events = _db.Queryable<AiAgentRunEvent>().Where(x => x.RunId == runId).OrderBy(x => x.Sequence).ToList()
            .Select(ToEvent).ToList();
        return new AgentRunDetailDto(ToSummary(run), events);
    }

    private static AgentRunSummaryDto ToSummary(AiAgentRun x) => new(x.RunId, x.SessionId ?? string.Empty,
        x.RuntimeKind ?? "unknown", x.RuntimeVersion ?? "unknown", x.ProtocolVersion ?? "unknown", x.Status ?? "unknown",
        x.ModelId, x.PromptTokens ?? 0, x.CompletionTokens ?? 0, x.TotalTokens ?? 0, x.ToolCalls ?? 0, x.FileChanges ?? 0,
        x.ErrorCode, x.CreatedAt ?? DateTime.MinValue, x.UpdatedAt ?? DateTime.MinValue, x.CompletedAt);

    private static AgentRunEventDto ToEvent(AiAgentRunEvent x)
    {
        StoredEventPayload? payload = null;
        try { payload = JsonSerializer.Deserialize<StoredEventPayload>(x.PayloadJson ?? "{}", JsonOptions); } catch (JsonException) { }
        return new AgentRunEventDto(x.Sequence ?? 0, x.EventType ?? "unknown", x.ItemId, x.CreatedAt ?? DateTime.MinValue,
            payload?.Status, payload?.ContentPreview, payload?.Metadata ?? new Dictionary<string, object?>());
    }

    private static Dictionary<string, object?> SanitizeMetadata(IReadOnlyDictionary<string, object?>? source)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "iteration", "llm_calls", "tool_calls", "prompt_tokens", "completion_tokens", "total_tokens", "elapsed_seconds", "tool_names", "tool_call_ids", "file_changes", "changed_files", "turn_id", "step_id", "step", "tool_count", "tool_snapshot_id", "context_characters", "context_tokens_estimated", "workspace_revision_present", "maximum_steps", "maximum_tool_calls" };
        return source?.Where(x => allowed.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value) ?? [];
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> metadata, string key) => metadata.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var result) ? Math.Max(0, result) : 0;
    private static string? NormalizeErrorCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "runtime_failed";
    private sealed record StoredEventPayload(string? Status, string? ContentPreview, Dictionary<string, object?> Metadata);
}
