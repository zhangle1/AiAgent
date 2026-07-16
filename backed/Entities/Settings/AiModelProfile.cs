using SqlSugar;

namespace AiAgent.Backend.Entities.Settings;

[SugarTable("ai_model_profile")]
public sealed class AiModelProfile
{
    /// <summary>
    /// 配置档自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 服务类型，例如 llm、embedding。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 配置档名称。
    /// </summary>
    [SugarColumn(Length = 128)]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// 关联 provider 主键。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? ProviderId { get; set; }

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
    /// 绑定类型。
    /// </summary>
    [SugarColumn(Length = 64)]
    public string BindingType { get; set; } = string.Empty;

    /// <summary>
    /// 服务基础地址。
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// API Key 密文或存储值。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? ApiKeyCipher { get; set; }

    /// <summary>
    /// API 版本。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ApiVersion { get; set; }

    /// <summary>
    /// 鉴权类型。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string AuthType { get; set; } = "bearer";

    /// <summary>
    /// 额外请求头 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? ExtraHeadersJson { get; set; }

    /// <summary>
    /// 额外选项 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? ExtraOptionsJson { get; set; }

    /// <summary>
    /// 代理地址。
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// 最大结果数，搜索服务使用。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? MaxResults { get; set; }

    /// <summary>
    /// 是否当前激活配置档。
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