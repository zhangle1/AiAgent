using AiAgent.Backend.Dtos.Knowledge;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Chat;

/// <summary>
/// 聊天请求，承载用户消息、知识库选择和模型选择。
/// </summary>
public sealed class ChatCompleteRequest
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    /// <summary>
    /// 用户本轮输入。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 可选知识库名称；传入后会先检索知识库，再交给 LLM 生成回答。
    /// </summary>
    [JsonPropertyName("knowledge_base_name")]
    public string? KnowledgeBaseName { get; set; }

    /// <summary>
    /// All knowledge bases selected for the current turn. The legacy single-name field remains supported.
    /// </summary>
    [JsonPropertyName("knowledge_base_names")]
    public List<string> KnowledgeBaseNames { get; set; } = [];

    /// <summary>
    /// Code repositories selected as the current chat context. Source inspection tools consume this field in later phases.
    /// </summary>
    [JsonPropertyName("code_repository_names")]
    public List<string> CodeRepositoryNames { get; set; } = [];

    /// <summary>
    /// Selected code project. Its registered repositories become this chat's code context.
    /// </summary>
    [JsonPropertyName("code_project_id")]
    public long? CodeProjectId { get; set; }

    /// <summary>
    /// Optional constrained dashboard workspace for agent file-write tools.
    /// </summary>
    [JsonPropertyName("dashboard_application_id")]
    public string? DashboardApplicationId { get; set; }

    [JsonPropertyName("dashboard_file_path")]
    public string? DashboardFilePath { get; set; }

    [JsonPropertyName("dashboard_workspace_revision")]
    public string? DashboardWorkspaceRevision { get; set; }

    /// <summary>
    /// 可选 LLM 模型配置 Id；为空时使用设置中的激活模型。
    /// </summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    /// <summary>
    /// 检索返回的引用片段数量。
    /// </summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 前端选择的聊天模式，当前先用于记录和后续扩展。
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "chat";

    /// <summary>
    /// Optional external agent that takes over this turn. The first supported value is codex.
    /// </summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    /// <summary>
    /// Uploaded image attachment identifiers. They are resolved to server-owned local paths only for a Codex turn.
    /// </summary>
    [JsonPropertyName("attachment_ids")]
    public List<string> AttachmentIds { get; set; } = [];

    /// <summary>
    /// Server-only resolved image paths. This field must never be accepted from or returned to the browser.
    /// </summary>
    [JsonIgnore]
    public List<string> LocalImagePaths { get; set; } = [];
}

public sealed class ChatImageAttachmentDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }
}

public sealed class ChatTokenUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("is_estimated")]
    public bool IsEstimated { get; set; }
}

/// <summary>
/// 聊天响应，包含 LLM 生成结果和知识库引用。
/// </summary>
public sealed class ChatCompleteResponse
{
    /// <summary>
    /// 用户原始问题。
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// LLM 面向用户生成的最终回答。
    /// </summary>
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// 兼容字段，通常与 Answer 一致。
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 使用的模型配置 Id。
    /// </summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    /// <summary>
    /// 使用的模型名称。
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// 使用的知识库名称。
    /// </summary>
    [JsonPropertyName("knowledge_base_name")]
    public string? KnowledgeBaseName { get; set; }

    /// <summary>
    /// 参与生成的引用片段。
    /// </summary>
    [JsonPropertyName("citations")]
    public List<KnowledgeCitationDto> Citations { get; set; } = [];

    [JsonPropertyName("usage")]
    public ChatTokenUsage? Usage { get; set; }
}
