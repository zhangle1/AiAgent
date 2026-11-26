using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.AgentRuntime;

namespace AiAgent.Backend.Services.Chat;

/// <summary>
/// 默认聊天编排器。
/// </summary>
public sealed class ChatOrchestrator : IChatOrchestrator
{
    private readonly IAgentLoop _agentLoop;
    private readonly AgentProviderRegistry _providers;
    private readonly IDshChatService _dsh;
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRuntimeRequestFactory _runtimeRequestFactory;
    private readonly bool _nativeRuntimeEnabled;

    /// <summary>
    /// 初始化聊天编排器。
    /// </summary>
    public ChatOrchestrator(
        IAgentLoop agentLoop,
        AgentProviderRegistry providers,
        IDshChatService dsh,
        IRunCoordinator runCoordinator,
        IRuntimeRequestFactory runtimeRequestFactory,
        IConfiguration configuration)
    {
        _agentLoop = agentLoop;
        _providers = providers;
        _dsh = dsh;
        _runCoordinator = runCoordinator;
        _runtimeRequestFactory = runtimeRequestFactory;
        _nativeRuntimeEnabled = configuration.GetValue("AgentRuntime:NativeEnabled", false);
    }

    /// <summary>
    /// 创建 Agent 上下文并运行 Agent Loop。
    /// </summary>
    public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request, null, false, cancellationToken);

    public Task<ChatCompleteResponse> CompleteStreamingAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken) =>
        ExecuteAsync(request, onEvent, true, cancellationToken);

    private async Task<ChatCompleteResponse> ExecuteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, bool streaming, CancellationToken cancellationToken)
    {
        var agent = request.Agent?.Trim();
        if (request.Goal != null && !string.Equals(agent, CodexAgentProviderDriver.KindId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标模式仅支持 Codex app-server。");
        if (!string.IsNullOrWhiteSpace(agent))
        {
            if (_providers.ResolveDefault(agent) is { } instance)
                return await instance.ExecuteAsync(request, onEvent, cancellationToken);
            if (string.Equals(agent, "deepseek-harness", StringComparison.OrdinalIgnoreCase))
                return await _dsh.CompleteAsync(request, onEvent, cancellationToken);
            if (string.Equals(agent, "codebuddy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CodeBuddy CLI was detected but its app-server protocol is not yet supported.");
            throw new InvalidOperationException($"Unsupported external agent: {agent}.");
        }

        var context = AgentContext.FromRequest(request);
        if (string.IsNullOrWhiteSpace(context.UserMessage))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        if (!_nativeRuntimeEnabled)
        {
            var outcome = streaming
                ? await _agentLoop.RunStreamingAsync(context, onEvent, cancellationToken)
                : await _agentLoop.RunAsync(context, cancellationToken);
            return ToResponse(outcome);
        }

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
}
