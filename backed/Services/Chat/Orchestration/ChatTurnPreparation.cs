using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;

namespace AiAgent.Backend.Services.Chat.Orchestration;

internal sealed class ChatTurnPreparation(IChatSessionService sessions, IChatImageAttachmentService images,
    IChatFileAttachmentService files, ICodexModelPolicyService models, IImageOcrPolicyService imagePolicy,
    IProjectReferenceContextService projects, IMarkdownDocumentReferenceContextService markdown,
    IProjectAgentMarkdownIndexContextService projectIndex, Knowledge.KnowledgeResourceService? books = null)
{
    internal Task ResolveAttachmentsAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken token) =>
        ChatAttachmentPreparation.ResolveAsync(user, request, images, files, models, imagePolicy, token, books);

    internal async Task PrepareAndRecordUserMessageAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken token)
    {
        await ResolveAttachmentsAsync(user, request, token);
        await projects.ResolveAsync(user, request, token);
        await markdown.ResolveAsync(user, request, token);
        await projectIndex.ResolveAsync(user, request, token);
        ChatSkillCatalog.Apply(request);
        await sessions.RecordUserMessageAsync(user, request, token);
    }
}
