using AiAgent.Backend.Models.Settings;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Settings;

/// <summary>
/// 设置中心首页返回数据。
/// </summary>
public sealed class SettingsResponse
{
    /// <summary>
    /// UI 偏好设置。
    /// </summary>
    [JsonPropertyName("ui")]
    public UiSettings Ui { get; set; } = new();

    /// <summary>
    /// 当前模型服务目录。
    /// </summary>
    [JsonPropertyName("catalog")]
    public ModelCatalog Catalog { get; set; } = new();

    /// <summary>
    /// 各服务可选 provider 列表。
    /// </summary>
    [JsonPropertyName("providers")]
    public Dictionary<string, List<ProviderOption>> Providers { get; set; } = new();
}

/// <summary>
/// UI 偏好设置。
/// </summary>
public sealed class UiSettings
{
    /// <summary>
    /// 主题名称。
    /// </summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "light";

    /// <summary>
    /// 当前语言。
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-CN";

    [JsonPropertyName("preferred_agent")]
    public string PreferredAgent { get; set; } = "codex";
}

/// <summary>
/// 更新 UI 偏好时的请求体。
/// </summary>
public sealed class UiSettingsPayload
{
    /// <summary>
    /// 主题名称。
    /// </summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    /// <summary>
    /// 语言标识。
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("preferred_agent")]
    public string? PreferredAgent { get; set; }
}

/// <summary>
/// 模型 catalog 保存/应用请求体。
/// </summary>
public sealed class CatalogPayload
{
    /// <summary>
    /// 模型服务目录。
    /// </summary>
    [JsonPropertyName("catalog")]
    public ModelCatalog Catalog { get; set; } = new();
}

/// <summary>
/// Provider 下拉选项。
/// </summary>
public sealed class ProviderOption
{
    /// <summary>
    /// 选项值，通常是 provider code。
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>
    /// 展示名称。
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>
    /// 默认基础地址。
    /// </summary>
    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// 默认模型名称。
    /// </summary>
    [JsonPropertyName("default_model")]
    public string DefaultModel { get; set; } = "";

    /// <summary>
    /// 默认向量维度。
    /// </summary>
    [JsonPropertyName("default_dim")]
    public string DefaultDim { get; set; } = "";
}

/// <summary>
/// 应用模型 catalog 后的响应。
/// </summary>
public sealed class ApplyCatalogResponse
{
    /// <summary>
    /// 应用结果消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "Catalog applied to runtime settings.";

    /// <summary>
    /// 脱敏后的最新 catalog。
    /// </summary>
    [JsonPropertyName("catalog")]
    public ModelCatalog Catalog { get; set; } = new();

    /// <summary>
    /// 运行时应用结果。
    /// </summary>
    [JsonPropertyName("runtime")]
    public object Runtime { get; set; } = new();
}

/// <summary>
/// 模型服务连通性测试结果。
/// </summary>
public sealed class ServiceTestResponse
{
    /// <summary>
    /// 测试状态，success 或 failed。
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = "success";

    /// <summary>
    /// 面向用户的结果消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// 测试摘要。
    /// </summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>
    /// 诊断日志。
    /// </summary>
    [JsonPropertyName("logs")]
    public List<string> Logs { get; set; } = [];

    /// <summary>
    /// 被测试的配置档 Id。
    /// </summary>
    [JsonPropertyName("profile_id")]
    public string? ProfileId { get; set; }

    /// <summary>
    /// 被测试的模型 Id。
    /// </summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    /// <summary>
    /// Embedding 测试探测到的向量维度。
    /// </summary>
    [JsonPropertyName("detected_dimension")]
    public int? DetectedDimension { get; set; }

    /// <summary>
    /// 模型支持的维度集合。
    /// </summary>
    [JsonPropertyName("supported_dimensions")]
    public string? SupportedDimensions { get; set; }

    /// <summary>
    /// 测试后更新的 catalog。
    /// </summary>
    [JsonPropertyName("catalog")]
    public ModelCatalog? Catalog { get; set; }

    /// <summary>
    /// 测试时间。
    /// </summary>
    [JsonPropertyName("tested_at")]
    public DateTimeOffset TestedAt { get; set; } = DateTimeOffset.UtcNow;
}
