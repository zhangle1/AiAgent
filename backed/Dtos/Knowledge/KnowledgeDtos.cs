using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

/// <summary>
/// 前端展示的 RAG 引擎信息。
/// </summary>
public sealed class KnowledgeProviderDto
{
    /// <summary>
    /// 引擎标识，例如 llamaindex、pageindex。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 引擎展示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 引擎说明。
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 当前环境是否已经配置好该引擎。
    /// </summary>
    [JsonPropertyName("configured")]
    public bool Configured { get; set; }

    /// <summary>
    /// 引擎状态，例如 ready、needs_setup、planned。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "planned";

    /// <summary>
    /// 支持的检索模式。
    /// </summary>
    [JsonPropertyName("modes")]
    public List<string> Modes { get; set; } = [];

    /// <summary>
    /// 默认检索模式。
    /// </summary>
    [JsonPropertyName("default_mode")]
    public string DefaultMode { get; set; } = "semantic";
}

/// <summary>
/// 知识库列表项 DTO。
/// </summary>
public class KnowledgeBaseDto
{
    /// <summary>
    /// 知识库主键。
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// 知识库唯一名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 前端展示名称。
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 知识库说明。
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 绑定的 RAG 引擎类型。
    /// </summary>
    [JsonPropertyName("engine_type")]
    public string EngineType { get; set; } = "llamaindex";

    /// <summary>
    /// 知识库状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    /// <summary>
    /// 是否默认知识库。
    /// </summary>
    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// 文档数量。
    /// </summary>
    [JsonPropertyName("document_count")]
    public int DocumentCount { get; set; }

    /// <summary>
    /// 当前激活索引版本 Id。
    /// </summary>
    [JsonPropertyName("active_version_id")]
    public long? ActiveVersionId { get; set; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最近更新时间。
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 最近一次后台任务。
    /// </summary>
    [JsonPropertyName("latest_job")]
    public KnowledgeJobDto? LatestJob { get; set; }
}

/// <summary>
/// 知识库文档 DTO。
/// </summary>
public sealed class KnowledgeDocumentDto
{
    /// <summary>
    /// 文档主键。
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// 内部保存文件名。
    /// </summary>
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 原始上传文件名。
    /// </summary>
    [JsonPropertyName("original_file_name")]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小，单位字节。
    /// </summary>
    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    /// <summary>
    /// 文件 Content-Type。
    /// </summary>
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    /// <summary>
    /// 文件扩展名。
    /// </summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    /// <summary>
    /// 文件哈希。
    /// </summary>
    [JsonPropertyName("file_hash")]
    public string? FileHash { get; set; }

    /// <summary>
    /// 文档状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "uploaded";

    /// <summary>
    /// 上传创建时间。
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 知识库索引版本 DTO。
/// </summary>
public sealed class KnowledgeIndexVersionDto
{
    /// <summary>
    /// 索引版本主键。
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// 所属知识库 Id。
    /// </summary>
    [JsonPropertyName("knowledge_base_id")]
    public long KnowledgeBaseId { get; set; }

    /// <summary>
    /// 知识库内版本号。
    /// </summary>
    [JsonPropertyName("version_no")]
    public int VersionNo { get; set; }

    /// <summary>
    /// 版本状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "building";

    /// <summary>
    /// 检索引擎类型。
    /// </summary>
    [JsonPropertyName("engine_type")]
    public string EngineType { get; set; } = "llamaindex";

    /// <summary>
    /// 索引存储路径。
    /// </summary>
    [JsonPropertyName("storage_path")]
    public string? StoragePath { get; set; }

    /// <summary>
    /// 文档数量。
    /// </summary>
    [JsonPropertyName("document_count")]
    public int DocumentCount { get; set; }

    /// <summary>
    /// 分块数量。
    /// </summary>
    [JsonPropertyName("chunk_count")]
    public int ChunkCount { get; set; }

    /// <summary>
    /// 是否当前激活版本。
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 激活时间。
    /// </summary>
    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }
}

/// <summary>
/// 知识库任务 DTO。
/// </summary>
public sealed class KnowledgeJobDto
{
    /// <summary>
    /// 任务主键。
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// 所属知识库 Id。
    /// </summary>
    [JsonPropertyName("knowledge_base_id")]
    public long KnowledgeBaseId { get; set; }

    /// <summary>
    /// 关联索引版本 Id。
    /// </summary>
    [JsonPropertyName("index_version_id")]
    public long? IndexVersionId { get; set; }

    /// <summary>
    /// 任务类型。
    /// </summary>
    [JsonPropertyName("job_type")]
    public string JobType { get; set; } = "index";

    /// <summary>
    /// 任务状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "queued";

    /// <summary>
    /// 任务进度百分比。
    /// </summary>
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    /// <summary>
    /// 进度消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// 错误消息。
    /// </summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 开始时间。
    /// </summary>
    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 结束时间。
    /// </summary>
    [JsonPropertyName("finished_at")]
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// 知识库详情 DTO，包含基础信息和文档列表。
/// </summary>
public sealed class KnowledgeDetailDto : KnowledgeBaseDto
{
    /// <summary>
    /// 知识库下的文档列表。
    /// </summary>
    [JsonPropertyName("documents")]
    public List<KnowledgeDocumentDto> Documents { get; set; } = [];
}

/// <summary>
/// 新建知识库请求。
/// </summary>
public sealed class KnowledgeCreateRequest
{
    /// <summary>
    /// 知识库名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 展示名称。
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 知识库说明。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// RAG provider，默认 llamaindex。
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 创建时一并上传的文件。
    /// </summary>
    public List<IFormFile> Files { get; set; } = [];
}

/// <summary>
/// RAG provider 检索与分块配置。
/// </summary>
public sealed class KnowledgeProviderConfigDto
{
    /// <summary>
    /// Provider 标识，例如 llamaindex。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "llamaindex";

    /// <summary>
    /// 检索配置，hybrid 表示混合检索，vector 表示仅向量检索。
    /// </summary>
    [JsonPropertyName("retrieval_profile")]
    public string RetrievalProfile { get; set; } = "hybrid";

    /// <summary>
    /// 每次查询最终返回给上层的片段数量。
    /// </summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 向量检索阶段召回的候选倍数。
    /// </summary>
    [JsonPropertyName("vector_candidate_multiplier")]
    public int VectorCandidateMultiplier { get; set; } = 2;

    /// <summary>
    /// 关键词检索阶段召回的候选倍数。
    /// </summary>
    [JsonPropertyName("keyword_candidate_multiplier")]
    public int KeywordCandidateMultiplier { get; set; } = 2;

    /// <summary>
    /// 文档切片大小，通常按 token 近似计算。
    /// </summary>
    [JsonPropertyName("chunk_size")]
    public int ChunkSize { get; set; } = 512;

    /// <summary>
    /// 相邻切片之间保留的重叠长度。
    /// </summary>
    [JsonPropertyName("chunk_overlap")]
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>
    /// 配置最近保存时间。
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 上传文档请求。
/// </summary>
public sealed class KnowledgeUploadRequest
{
    /// <summary>
    /// 上传文件集合。
    /// </summary>
    public List<IFormFile> Files { get; set; } = [];
}

/// <summary>
/// 知识库检索请求。
/// </summary>
public sealed class KnowledgeSearchRequest
{
    /// <summary>
    /// 用户查询文本。
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 返回的相似片段数量。
    /// </summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 5;
}

/// <summary>
/// 知识库变更操作响应。
/// </summary>
public sealed class KnowledgeMutationResponse
{
    /// <summary>
    /// 变更后的知识库信息。
    /// </summary>
    [JsonPropertyName("knowledge_base")]
    public KnowledgeBaseDto KnowledgeBase { get; set; } = new();

    /// <summary>
    /// 后台任务 Id，没有触发任务时为空。
    /// </summary>
    [JsonPropertyName("task_id")]
    public long? TaskId { get; set; }

    /// <summary>
    /// 操作提示消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 知识库检索响应。
/// </summary>
public sealed class KnowledgeSearchResponse
{
    /// <summary>
    /// 原始查询。
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 实际使用的 RAG provider。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "llamaindex";

    /// <summary>
    /// 检索/生成的回答文本。
    /// </summary>
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// 兼容字段，通常与 Answer 一致。
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 命中的引用片段。
    /// </summary>
    [JsonPropertyName("citations")]
    public List<KnowledgeCitationDto> Citations { get; set; } = [];
}

/// <summary>
/// 检索引用片段。
/// </summary>
public sealed class KnowledgeCitationDto
{
    /// <summary>
    /// 相似度得分。
    /// </summary>
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    /// <summary>
    /// 引用片段文本。
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 引用片段元数据。
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = [];
}