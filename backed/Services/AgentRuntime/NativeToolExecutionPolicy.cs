using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Planning;

namespace AiAgent.Backend.Services.AgentRuntime;

/// <summary>
/// Conservative concurrency policy. Only stateless retrieval/index queries may
/// run together; workspace and file operations remain ordered by the model.
/// </summary>
public static class NativeToolExecutionPolicy
{
    private static readonly HashSet<string> ParallelReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        AgentToolNames.RagSearch,
        AgentToolNames.ReadPageRange,
        AgentToolNames.CodeRepositoryOverview,
        AgentToolNames.CodeSearch,
        AgentToolNames.FindSymbol
    };

    public static bool IsSafeReadOnly(ToolCall call) => ParallelReadOnlyTools.Contains(call.Name);

    public static bool CanExecuteInParallel(IReadOnlyList<ToolCall> calls) => calls.Count > 1 && calls.All(IsSafeReadOnly);
}
