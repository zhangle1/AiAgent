using SqlSugar;

namespace AiAgent.Backend.Entities.Settings;

[SugarTable("ai_model")]
public sealed class AiModel
{
    /// <summary>
    /// 模型配置自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 所属配置档 Id。
    /// </summary>
    public long ProfileId { get; set; }

    /// <summary>
    /// 服务类型，例如 llm、embedding、tts。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 前端展示模型名称。
    /// </summary>
    [SugarColumn(Length = 128)]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Provider 侧模型 Id 或模型名称。
    /// </summary>
    [SugarColumn(Length = 256)]
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// 上下文窗口大小。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? ContextWindow { get; set; }

    /// <summary>
    /// 向量维度。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? Dimension { get; set; }

    /// <summary>
    /// 调用 embedding API 时是否发送 dimensions 参数。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public bool? SendDimensions { get; set; }

    /// <summary>
    /// 支持的向量维度列表。
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? SupportedDimensions { get; set; }

    /// <summary>
    /// TTS 音色。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Voice { get; set; }

    /// <summary>
    /// 响应格式。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// 语言选项。
    /// </summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Language { get; set; }

    /// <summary>
    /// 图像尺寸。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Size { get; set; }

    /// <summary>
    /// 生成质量。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Quality { get; set; }

    /// <summary>
    /// 生成风格。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Style { get; set; }

    /// <summary>
    /// 宽高比。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? AspectRatio { get; set; }

    /// <summary>
    /// 视频或音频时长，单位秒。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// 分辨率。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? Resolution { get; set; }

    /// <summary>
    /// 额外模型参数 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? ExtraOptionsJson { get; set; }

    /// <summary>
    /// 是否当前激活模型。
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 排序号。
    /// </summary>
    public int SortOrder { get; set; } = 100;

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
    /// 备注。
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? Remark { get; set; }
}