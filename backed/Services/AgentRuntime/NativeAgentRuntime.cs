using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.AgentRuntime;

/// <summary>
/// Transitional native runtime. It exposes the new stable contract while the
/// label-driven implementation is incrementally decomposed behind this boundary.
/// </summary>
public sealed class NativeAgentRuntime : IRuntimeEngine
{
    private readonly IAgentLoop _legacyLoop;

    public NativeAgentRuntime(IAgentLoop legacyLoop)
    {
        _legacyLoop = legacyLoop;
    }

    public RuntimeKind Kind => RuntimeKind.Native;
    public string RuntimeVersion => "native-bridge/1";
    public string ProtocolVersion => "aiagent-runtime/1";

    public async Task<RuntimeTurnResult> ExecuteAsync(
        RuntimeTurnRequest request,
        RuntimeEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        var context = ToAgentContext(request);
        var itemSequence = 0L;
        var outcome = await _legacyLoop.RunStreamingAsync(context, async (streamEvent, token) =>
        {
            if (onEvent is null) return;
            var runtimeEvent = RuntimeEventProjector.FromLegacyStreamEvent(request.RunId, ++itemSequence, streamEvent);
            await onEvent(runtimeEvent, token);
        }, cancellationToken);

        return new RuntimeTurnResult(
            outcome.Query,
            outcome.Answer,
            outcome.ModelId,
            outcome.Model,
            outcome.KnowledgeBaseName,
            outcome.Citations,
            outcome.Usage,
            outcome.Completed);
    }

    private static AgentContext ToAgentContext(RuntimeTurnRequest request)
    {
        return new AgentContext
        {
            RuntimeUserId = request.UserId,
            SessionId = request.ThreadId,
            UserMessage = request.Input.Content,
            Mode = request.Input.Mode,
            ModelId = request.ModelId,
            KnowledgeBaseNames = request.Capabilities.KnowledgeBaseNames.ToList(),
            KnowledgeBaseName = request.Capabilities.KnowledgeBaseNames.FirstOrDefault(),
            CodeRepositoryNames = request.Capabilities.CodeRepositoryNames.ToList(),
            DashboardApplicationId = request.Capabilities.DashboardApplicationId,
            DashboardFilePath = request.Capabilities.DashboardFilePath,
            DashboardWorkspaceRevision = request.Capabilities.DashboardWorkspaceRevision,
            TopK = request.Capabilities.TopK,
            MemoryContext = request.Capabilities.MemoryContext,
            ProjectReferenceContext = request.Capabilities.ProjectReferenceContext,
            MarkdownDocumentContext = request.Capabilities.MarkdownDocumentContext,
            ProjectAgentMarkdownIndexContext = request.Capabilities.ProjectAgentMarkdownIndexContext,
            Attachments = request.Input.Attachments.Select(x => new AgentAttachment
            {
                Type = x.Type,
                FileName = x.FileName,
                ContentType = x.ContentType,
                Url = x.Reference,
                ExtractedText = x.ExtractedText
            }).ToList(),
            Metadata = new Dictionary<string, object?>(request.Metadata)
        };
    }
}
