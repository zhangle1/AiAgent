using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_document")]
public sealed class AiKnowledgeDocument
{
    /// <summary>
    /// 文档自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 所属知识库 Id。
    /// </summary>
    public long KnowledgeBaseId { get; set; }

    /// <summary>
    /// 系统内部保存后的文件名。
    /// </summary>
    [SugarColumn(Length = 512)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 用户上传时的原始文件名。
    /// </summary>
    [SugarColumn(Length = 512)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件 Content-Type。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ContentType { get; set; }

    /// <summary>
    /// 文件扩展名。
    /// </summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Extension { get; set; }

    /// <summary>
    /// 文件大小，单位字节。
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件 SHA256 哈希，用于去重和变更判断。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? FileHash { get; set; }

    /// <summary>
    /// 文件在服务器上的实际存储路径。
    /// </summary>
    [SugarColumn(Length = 1024)]
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// 文档解析器类型，auto 表示自动识别。
    /// </summary>
    [SugarColumn(Length = 64)]
    public string ParserType { get; set; } = "auto";

    /// <summary>
    /// 文档处理状态，例如 uploaded、indexed、error。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string Status { get; set; } = "uploaded";

    /// <summary>
    /// 文档处理失败时的错误信息。
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? ErrorMessage { get; set; }

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