using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Chat.Planning;
using AiAgent.Backend.Services.Chat.Prompting;

namespace AiAgent.Backend.Services.Chat.Agentic;

/// <summary>
/// Runs the label-driven chat agent loop.
/// </summary>
public interface IAgentLoop
{
    /// <summary>
    /// Runs one chat agent turn.
    /// </summary>
    Task<AgentLoopOutcome> RunAsync(AgentContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one streaming chat agent turn.
    /// </summary>
    Task<AgentLoopOutcome> RunStreamingAsync(AgentContext context, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Default label-driven agent loop.
/// </summary>
public sealed class AgentLoop : IAgentLoop
{
    private readonly IToolDispatcher _toolDispatcher;
    private readonly IChatPromptBuilder _promptBuilder;
    private readonly ILabeledStepRunner _labeledStepRunner;

    /// <summary>
    /// Creates the loop with tool dispatch, prompt building, and LLM step execution.
    /// </summary>
    public AgentLoop(
        IToolDispatcher toolDispatcher,
        IChatPromptBuilder promptBuilder,
        ILabeledStepRunner labeledStepRunner)
    {
        _toolDispatcher = toolDispatcher;
        _promptBuilder = promptBuilder;
        _labeledStepRunner = labeledStepRunner;
    }

    /// <summary>
    /// Runs a non-streaming turn by collecting the streaming result.
    /// </summary>
    public async Task<AgentLoopOutcome> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return await RunStreamingAsync(context, null, cancellationToken);
    }

    /// <summary>
    /// Lets the model choose labels and tools until it emits FINISH or reaches the iteration limit.
    /// </summary>
    public async Task<AgentLoopOutcome> RunStreamingAsync(AgentContext context, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var plan = CreateInitialPlan(context);
        var dispatch = new ToolDispatchOutcome();
        var toolDefinitions = _toolDispatcher.GetDefinitions();
        var messages = _promptBuilder.BuildMessages(context, plan, dispatch, toolDefinitions).ToList();
        LabeledStepResult? step = null;

        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId))
        {
            var inspection = await DispatchToolCallsAsync(context,
            [
                new ToolCall { Name = AgentToolNames.InspectDashboardWorkspace }
            ], onEvent, cancellationToken);
            dispatch.Results.AddRange(inspection.Results);
            dispatch.Citations.AddRange(inspection.Citations);
            messages.Add(new LlmMessage
            {
                Role = "user",
                Content = BuildToolObservationPrompt(inspection)
            });
        }

        var maximumIterations = string.IsNullOrWhiteSpace(context.DashboardApplicationId) ? 5 : 8;
        for (var iteration = 0; iteration < maximumIterations; iteration++)
        {
            context.Stats.Iteration = iteration + 1;
            await EmitAsync(onEvent, new AgentStreamEvent
            {
                Type = "loop",
                Content = $"iteration:{iteration + 1}",
                Metadata = context.Stats.ToMetadata()
            }, cancellationToken);

            step = await _labeledStepRunner.RunStreamingAsync(context, messages, onEvent, cancellationToken);
            if (step.Label == AgentLabels.Finish)
            {
                break;
            }

            messages.Add(new LlmMessage
            {
                Role = "assistant",
                Content = $"{step.Label}\n{step.Text}"
            });

            if (step.Label == AgentLabels.Tool && step.ToolCalls.Count > 0 && (context.KnowledgeBaseNames.Count > 0 || context.CodeRepositoryNames.Count > 0 || !string.IsNullOrWhiteSpace(context.DashboardApplicationId)))
            {
                var toolDispatch = await DispatchToolCallsAsync(context, step.ToolCalls, onEvent, cancellationToken);
                dispatch.Results.AddRange(toolDispatch.Results);
                dispatch.Citations.AddRange(toolDispatch.Citations);
                messages.Add(new LlmMessage
                {
                    Role = "user",
                    Content = BuildToolObservationPrompt(toolDispatch)
                });
                continue;
            }

            var repairPrompt = !string.IsNullOrWhiteSpace(context.DashboardApplicationId)
                && (step.Label == AgentLabels.Think || step.Label == AgentLabels.Tool)
                ? "Dashboard implementation cannot stop at THINK or an empty TOOL. Use TOOL now: apply_dashboard_patch to a previously read file, validate_dashboard_change after the patch, or FINISH only if no change is needed."
                : BuildLoopRepairPrompt(step.Label, dispatch);
            messages.Add(new LlmMessage
            {
                Role = "user",
                Content = repairPrompt
            });
        }

        if (step is null || step.Label != AgentLabels.Finish)
        {
            context.Stats.Iteration++;
            messages.Add(new LlmMessage
            {
                Role = "user",
                Content = "Tool budget is exhausted. First line must be FINISH. Produce the best possible Markdown answer from the observations already available."
            });
            step = await _labeledStepRunner.RunStreamingAsync(context, messages, onEvent, cancellationToken);
            if (step.Label != AgentLabels.Finish)
            {
                step = new LabeledStepResult
                {
                    Label = AgentLabels.Finish,
                    Text = BuildFallbackAnswer(context, dispatch),
                    ModelId = step.ModelId,
                    Model = step.Model
                };
            }
        }

        step ??= new LabeledStepResult
        {
            Label = AgentLabels.Finish,
            Text = string.Empty
        };

        await EmitAsync(onEvent, new AgentStreamEvent
        {
            Type = "sources",
            KnowledgeBaseName = context.KnowledgeBaseName,
            Citations = dispatch.Citations,
            Metadata = context.Stats.ToMetadata()
        }, cancellationToken);

        context.Stats.CompletedAt = DateTime.UtcNow;
        await EmitAsync(onEvent, new AgentStreamEvent
        {
            Type = "done",
            Label = step.Label,
            Content = step.Text,
            ModelId = step.ModelId,
            Model = step.Model,
            KnowledgeBaseName = context.KnowledgeBaseName,
            Citations = dispatch.Citations,
            Metadata = BuildDoneMetadata(context, step, plan)
        }, cancellationToken);

        return new AgentLoopOutcome
        {
            Query = context.UserMessage,
            Answer = step.Text,
            ModelId = step.ModelId,
            Model = step.Model,
            KnowledgeBaseName = context.KnowledgeBaseName,
            Citations = dispatch.Citations,
            Plan = plan,
            ToolDispatch = dispatch,
            Completed = step.Label == AgentLabels.Finish
        };
    }

    private async Task<ToolDispatchOutcome> DispatchToolCallsAsync(
        AgentContext context,
        IReadOnlyList<ToolCall> toolCalls,
        AgentStreamEventHandler? onEvent,
        CancellationToken cancellationToken)
    {
        if (toolCalls.Count == 0)
        {
            return new ToolDispatchOutcome();
        }

        context.Stats.ToolCalls += toolCalls.Count;
        await EmitAsync(onEvent, new AgentStreamEvent
        {
            Type = "label",
            Label = AgentLabels.Tool,
            Content = AgentLabels.Tool,
            Metadata = context.Stats.ToMetadata()
        }, cancellationToken);

        await EmitAsync(onEvent, new AgentStreamEvent
        {
            Type = "tool",
            Label = AgentLabels.Tool,
            Content = $"Executing {toolCalls.Count} knowledge tool(s).",
            Metadata = BuildToolMetadata(context, toolCalls)
        }, cancellationToken);

        var dispatch = await _toolDispatcher.DispatchAsync(context, toolCalls, cancellationToken);
        await EmitAsync(onEvent, new AgentStreamEvent
        {
            Type = "tool_result",
            Label = AgentLabels.Tool,
            Content = dispatch.BuildToolContext(),
            Citations = dispatch.Citations,
            Metadata = BuildToolResultMetadata(context, dispatch)
        }, cancellationToken);

        return dispatch;
    }

    private static KnowledgeQueryPlan CreateInitialPlan(AgentContext context)
    {
        return new KnowledgeQueryPlan
        {
            Intent = KnowledgeQueryIntent.SemanticQuestion,
            NormalizedQuestion = context.UserMessage,
            SearchQuery = context.UserMessage,
            TopK = context.TopK
        };
    }

    private static string BuildToolObservationPrompt(ToolDispatchOutcome dispatch)
    {
        var toolContext = dispatch.BuildToolContext();
        return string.IsNullOrWhiteSpace(toolContext)
            ? "Tool observation: no readable result. Continue with FINISH and explain the evidence gap."
            : $"Tool observation:\n{toolContext}\n\nNow continue the label protocol. Use FINISH when you can answer, or TOOL with JSON if another tool call is truly needed.";
    }

    private static Dictionary<string, object?> BuildDoneMetadata(AgentContext context, LabeledStepResult step, KnowledgeQueryPlan plan)
    {
        var metadata = context.Stats.ToMetadata();
        metadata["completed"] = step.Label == AgentLabels.Finish;
        metadata["intent"] = plan.Intent.ToString();
        return metadata;
    }

    private static Dictionary<string, object?> BuildToolMetadata(AgentContext context, IReadOnlyList<ToolCall> toolCalls)
    {
        var metadata = context.Stats.ToMetadata();
        metadata["tools"] = toolCalls.Select(x => x.Name).ToArray();
        return metadata;
    }

    private static Dictionary<string, object?> BuildToolResultMetadata(AgentContext context, ToolDispatchOutcome dispatch)
    {
        var metadata = context.Stats.ToMetadata();
        metadata["has_failure"] = dispatch.HasFailure;
        metadata["citation_count"] = dispatch.Citations.Count;
        return metadata;
    }

    private static string BuildLoopRepairPrompt(string label, ToolDispatchOutcome dispatch)
    {
        if (label == AgentLabels.Tool)
        {
            return "Protocol correction: you chose TOOL but emitted no valid executable JSON tool calls, or no knowledge base is selected. Continue now with exactly one label. Use TOOL with a valid JSON array if evidence is still needed; otherwise use FINISH. Do not put JSON inside FINISH.";
        }

        if (label == AgentLabels.Think)
        {
            return "Your previous round was internal reasoning only. Continue now with exactly one label. Use TOOL with JSON if knowledge-base evidence is needed; otherwise use FINISH with the final user-facing answer. Do not explain the protocol.";
        }

        return dispatch.Citations.Count == 0
            ? "No more evidence is available. First line must be FINISH. Explain the evidence gap clearly."
            : "First line must be FINISH. Use the available citations to answer.";
    }

    private static string BuildFallbackAnswer(AgentContext context, ToolDispatchOutcome dispatch)
    {
        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId))
        {
            var changed = dispatch.Results.Any(result => result.Content.StartsWith("dashboard_change_applied:", StringComparison.Ordinal));
            return changed
                ? "已写入看板文件，但模型未能生成总结。请在右侧工具记录中查看 dashboard_change_applied 与 dashboard_change_validated。"
                : "AI 已读取当前看板工作区，但未能在限定轮次内生成有效补丁；没有写入文件。请重新发送需求，系统会保留工作区定位和版本保护。";
        }
        if (dispatch.Results.Count == 0)
        {
            return "I could not obtain usable knowledge-base evidence for this question.";
        }

        var context = dispatch.BuildToolContext();
        return string.IsNullOrWhiteSpace(context)
            ? "I could not obtain readable knowledge-base evidence for this question."
            : $"The model did not produce a final response after tool use. Here is the available evidence for review:\n\n{context}";
    }

    private static Task EmitAsync(AgentStreamEventHandler? onEvent, AgentStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        return onEvent is null ? Task.CompletedTask : onEvent(streamEvent, cancellationToken);
    }
}

/// <summary>
/// Agent loop result.
/// </summary>
public sealed class AgentLoopOutcome
{
    /// <summary>
    /// User query.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Final answer.
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Model configuration id.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Provider model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Knowledge base name.
    /// </summary>
    public string? KnowledgeBaseName { get; set; }

    /// <summary>
    /// Citations used by the turn.
    /// </summary>
    public List<KnowledgeCitationDto> Citations { get; set; } = [];

    /// <summary>
    /// Planning placeholder retained for response compatibility.
    /// </summary>
    public KnowledgeQueryPlan? Plan { get; set; }

    /// <summary>
    /// Aggregated tool dispatch result.
    /// </summary>
    public ToolDispatchOutcome? ToolDispatch { get; set; }

    /// <summary>
    /// Whether the loop reached FINISH.
    /// </summary>
    public bool Completed { get; set; }
}
