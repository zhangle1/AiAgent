using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Usage;

namespace AiAgent.Backend.Services.Chat.Orchestration;

internal sealed class ChatRunFinalizer(IChatSessionService sessions, IUsageStatisticsService usage)
{
    internal async Task SaveAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatCompleteResponse result,
        ChatProviderOutput? output, CancellationToken token)
    {
        await sessions.RecordAssistantMessageAsync(user, request, output?.Content ?? result.Content, output?.Thinking,
            output?.Citations ?? result.Citations, output?.ModelId ?? result.ModelId, output?.Model ?? result.Model, token);
        await usage.RecordAsync(user, request, result, token);
    }
}
