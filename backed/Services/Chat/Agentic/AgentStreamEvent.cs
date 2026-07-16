using AiAgent.Backend.Dtos.Knowledge;

namespace AiAgent.Backend.Services.Chat.Agentic;

/// <summary>
/// Agent 流式事件，后端通过 SSE 推给前端。
/// </summary>
public sealed class AgentStreamEvent
{
    /// <summary>
    /// 事件类型，例如 label、thinking、content、tool、sources、done、error。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 当前 label，例如 THINK、TOOL、FINISH。
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// 增量文本或事件说明。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 模型配置 Id。
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Provider 模型名称。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 知识库名称。
    /// </summary>
    public string? KnowledgeBaseName { get; set; }

    /// <summary>
    /// 引用片段。
    /// </summary>
    public List<KnowledgeCitationDto>? Citations { get; set; }

    /// <summary>
    /// 扩展元数据。
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = [];
}

/// <summary>
/// Agent 流式事件接收器。
/// </summary>
public delegate Task AgentStreamEventHandler(AgentStreamEvent streamEvent, CancellationToken cancellationToken);