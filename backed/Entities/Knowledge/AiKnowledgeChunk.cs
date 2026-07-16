using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_chunk")]
public sealed class AiKnowledgeChunk
{
    /// <summary>
    /// 切片自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 所属知识库 Id。
    /// </summary>
    public long KnowledgeBaseId { get; set; }

    /// <summary>
    /// 所属文档 Id。
    /// </summary>
    public long DocumentId { get; set; }

    /// <summary>
    /// 所属索引版本 Id。
    /// </summary>
    public long IndexVersionId { get; set; }

    /// <summary>
    /// 文档内的切片序号。
    /// </summary>
    public int ChunkNo { get; set; }

    /// <summary>
    /// 切片标题或章节名。
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? Title { get; set; }

    /// <summary>
    /// 切片正文内容。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 切片估算 token 数。
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// 来源页码，适用于 PDF 等分页文档。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? PageNo { get; set; }

    /// <summary>
    /// 切片扩展元数据 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? MetadataJson { get; set; }

    /// <summary>
    /// 向量 JSON，第一版可由外部向量索引管理，此字段用于预留。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? EmbeddingVectorJson { get; set; }

    /// <summary>
    /// 创建时间，使用 UTC。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}