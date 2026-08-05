using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Services.Chat.Agentic;

/// <summary>
/// Agent 单轮上下文，对齐 DeepTutor 的 context.py，负责把请求、用户输入、知识库、模型和中间元数据打包后传递给工具与 Loop。
/// </summary>
public sealed class AgentContext
{
    /// <summary>
    /// 当前会话 Id，第一版由后端临时生成，后续可接入真实会话表。
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 用户本轮输入。
    /// </summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>
    /// 前端选择的聊天模式，例如 chat、visualize、write。
    /// </summary>
    public string Mode { get; set; } = "chat";

    /// <summary>
    /// 当前选择的知识库名称。
    /// </summary>
    public string? KnowledgeBaseName { get; set; }

    /// <summary>
    /// All selected knowledge bases. KnowledgeBaseName is retained as the first selected name for compatibility.
    /// </summary>
    public List<string> KnowledgeBaseNames { get; set; } = [];

    /// <summary>
    /// Selected code repositories reserved for code retrieval and diagnostics tools.
    /// </summary>
    public List<string> CodeRepositoryNames { get; set; } = [];

    /// <summary>
    /// A dashboard application id scopes the agent's file-write tool to one server workspace.
    /// </summary>
    public string? DashboardApplicationId { get; set; }

    /// <summary>Currently opened file selected in the dashboard editor.</summary>
    public string? DashboardFilePath { get; set; }

    /// <summary>Workspace revision observed by the dashboard editor before this agent turn.</summary>
    public string? DashboardWorkspaceRevision { get; set; }

    /// <summary>
    /// 当前选择的模型配置 Id。
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// 检索返回片段数量。
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 记忆上下文预留字段，后续 Memory 模块写入。
    /// </summary>
    public string MemoryContext { get; set; } = string.Empty;

    /// <summary>
    /// Server-validated metadata for projects referenced from the current message.
    /// </summary>
    public string ProjectReferenceContext { get; set; } = string.Empty;

    /// <summary>
    /// 附件上下文预留字段，后续支持用户上传附件参与聊天。
    /// </summary>
    public List<AgentAttachment> Attachments { get; set; } = [];

    /// <summary>
    /// Loop、工具、Prompt 之间共享的临时元数据。
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = [];

    /// <summary>
    /// 本轮运行统计，用于前端展示 token、调用次数、耗时和当前轮次。
    /// </summary>
    public AgentRunStats Stats { get; set; } = new();

    /// <summary>
    /// 从 HTTP 请求创建 Agent 上下文。
    /// </summary>
    public static AgentContext FromRequest(ChatCompleteRequest request)
    {
        var knowledgeBaseNames = NormalizeNames(request.KnowledgeBaseNames, request.KnowledgeBaseName);
        var dashboardApplicationId = string.IsNullOrWhiteSpace(request.DashboardApplicationId) ? null : request.DashboardApplicationId.Trim();
        return new AgentContext
        {
            UserMessage = (string.IsNullOrWhiteSpace(request.ServerPromptMessage) ? request.Message : request.ServerPromptMessage).Trim(),
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "chat" : request.Mode.Trim(),
            KnowledgeBaseNames = knowledgeBaseNames,
            KnowledgeBaseName = knowledgeBaseNames.FirstOrDefault(),
            CodeRepositoryNames = dashboardApplicationId is null ? NormalizeNames(request.CodeRepositoryNames, null) : [],
            DashboardApplicationId = dashboardApplicationId,
            DashboardFilePath = string.IsNullOrWhiteSpace(request.DashboardFilePath) ? null : request.DashboardFilePath.Trim(),
            DashboardWorkspaceRevision = string.IsNullOrWhiteSpace(request.DashboardWorkspaceRevision) ? null : request.DashboardWorkspaceRevision.Trim(),
            ModelId = string.IsNullOrWhiteSpace(request.ModelId) ? null : request.ModelId.Trim(),
            TopK = Math.Clamp(request.TopK, 1, 12),
            MemoryContext = request.ServerMemoryContext ?? string.Empty,
            ProjectReferenceContext = request.ServerProjectReferenceContext ?? string.Empty
        };
    }

    private static List<string> NormalizeNames(IEnumerable<string>? values, string? legacyValue)
    {
        return (values ?? [])
            .Append(legacyValue ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Agent 单轮运行统计。
/// </summary>
public sealed class AgentRunStats
{
    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 当前思考/调用轮次。
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// LLM 调用次数。
    /// </summary>
    public int LlmCalls { get; set; }

    /// <summary>
    /// 工具调用次数。
    /// </summary>
    public int ToolCalls { get; set; }

    /// <summary>
    /// 估算输入 token 数。
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// 估算输出 token 数。
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// 总 token 数。
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>
    /// 已运行秒数。
    /// </summary>
    public int ElapsedSeconds => (int)Math.Max(0, ((CompletedAt ?? DateTime.UtcNow) - StartedAt).TotalSeconds);

    /// <summary>
    /// 输出给前端的统计快照。
    /// </summary>
    public Dictionary<string, object?> ToMetadata()
    {
        return new Dictionary<string, object?>
        {
            ["iteration"] = Iteration,
            ["llm_calls"] = LlmCalls,
            ["tool_calls"] = ToolCalls,
            ["prompt_tokens"] = PromptTokens,
            ["completion_tokens"] = CompletionTokens,
            ["total_tokens"] = TotalTokens,
            ["elapsed_seconds"] = ElapsedSeconds,
            ["estimated_cost"] = 0
        };
    }
}

/// <summary>
/// Agent 附件描述，对齐 DeepTutor context.py 的 Attachment，当前先预留结构。
/// </summary>
public sealed class AgentAttachment
{
    /// <summary>
    /// 附件类型，例如 file、image、pdf。
    /// </summary>
    public string Type { get; set; } = "file";

    /// <summary>
    /// 附件文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 附件 MIME 类型。
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 附件内容地址或后端存储路径。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 附件已解析文本，供 LLM 读取。
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;
}
