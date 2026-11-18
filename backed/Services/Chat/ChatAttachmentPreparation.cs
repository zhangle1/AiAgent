using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;

namespace AiAgent.Backend.Services.Chat;

internal static class ChatAttachmentPreparation
{
    internal static async Task ResolveAsync(AuthenticatedUser user, ChatCompleteRequest request,
        IChatImageAttachmentService images, IChatFileAttachmentService files, ICodexModelPolicyService models,
        IImageOcrPolicyService imagePolicy, CancellationToken cancellationToken)
    {
        var isCodex = string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase);
        if (request.AttachmentIds.Count > 0)
        {
            if (!isCodex) throw new InvalidOperationException("Image attachments are currently supported only when Codex local agent is selected.");
            var model = models.ResolveModel(request.CodexModelId, request.CodexReasoningEffort);
            model.EnsureImageInputEnabled(imagePolicy.GetPolicy());
            var attachments = await images.ResolveLocalAttachmentsAsync(user, request.SessionId, request.AttachmentIds, cancellationToken);
            request.LocalImagePaths = attachments.Select(item => item.LocalPath).ToList();
        }
        if (request.DocumentAttachmentIds.Count > 0)
        {
            if (!isCodex) throw new InvalidOperationException("Document attachments are currently supported only when Codex local agent is selected.");
            var attachments = await files.ResolveLocalAttachmentsAsync(user, request.SessionId, request.DocumentAttachmentIds, cancellationToken);
            request.ServerAttachmentContext = await files.ExtractContextAsync(attachments, cancellationToken);
        }
    }
}
