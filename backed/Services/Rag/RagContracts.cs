using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Rag;

/// <summary>
/// RAG pipeline 调用 Embedding 服务时需要的配置快照。
/// </summary>
public sealed class RagEmbeddingOptions
{
    /// <summary>
    /// Embedding provider 标识。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Embedding 模型名称。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// API Key，传给外部 worker 时使用。
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Provider 基础地址。
    /// </summary>
    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 向量维度。
    /// </summary>
    [JsonPropertyName("dimension")]
    public string? Dimension { get; set; }
}

/// <summary>
/// 构建或重建索引的 provider 无关请求。
/// </summary>
public sealed class RagIndexRequest
{
    /// <summary>
    /// 知识库名称。
    /// </summary>
    public string KnowledgeBaseName { get; set; } = string.Empty;

    /// <summary>
    /// 参与索引的文件路径集合。
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; set; } = [];

    /// <summary>
    /// provider 持久化索引的位置。
    /// </summary>
    public string PersistDir { get; set; } = string.Empty;

    /// <summary>
    /// Embedding 配置快照。
    /// </summary>
    public RagEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// 检索与分块配置快照。
    /// </summary>
    public RagRetrievalOptions Retrieval { get; set; } = new();
}

/// <summary>
/// RAG 索引过程中上报的流式进度事件。
/// </summary>
public sealed class RagProgressEvent
{
    /// <summary>
    /// 当前阶段，例如 loading、embedding、persisting。
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// 当前进度百分比。
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// 面向用户展示的阶段说明。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 检索请求。
/// </summary>
public sealed class RagSearchRequest
{
    /// <summary>
    /// 知识库名称。
    /// </summary>
    public string KnowledgeBaseName { get; set; } = string.Empty;

    /// <summary>
    /// 查询文本。
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 返回相似片段数量。
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 已构建索引的持久化目录。
    /// </summary>
    public string PersistDir { get; set; } = string.Empty;

    /// <summary>
    /// Embedding 配置快照。
    /// </summary>
    public RagEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// 检索配置快照。
    /// </summary>
    public RagRetrievalOptions Retrieval { get; set; } = new();
}

/// <summary>
/// RAG 检索召回与文档切片配置。
/// </summary>
public sealed class RagRetrievalOptions
{
    /// <summary>
    /// 检索模式，hybrid 表示关键词与向量融合，vector 表示仅向量。
    /// </summary>
    [JsonPropertyName("retrieval_profile")]
    public string RetrievalProfile { get; set; } = "hybrid";

    /// <summary>
    /// 最终返回片段数量。
    /// </summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 向量召回候选倍数。
    /// </summary>
    [JsonPropertyName("vector_candidate_multiplier")]
    public int VectorCandidateMultiplier { get; set; } = 2;

    /// <summary>
    /// 关键词召回候选倍数。
    /// </summary>
    [JsonPropertyName("keyword_candidate_multiplier")]
    public int KeywordCandidateMultiplier { get; set; } = 2;

    /// <summary>
    /// 文档切片大小。
    /// </summary>
    [JsonPropertyName("chunk_size")]
    public int ChunkSize { get; set; } = 512;

    /// <summary>
    /// 文档切片重叠。
    /// </summary>
    [JsonPropertyName("chunk_overlap")]
    public int ChunkOverlap { get; set; } = 50;
}

/// <summary>
/// 索引类操作返回结果。
/// </summary>
public sealed class RagOperationResult
{
    /// <summary>
    /// 操作是否成功。
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    /// 实际处理的 provider。
    /// </summary>
    public string Provider { get; set; } = "llamaindex";

    /// <summary>
    /// 操作名称。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 处理的文档数量。
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// 生成的切片数量。
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 错误编码。
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 环境检测或索引操作返回的额外信息。
    /// </summary>
    public Dictionary<string, object?> Details { get; set; } = [];
}

/// <summary>
/// 检索结果。
/// </summary>
public sealed class RagSearchResult
{
    /// <summary>
    /// 检索是否成功。
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    /// 实际处理的 provider。
    /// </summary>
    public string Provider { get; set; } = "llamaindex";

    /// <summary>
    /// 原始查询。
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 回答文本。
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// 兼容字段，通常与 Answer 一致。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 引用片段集合。
    /// </summary>
    public List<RagCitation> Citations { get; set; } = [];

    /// <summary>
    /// 错误编码。
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// RAG 检索命中的引用片段。
/// </summary>
public sealed class RagCitation
{
    /// <summary>
    /// 相似度得分。
    /// </summary>
    public double? Score { get; set; }

    /// <summary>
    /// 引用片段文本。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 引用片段元数据。
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = [];
}