using SqlSugar;

namespace AiAgent.Backend.Entities.Knowledge;

[SugarTable("ai_knowledge_job")]
public sealed class AiKnowledgeJob
{
    /// <summary>
    /// 任务自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 所属知识库 Id。
    /// </summary>
    public long KnowledgeBaseId { get; set; }

    /// <summary>
    /// 关联文档 Id，文档级任务时使用。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? DocumentId { get; set; }

    /// <summary>
    /// 关联索引版本 Id。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? IndexVersionId { get; set; }

    /// <summary>
    /// 任务类型，例如 initialize、upload、reindex。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string JobType { get; set; } = "index";

    /// <summary>
    /// 任务状态，例如 queued、processing、success、error。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string Status { get; set; } = "queued";

    /// <summary>
    /// 任务进度百分比，0 到 100。
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// 当前进度说明。
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? Message { get; set; }

    /// <summary>
    /// 任务失败时的错误信息。
    /// </summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 任务开始时间，使用 UTC。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 任务结束时间，使用 UTC。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// 任务创建时间，使用 UTC。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}