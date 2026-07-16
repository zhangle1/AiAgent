using SqlSugar;

namespace AiAgent.Backend.Entities.Settings;

[SugarTable("ai_model_provider")]
public sealed class AiModelProvider
{
    /// <summary>
    /// Provider 自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 服务类型，例如 llm、embedding、tts。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// Provider 编码。
    /// </summary>
    [SugarColumn(Length = 64)]
    public string ProviderCode { get; set; } = string.Empty;

    /// <summary>
    /// Provider 展示名称。
    /// </summary>
    [SugarColumn(Length = 128)]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 绑定类型，用于适配不同协议。
    /// </summary>
    [SugarColumn(Length = 64)]
    public string BindingType { get; set; } = string.Empty;

    /// <summary>
    /// 默认基础地址。
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 鉴权类型，例如 bearer。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string AuthType { get; set; } = "bearer";

    /// <summary>
    /// API 版本。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ApiVersion { get; set; }

    /// <summary>
    /// 前端图标 key。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? IconKey { get; set; }

    /// <summary>
    /// 默认模型名称。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? DefaultModel { get; set; }

    /// <summary>
    /// 默认向量维度。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? DefaultDimension { get; set; }

    /// <summary>
    /// 默认音色。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? DefaultVoice { get; set; }

    /// <summary>
    /// 能力描述 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? CapabilitiesJson { get; set; }

    /// <summary>
    /// 排序号。
    /// </summary>
    public int SortOrder { get; set; } = 100;

    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

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