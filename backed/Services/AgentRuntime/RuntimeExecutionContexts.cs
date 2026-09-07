using System.Security.Cryptography;
using System.Text;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.AgentRuntime;

/// <summary>
/// Immutable server-side snapshot of a turn. It intentionally contains only ids,
/// selected capabilities, and budgets; prompts, file contents, and credentials
/// remain outside the runtime ledger.
/// </summary>
public sealed record RuntimeTurnContext(
    string RunId,
    string TurnId,
    string ThreadId,
    string UserId,
    RuntimeKind RuntimeKind,
    string? ModelId,
    DateTimeOffset StartedAt,
    int MaximumSteps,
    int MaximumToolCalls)
{
    public static RuntimeTurnContext Create(RuntimeTurnRequest request)
    {
        var maximumSteps = string.IsNullOrWhiteSpace(request.Capabilities.DashboardApplicationId) ? 5 : 8;
        return new RuntimeTurnContext(
            request.RunId,
            string.IsNullOrWhiteSpace(request.TurnId) ? request.RunId : request.TurnId,
            request.ThreadId,
            request.UserId,
            request.RuntimeKind,
            request.ModelId,
            DateTimeOffset.UtcNow,
            maximumSteps,
            maximumSteps * 4);
    }
}

/// <summary>
/// Immutable, safe-to-persist snapshot captured immediately before a model step.
/// This is the seam that lets a future native model client replace the legacy
/// text-label adapter without changing run history or tool visibility.
/// </summary>
public sealed record RuntimeStepContext(
    string StepId,
    string RunId,
    string TurnId,
    int StepNumber,
    string? ModelId,
    int ToolCount,
    string ToolSnapshotId,
    int ContextCharacterCount,
    int EstimatedContextTokens,
    string? WorkspaceRevision,
    DateTimeOffset CreatedAt)
{
    public IReadOnlyDictionary<string, object?> ToSafeMetadata() => new Dictionary<string, object?>
    {
        ["turn_id"] = TurnId,
        ["step_id"] = StepId,
        ["step"] = StepNumber,
        ["tool_count"] = ToolCount,
        ["tool_snapshot_id"] = ToolSnapshotId,
        ["context_characters"] = ContextCharacterCount,
        ["context_tokens_estimated"] = EstimatedContextTokens,
        ["workspace_revision_present"] = !string.IsNullOrWhiteSpace(WorkspaceRevision)
    };

    public static RuntimeStepContext Capture(RuntimeTurnContext turn, RuntimeTurnRequest request, int stepNumber, IReadOnlyList<ToolDefinition> tools)
    {
        var characters = ContextCharacters(request);
        return new RuntimeStepContext(
            Guid.NewGuid().ToString("N"),
            turn.RunId,
            turn.TurnId,
            stepNumber,
            request.ModelId,
            tools.Count,
            CreateToolSnapshotId(tools),
            characters,
            EstimateTokens(characters),
            request.Capabilities.DashboardWorkspaceRevision,
            DateTimeOffset.UtcNow);
    }

    internal static string CreateToolSnapshotId(IReadOnlyList<ToolDefinition> tools)
    {
        var canonical = string.Join("\n", tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase).Select(tool =>
            $"{tool.Name}|{tool.Description}|{string.Join(",", tool.Parameters.OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase).Select(parameter => $"{parameter.Name}:{parameter.Type}:{parameter.Required}"))}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static int ContextCharacters(RuntimeTurnRequest request)
    {
        return request.Input.Content.Length
            + request.Capabilities.MemoryContext.Length
            + request.Capabilities.ProjectReferenceContext.Length
            + request.Capabilities.MarkdownDocumentContext.Length
            + request.Capabilities.ProjectAgentMarkdownIndexContext.Length
            + request.Input.Attachments.Sum(attachment => attachment.ExtractedText?.Length ?? 0);
    }

    private static int EstimateTokens(int characters) => characters == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(characters / 3.6d));
}

public enum RuntimeExecutionPhase
{
    Running,
    ExecutingTool,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// In-memory execution position for the active turn. Run events are the durable
/// audit source today; this object keeps the state explicit until durable resume
/// is introduced in a later phase.
/// </summary>
public sealed class RuntimeExecutionCheckpoint
{
    public string RunId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public RuntimeExecutionPhase Phase { get; private set; } = RuntimeExecutionPhase.Running;
    public string? CurrentStepId { get; private set; }
    public int LastCompletedStep { get; private set; }
    public int TotalToolCalls { get; private set; }
    public string ToolSnapshotId { get; private set; } = string.Empty;

    public RuntimeStepContext StartStep(RuntimeStepContext step)
    {
        CurrentStepId = step.StepId;
        ToolSnapshotId = step.ToolSnapshotId;
        Phase = RuntimeExecutionPhase.Running;
        return step;
    }

    public void CompleteStep(int stepNumber)
    {
        LastCompletedStep = Math.Max(LastCompletedStep, stepNumber);
        CurrentStepId = null;
        if (Phase is RuntimeExecutionPhase.Running or RuntimeExecutionPhase.ExecutingTool)
            Phase = RuntimeExecutionPhase.Running;
    }

    public void RecordToolCalls(int count)
    {
        if (count <= 0) return;
        TotalToolCalls += count;
        Phase = RuntimeExecutionPhase.ExecutingTool;
    }

    public void Complete() => Phase = RuntimeExecutionPhase.Completed;
    public void Fail() => Phase = RuntimeExecutionPhase.Failed;
    public void Cancel() => Phase = RuntimeExecutionPhase.Cancelled;
}
