using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.AgentRuntime;

public static class RuntimeEventProjector
{
    public static RuntimeEvent FromLegacyStreamEvent(string runId, long sequence, AgentStreamEvent source)
    {
        var kind = source.Type switch
        {
            "tool" => RuntimeEventKind.ToolCallStarted,
            "tool_result" => RuntimeEventKind.ToolCallCompleted,
            "done" => RuntimeEventKind.TurnCompleted,
            "error" => RuntimeEventKind.TurnFailed,
            "content" or "thinking" => RuntimeEventKind.ItemDelta,
            _ => RuntimeEventKind.ItemStarted
        };

        return New(runId, sequence, kind, source.Content, source.Metadata, source.Citations);
    }

    public static AgentStreamEvent ToAgentStreamEvent(RuntimeEvent source)
    {
        var type = source.Kind switch
        {
            RuntimeEventKind.ToolCallStarted => "tool",
            RuntimeEventKind.ToolCallCompleted => "tool_result",
            RuntimeEventKind.TurnCompleted => "done",
            RuntimeEventKind.TurnFailed => "error",
            RuntimeEventKind.ItemDelta => "content",
            RuntimeEventKind.RunStatusChanged => "runtime_status",
            _ => "runtime"
        };

        var metadata = source.Metadata is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source.Metadata);
        metadata["run_id"] = source.RunId;
        metadata["event_id"] = source.EventId;
        metadata["sequence"] = source.Sequence;
        if (source.Status is not null) metadata["run_status"] = source.Status.ToString()!.ToLowerInvariant();

        return new AgentStreamEvent
        {
            Type = type,
            Content = source.Content ?? string.Empty,
            Citations = source.Citations?.ToList(),
            Metadata = metadata
        };
    }

    public static RuntimeEvent New(
        string runId,
        long sequence,
        RuntimeEventKind kind,
        string? content = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<AiAgent.Backend.Dtos.Knowledge.KnowledgeCitationDto>? citations = null,
        TurnRunStatus? status = null,
        string? itemId = null)
    {
        return new RuntimeEvent(
            Guid.NewGuid().ToString("N"), runId, sequence, DateTimeOffset.UtcNow,
            kind, ItemId: itemId, Content: content, Status: status, Metadata: metadata, Citations: citations);
    }
}
