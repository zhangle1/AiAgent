using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Dtos.Chat;
using System.Diagnostics;

namespace AiAgent.Backend.Services.Chat;

/// <summary>Request-scoped, non-persistent timing trace for a chat turn.</summary>
public sealed class ChatDebugTrace
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, long> _started = new(StringComparer.Ordinal);
    private readonly List<ChatDebugTraceEvent> _events = [];
    private readonly string _provider;
    private readonly string _transport;
    private bool _firstStreamEventRecorded;

    private ChatDebugTrace(string traceId, string provider, string transport)
    {
        TraceId = traceId;
        _provider = provider;
        _transport = transport;
    }

    public string TraceId { get; }

    public IReadOnlyList<ChatDebugTraceEvent> Events => _events;

    public static ChatDebugTrace? Create(ChatCompleteRequest request)
    {
        if (!request.DebugTrace) return null;
        var traceId = NormalizeTraceId(request.TraceId) ?? Guid.NewGuid().ToString("N");
        var isCodex = string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase);
        var isDsh = string.Equals(request.Agent?.Trim(), "deepseek-harness", StringComparison.OrdinalIgnoreCase);
        return new ChatDebugTrace(
            traceId,
            isCodex ? "codex" : isDsh ? "deepseek_harness" : "openai_compatible",
            isCodex ? "codex_app_server" : isDsh ? "sdk_jsonrpc_stdio" : "http_stream");
    }

    public AgentStreamEvent Start(string stage) => Add(stage, "started", null, null);

    public AgentStreamEvent Complete(string stage) => Add(stage, "completed", GetDuration(stage), null);

    public AgentStreamEvent CompleteProvider() => Add("provider_completed", "completed", GetDuration("provider_request_started"), null);

    public AgentStreamEvent FailProvider(string errorCode) => Add("provider_completed", "failed", GetDuration("provider_request_started"), errorCode);

    public AgentStreamEvent CancelProvider() => Add("provider_completed", "cancelled", GetDuration("provider_request_started"), "cancelled");

    public AgentStreamEvent Fail(string stage, string errorCode) => Add(stage, "failed", GetDuration(stage), errorCode);

    public AgentStreamEvent Cancel(string stage) => Add(stage, "cancelled", GetDuration(stage), "cancelled");

    public AgentStreamEvent? FirstStreamEvent()
    {
        if (_firstStreamEventRecorded) return null;
        _firstStreamEventRecorded = true;
        return Add("first_stream_event", "completed", null, null);
    }

    private AgentStreamEvent Add(string stage, string status, long? durationMs, string? errorCode)
    {
        var elapsedMs = _clock.ElapsedMilliseconds;
        if (status == "started") _started[stage] = elapsedMs;
        var traceEvent = new ChatDebugTraceEvent
        {
            TraceId = TraceId,
            Stage = stage,
            Status = status,
            ElapsedMs = Math.Max(0, elapsedMs),
            DurationMs = durationMs is null ? null : Math.Max(0, durationMs.Value),
            Provider = _provider,
            Transport = _transport,
            ErrorCode = errorCode
        };
        _events.Add(traceEvent);
        return new AgentStreamEvent { Type = "debug_trace", DebugTrace = traceEvent };
    }

    private long? GetDuration(string stage)
    {
        return _started.TryGetValue(stage, out var startedAt) ? _clock.ElapsedMilliseconds - startedAt : null;
    }

    private static string? NormalizeTraceId(string? traceId)
    {
        var normalized = traceId?.Trim();
        return !string.IsNullOrWhiteSpace(normalized)
            && normalized.Length <= 64
            && normalized.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            ? normalized
            : null;
    }
}
