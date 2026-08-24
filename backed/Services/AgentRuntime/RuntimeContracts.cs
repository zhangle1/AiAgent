using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Knowledge;

namespace AiAgent.Backend.Services.AgentRuntime;

public enum RuntimeKind
{
    Native,
    Codex
}

public enum TurnRunStatus
{
    Draft,
    Queued,
    Starting,
    Running,
    WaitingApproval,
    WaitingInput,
    Completed,
    Failed,
    Cancelled,
    Expired
}

public enum RuntimeEventKind
{
    RunStatusChanged,
    TurnStarted,
    ItemStarted,
    ItemDelta,
    ItemCompleted,
    ToolCallStarted,
    ToolCallCompleted,
    ApprovalRequested,
    UsageUpdated,
    TurnCompleted,
    TurnFailed
}

public sealed record RuntimeAttachment(
    string Type,
    string FileName,
    string ContentType,
    string Reference,
    string ExtractedText);

public sealed record RuntimeTurnInput(
    string Content,
    string Mode,
    IReadOnlyList<RuntimeAttachment> Attachments);

public sealed record RuntimeCapabilitySnapshot(
    IReadOnlyList<string> KnowledgeBaseNames,
    IReadOnlyList<string> CodeRepositoryNames,
    string? DashboardApplicationId,
    string? DashboardFilePath,
    string? DashboardWorkspaceRevision,
    int TopK,
    string MemoryContext,
    string ProjectReferenceContext,
    string MarkdownDocumentContext,
    string ProjectAgentMarkdownIndexContext);

public sealed record RuntimeTurnRequest(
    string RunId,
    string ThreadId,
    RuntimeKind RuntimeKind,
    string? ModelId,
    RuntimeTurnInput Input,
    RuntimeCapabilitySnapshot Capabilities,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record RuntimeEvent(
    string EventId,
    string RunId,
    long Sequence,
    DateTimeOffset CreatedAt,
    RuntimeEventKind Kind,
    string? ItemId = null,
    string? Content = null,
    TurnRunStatus? Status = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    IReadOnlyList<KnowledgeCitationDto>? Citations = null);

public sealed record RuntimeTurnResult(
    string Query,
    string Answer,
    string? ModelId,
    string? Model,
    string? KnowledgeBaseName,
    IReadOnlyList<KnowledgeCitationDto> Citations,
    ChatTokenUsage? Usage,
    bool Completed);

public delegate Task RuntimeEventHandler(RuntimeEvent runtimeEvent, CancellationToken cancellationToken);

public interface IRuntimeEngine
{
    RuntimeKind Kind { get; }
    string RuntimeVersion { get; }
    string ProtocolVersion { get; }

    Task<RuntimeTurnResult> ExecuteAsync(
        RuntimeTurnRequest request,
        RuntimeEventHandler? onEvent,
        CancellationToken cancellationToken);
}
