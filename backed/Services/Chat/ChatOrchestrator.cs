using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.AgentRuntime;

namespace AiAgent.Backend.Services.Chat;

/// <summary>
/// 聊天编排器，负责把 HTTP 请求转换为 AgentContext，并调用 AgentLoop 完成一次回答。
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

/// <summary>
/// 默认聊天编排器。
/// </summary>
public sealed class ChatOrchestrator : IChatOrchestrator
{
    private readonly IAgentLoop _agentLoop;
    private readonly ICodexChatService _codex;
    private readonly IDshChatService _dsh;
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRuntimeRequestFactory _runtimeRequestFactory;
    private readonly bool _nativeRuntimeEnabled;

    /// <summary>
    /// 初始化聊天编排器。
    /// </summary>
    public ChatOrchestrator(
        IAgentLoop agentLoop,
        ICodexChatService codex,
        IDshChatService dsh,
        IRunCoordinator runCoordinator,
        IRuntimeRequestFactory runtimeRequestFactory,
        IConfiguration configuration)
    {
        _agentLoop = agentLoop;
        _codex = codex;
        _dsh = dsh;
        _runCoordinator = runCoordinator;
        _runtimeRequestFactory = runtimeRequestFactory;
        _nativeRuntimeEnabled = configuration.GetValue("AgentRuntime:NativeEnabled", false);
    }

    /// <summary>
    /// 创建 Agent 上下文并运行 Agent Loop。
    /// </summary>
    public async Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        EnsureSupportedExternalAgent(request);
        if (IsCodexRequest(request))
        {
            return await _codex.CompleteAsync(request, null, cancellationToken);
        }
        if (IsDshRequest(request))
        {
            return await _dsh.CompleteAsync(request, null, cancellationToken);
        }

        var context = AgentContext.FromRequest(request);
        if (string.IsNullOrWhiteSpace(context.UserMessage))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        if (!_nativeRuntimeEnabled)
            return ToResponse(await _agentLoop.RunAsync(context, cancellationToken));

        return ToResponse(await _runCoordinator.RunAsync(_runtimeRequestFactory.Create(context), null, cancellationToken));
    }

    /// <summary>
    /// 创建 Agent 上下文并运行流式 Agent Loop。
    /// </summary>
    public async Task<ChatCompleteResponse> CompleteStreamingAsync(
        ChatCompleteRequest request,
        AgentStreamEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        EnsureSupportedExternalAgent(request);
        if (IsCodexRequest(request))
        {
            return await _codex.CompleteAsync(request, onEvent, cancellationToken);
        }
        if (IsDshRequest(request))
        {
            return await _dsh.CompleteAsync(request, onEvent, cancellationToken);
        }

        var context = AgentContext.FromRequest(request);
        if (string.IsNullOrWhiteSpace(context.UserMessage))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        if (!_nativeRuntimeEnabled)
            return ToResponse(await _agentLoop.RunStreamingAsync(context, onEvent, cancellationToken));

        RuntimeEventHandler? runtimeHandler = onEvent is null
            ? null
            : async (runtimeEvent, token) => await onEvent(RuntimeEventProjector.ToAgentStreamEvent(runtimeEvent), token);
        return ToResponse(await _runCoordinator.RunAsync(_runtimeRequestFactory.Create(context), runtimeHandler, cancellationToken));
    }

    private static ChatCompleteResponse ToResponse(AgentLoopOutcome outcome)
    {
        return new ChatCompleteResponse
        {
            Query = outcome.Query,
            Answer = outcome.Answer,
            Content = outcome.Answer,
            ModelId = outcome.ModelId,
            Model = outcome.Model,
            KnowledgeBaseName = outcome.KnowledgeBaseName,
            Citations = outcome.Citations,
            Usage = outcome.Usage
        };
    }

    private static ChatCompleteResponse ToResponse(RuntimeTurnResult outcome)
    {
        return new ChatCompleteResponse
        {
            Query = outcome.Query,
            Answer = outcome.Answer,
            Content = outcome.Answer,
            ModelId = outcome.ModelId,
            Model = outcome.Model,
            KnowledgeBaseName = outcome.KnowledgeBaseName,
            Citations = outcome.Citations.ToList(),
            Usage = outcome.Usage
        };
    }

    private static bool IsCodexRequest(ChatCompleteRequest request) => string.Equals(request.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase);
    private static bool IsDshRequest(ChatCompleteRequest request) => string.Equals(request.Agent?.Trim(), "deepseek-harness", StringComparison.OrdinalIgnoreCase);

    private static void EnsureSupportedExternalAgent(ChatCompleteRequest request)
    {
        var agent = request.Agent?.Trim();
        if (string.IsNullOrWhiteSpace(agent) || string.Equals(agent, "codex", StringComparison.OrdinalIgnoreCase) || string.Equals(agent, "deepseek-harness", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(agent, "codebuddy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CodeBuddy CLI was detected but its app-server protocol is not yet supported.");
        throw new InvalidOperationException($"Unsupported external agent: {agent}.");
    }
}
