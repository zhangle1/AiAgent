using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.AgentRuntime;

public interface IRuntimeRequestFactory
{
    RuntimeTurnRequest Create(AgentContext context, RuntimeKind runtimeKind = RuntimeKind.Native);
}

public sealed class RuntimeRequestFactory : IRuntimeRequestFactory
{
    public RuntimeTurnRequest Create(AgentContext context, RuntimeKind runtimeKind = RuntimeKind.Native)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new RuntimeTurnRequest(
            Guid.NewGuid().ToString("N"),
            context.RuntimeUserId,
            context.SessionId,
            runtimeKind,
            context.ModelId,
            new RuntimeTurnInput(
                context.UserMessage,
                context.Mode,
                context.Attachments.Select(x => new RuntimeAttachment(
                    x.Type, x.FileName, x.ContentType, x.Url, x.ExtractedText)).ToArray()),
            new RuntimeCapabilitySnapshot(
                context.KnowledgeBaseNames.ToArray(),
                context.CodeRepositoryNames.ToArray(),
                context.DashboardApplicationId,
                context.DashboardFilePath,
                context.DashboardWorkspaceRevision,
                context.TopK,
                context.MemoryContext,
                context.ProjectReferenceContext,
                context.MarkdownDocumentContext,
                context.ProjectAgentMarkdownIndexContext),
            new Dictionary<string, object?>(context.Metadata));
    }
}
