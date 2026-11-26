using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat;

/// <summary>
/// 选择已配置的执行引擎，完成一次普通或流式聊天调用。
/// </summary>
public interface IChatOrchestrator
{
    /// <summary>
    /// 执行一次聊天完成。
    /// </summary>
    Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 执行一次流式聊天完成。
    /// </summary>
    Task<ChatCompleteResponse> CompleteStreamingAsync(
        ChatCompleteRequest request,
        AgentStreamEventHandler? onEvent,
        CancellationToken cancellationToken);
}
