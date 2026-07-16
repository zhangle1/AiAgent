using AiAgent.Backend.Dtos.Knowledge;

namespace AiAgent.Backend.Services.Chat.Agentic;

/// <summary>
/// 工具标准协议，对齐 DeepTutor 的 tool_protocol.py，定义工具描述、调用请求和统一返回值。
/// </summary>
public interface IAgentTool
{
    /// <summary>
    /// 工具名称，必须在一次 Agent Loop 内唯一。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 给模型或调度器看的工具说明。
    /// </summary>
    ToolDefinition GetDefinition();

    /// <summary>
    /// 执行工具调用。
    /// </summary>
    Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken);
}

/// <summary>
/// 工具定义，后续可直接转换为 OpenAI function-calling schema。
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>
    /// 工具名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具说明。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 工具参数列表。
    /// </summary>
    public List<ToolParameter> Parameters { get; set; } = [];
}

/// <summary>
/// 工具参数定义。
/// </summary>
public sealed class ToolParameter
{
    /// <summary>
    /// 参数名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数类型，例如 string、integer、boolean。
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// 参数说明。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// 工具调用请求，由 Planner 或 LLM 生成，交给 ToolDispatcher 执行。
/// </summary>
public sealed class ToolCall
{
    /// <summary>
    /// 调用 Id，用于追踪一次工具执行。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 工具名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具参数。
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = [];
}

/// <summary>
/// 工具统一返回值，对齐 DeepTutor 的 ToolResult。
/// </summary>
public sealed class ToolResult
{
    /// <summary>
    /// 工具是否成功。
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 工具返回给 LLM 阅读的文本。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 工具产生的引用片段。
    /// </summary>
    public List<KnowledgeCitationDto> Citations { get; set; } = [];

    /// <summary>
    /// 工具执行元数据。
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = [];

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static ToolResult Failed(string message)
    {
        return new ToolResult
        {
            Success = false,
            Content = message
        };
    }
}

/// <summary>
/// 一轮工具调度的聚合结果。
/// </summary>
public sealed class ToolDispatchOutcome
{
    /// <summary>
    /// 工具结果集合。
    /// </summary>
    public List<ToolResult> Results { get; set; } = [];

    /// <summary>
    /// 聚合后的引用片段。
    /// </summary>
    public List<KnowledgeCitationDto> Citations { get; set; } = [];

    /// <summary>
    /// 是否存在失败工具。
    /// </summary>
    public bool HasFailure => Results.Any(x => !x.Success);

    /// <summary>
    /// 工具结果拼接成给 LLM 的上下文。
    /// </summary>
    public string BuildToolContext()
    {
        return string.Join("\n\n", Results.Select((result, index) =>
        {
            var status = result.Success ? "success" : "failed";
            return $"[Tool {index + 1}: {status}]\n{result.Content}".Trim();
        }).Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}