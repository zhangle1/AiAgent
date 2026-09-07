using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Llm;

namespace AiAgent.Backend.Services.AgentRuntime;

/// <summary>
/// Transitional native runtime. It exposes the new stable contract while the
/// label-driven implementation is incrementally decomposed behind this boundary.
/// </summary>
public sealed class NativeAgentRuntime : IRuntimeEngine
{
    private readonly IAgentLoop _legacyLoop;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly INativeTurnRunner _nativeTurnRunner;
    private readonly ILlmChatClient? _llm;
    private readonly bool _nativeV2Enabled;

    /// <summary>Compatibility constructor for isolated tests and legacy hosts that do not register V2 services.</summary>
    public NativeAgentRuntime(IAgentLoop legacyLoop, IToolDispatcher toolDispatcher)
    {
        _legacyLoop = legacyLoop;
        _toolDispatcher = toolDispatcher;
        _nativeTurnRunner = new DisabledNativeTurnRunner();
        _llm = null;
        _nativeV2Enabled = false;
    }

    public NativeAgentRuntime(IAgentLoop legacyLoop, IToolDispatcher toolDispatcher, INativeTurnRunner nativeTurnRunner, IConfiguration configuration)
    {
        _legacyLoop = legacyLoop;
        _toolDispatcher = toolDispatcher;
        _nativeTurnRunner = nativeTurnRunner;
        _llm = null;
        _nativeV2Enabled = configuration.GetValue("AgentRuntime:NativeV2Enabled", false);
    }

    public NativeAgentRuntime(IAgentLoop legacyLoop, IToolDispatcher toolDispatcher, INativeTurnRunner nativeTurnRunner, ILlmChatClient llm, IConfiguration configuration)
        : this(legacyLoop, toolDispatcher, nativeTurnRunner, configuration)
    {
        _llm = llm;
    }

    public RuntimeKind Kind => RuntimeKind.Native;
    public string RuntimeVersion => _nativeV2Enabled ? "native-v2/1" : "native-bridge/2";
    public string ProtocolVersion => _nativeV2Enabled ? "aiagent-runtime/2" : "aiagent-runtime/2-bridge";

    public async Task<RuntimeTurnResult> ExecuteAsync(
        RuntimeTurnRequest request,
        RuntimeEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        var context = ToAgentContext(request);
        // Native V2 is an opt-in at both the deployment and model levels.
        // A global flag alone must not send tools to a provider that has not
        // been verified against the OpenAI-compatible wire protocol.
        if (_nativeV2Enabled && _llm?.GetCapabilities(request.ModelId).SupportsNativeToolCalling == true)
            return await _nativeTurnRunner.RunAsync(request, context, onEvent, cancellationToken);
        var turn = RuntimeTurnContext.Create(request);
        var checkpoint = new RuntimeExecutionCheckpoint { RunId = turn.RunId, TurnId = turn.TurnId };
        var toolDefinitions = _toolDispatcher.GetDefinitions(context);
        var itemSequence = 0L;
        RuntimeStepContext? activeStep = null;

        async Task EmitAsync(RuntimeEventKind kind, string? content = null, IReadOnlyDictionary<string, object?>? metadata = null, string? itemId = null)
        {
            if (onEvent is null) return;
            await onEvent(RuntimeEventProjector.New(request.RunId, ++itemSequence, kind, content, metadata, itemId: itemId), cancellationToken);
        }

        async Task StartStepAsync(int stepNumber)
        {
            if (activeStep is not null)
            {
                checkpoint.CompleteStep(activeStep.StepNumber);
                await EmitAsync(RuntimeEventKind.StepCompleted, metadata: activeStep.ToSafeMetadata(), itemId: activeStep.StepId);
            }

            activeStep = checkpoint.StartStep(RuntimeStepContext.Capture(turn, request, stepNumber, toolDefinitions));
            await EmitAsync(RuntimeEventKind.StepStarted, metadata: activeStep.ToSafeMetadata(), itemId: activeStep.StepId);
        }

        await EmitAsync(RuntimeEventKind.TurnStarted, metadata: new Dictionary<string, object?>
        {
            ["turn_id"] = turn.TurnId,
            ["maximum_steps"] = turn.MaximumSteps,
            ["maximum_tool_calls"] = turn.MaximumToolCalls
        });

        var outcome = await _legacyLoop.RunStreamingAsync(context, async (streamEvent, token) =>
        {
            if (string.Equals(streamEvent.Type, "loop", StringComparison.OrdinalIgnoreCase))
            {
                var stepNumber = ReadPositiveInt(streamEvent.Metadata, "iteration", activeStep?.StepNumber + 1 ?? 1);
                await StartStepAsync(stepNumber);
            }

            if (string.Equals(streamEvent.Type, "tool", StringComparison.OrdinalIgnoreCase))
                checkpoint.RecordToolCalls(ReadToolCallCount(streamEvent.Metadata));

            // The legacy loop emits its terminal "done" event before returning.
            // Close the active model step first so replay never observes a completed
            // turn with an unfinished step.
            if (string.Equals(streamEvent.Type, "done", StringComparison.OrdinalIgnoreCase) && activeStep is not null)
            {
                checkpoint.CompleteStep(activeStep.StepNumber);
                await EmitAsync(RuntimeEventKind.StepCompleted, metadata: activeStep.ToSafeMetadata(), itemId: activeStep.StepId);
                activeStep = null;
            }

            if (onEvent is null) return;
            var runtimeEvent = RuntimeEventProjector.FromLegacyStreamEvent(request.RunId, ++itemSequence, streamEvent);
            await onEvent(runtimeEvent, token);
        }, cancellationToken);

        if (activeStep is not null)
        {
            checkpoint.CompleteStep(activeStep.StepNumber);
            await EmitAsync(RuntimeEventKind.StepCompleted, metadata: activeStep.ToSafeMetadata(), itemId: activeStep.StepId);
        }
        checkpoint.Complete();

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

    private static int ReadPositiveInt(IReadOnlyDictionary<string, object?> metadata, string key, int fallback)
    {
        return metadata.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static int ReadToolCallCount(IReadOnlyDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("tool_call_ids", out var callIds) && callIds is IEnumerable<string> values)
            return values.Count();
        if (metadata.TryGetValue("tool_names", out var toolNames) && toolNames is IEnumerable<string> namedTools)
            return namedTools.Count();
        if (metadata.TryGetValue("tools", out var tools) && tools is IEnumerable<string> legacyToolNames)
            return legacyToolNames.Count();
        return ReadPositiveInt(metadata, "tool_calls", 1);
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

    private sealed class DisabledNativeTurnRunner : INativeTurnRunner
    {
        public Task<RuntimeTurnResult> RunAsync(RuntimeTurnRequest request, AgentContext context, RuntimeEventHandler? onEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Native V2 is not registered in this host.");
    }
}
