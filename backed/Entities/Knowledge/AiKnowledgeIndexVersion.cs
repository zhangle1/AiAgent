using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_index_version")]
public sealed class AiKnowledgeIndexVersion
{
    /// <summary>
    /// 索引版本自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 所属知识库 Id。
    /// </summary>
    public long KnowledgeBaseId { get; set; }

    /// <summary>
    /// 知识库内的版本号，从 1 开始递增。
    /// </summary>
    public int VersionNo { get; set; }

    /// <summary>
    /// 索引版本状态，例如 building、ready、error、archived。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string Status { get; set; } = "building";

    /// <summary>
    /// 构建该版本使用的检索引擎。
    /// </summary>
    [SugarColumn(Length = 64)]
    public string EngineType { get; set; } = "local_vector";

    /// <summary>
    /// 构建该版本使用的 Embedding 配置档 Id。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? EmbeddingProfileId { get; set; }

    /// <summary>
    /// 构建该版本使用的 Embedding 模型 Id。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? EmbeddingModelId { get; set; }

    /// <summary>
    /// 构建该版本使用的 Embedding 模型名称。
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// Embedding 向量维度。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? EmbeddingDimension { get; set; }

    /// <summary>
    /// Embedding 配置签名，用于判断索引是否需要重建。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? EmbeddingSignature { get; set; }

    /// <summary>
    /// 切片配置 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? ChunkConfigJson { get; set; }

    /// <summary>
    /// 索引文件或向量库的存储路径。
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? StoragePath { get; set; }

    /// <summary>
    /// 参与构建索引的文档数量。
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// 构建出的切片数量。
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 索引构建失败时的错误信息。
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 版本创建时间，使用 UTC。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 版本被激活的时间。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// 版本被归档的时间。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ArchivedAt { get; set; }
}