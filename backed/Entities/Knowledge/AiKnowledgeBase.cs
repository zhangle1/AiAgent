using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_base")]
public sealed class AiKnowledgeBase
{
    /// <summary>
    /// 知识库自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 知识库唯一名称，用于路由、目录名和业务查找。
    /// </summary>
    [SugarColumn(Length = 128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 前端展示名称。
    /// </summary>
    [SugarColumn(Length = 256)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 知识库说明。
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? Description { get; set; }

    /// <summary>
    /// 检索引擎类型，例如 llamaindex、pageindex、graphrag。
    /// </summary>
    [SugarColumn(Length = 64)]
    public string EngineType { get; set; } = "local_vector";

    /// <summary>
    /// 生命周期状态，例如 draft、initializing、processing、ready、error。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string Status { get; set; } = "draft";

    /// <summary>
    /// 是否为默认知识库。
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 当前未删除文档数量。
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// 当前激活的索引版本 Id。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? ActiveVersionId { get; set; }

    /// <summary>
    /// 扩展元数据 JSON，用于预留 connected source、引擎参数等信息。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? MetadataJson { get; set; }

    /// <summary>
    /// 软删除标记。
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 创建时间，使用 UTC。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最近更新时间，使用 UTC。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 删除时间，使用 UTC。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? DeletedAt { get; set; }
}