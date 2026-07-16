using System.Text.Json.Serialization;

namespace AiAgent.Backend.Models.Settings;

/// <summary>
/// 模型服务目录，保存所有 AI 服务的配置集合。
/// </summary>
public sealed class ModelCatalog
{
    /// <summary>
    /// catalog 结构版本。
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// 按服务类型划分的配置。
    /// </summary>
    [JsonPropertyName("services")]
    public ModelCatalogServices Services { get; set; } = new();
}

/// <summary>
/// 各类模型服务配置集合。
/// </summary>
public sealed class ModelCatalogServices
{
    /// <summary>
    /// 大语言模型配置。
    /// </summary>
    [JsonPropertyName("llm")]
    public CatalogService Llm { get; set; } = CatalogService.CreateModelService();

    /// <summary>
    /// Embedding 模型配置。
    /// </summary>
    [JsonPropertyName("embedding")]
    public CatalogService Embedding { get; set; } = CatalogService.CreateModelService();

    /// <summary>
    /// 搜索服务配置。
    /// </summary>
    [JsonPropertyName("search")]
    public CatalogService Search { get; set; } = CatalogService.CreateSearchService();

    /// <summary>
    /// 文本转语音配置。
    /// </summary>
    [JsonPropertyName("tts")]
    public CatalogService Tts { get; set; } = CatalogService.CreateModelService();

    /// <summary>
    /// 语音转文本配置。
    /// </summary>
    [JsonPropertyName("stt")]
    public CatalogService Stt { get; set; } = CatalogService.CreateModelService();

    /// <summary>
    /// 图像生成配置。
    /// </summary>
    [JsonPropertyName("imagegen")]
    public CatalogService Imagegen { get; set; } = CatalogService.CreateModelService();

    /// <summary>
    /// 视频生成配置。
    /// </summary>
    [JsonPropertyName("videogen")]
    public CatalogService Videogen { get; set; } = CatalogService.CreateModelService();
}

/// <summary>
/// 单个服务类型下的配置档和激活模型信息。
/// </summary>
public sealed class CatalogService
{
    /// <summary>
    /// 当前激活的配置档 Id。
    /// </summary>
    [JsonPropertyName("active_profile_id")]
    public string? ActiveProfileId { get; set; }

    /// <summary>
    /// 当前激活的模型 Id。
    /// </summary>
    [JsonPropertyName("active_model_id")]
    public string? ActiveModelId { get; set; }

    /// <summary>
    /// 服务配置档列表。
    /// </summary>
    [JsonPropertyName("profiles")]
    public List<CatalogProfile> Profiles { get; set; } = [];

    /// <summary>
    /// 创建带模型列表的服务配置。
    /// </summary>
    public static CatalogService CreateModelService() => new();

    /// <summary>
    /// 创建搜索服务配置，搜索服务不需要 active_model_id。
    /// </summary>
    public static CatalogService CreateSearchService() => new() { ActiveModelId = null };
}

/// <summary>
/// Provider 配置档，保存 endpoint、鉴权和模型列表。
/// </summary>
public sealed class CatalogProfile
{
    /// <summary>
    /// 配置档 Id。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// 配置档名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Profile";

    /// <summary>
    /// provider 绑定类型。
    /// </summary>
    [JsonPropertyName("binding")]
    public string? Binding { get; set; } = "openai";

    /// <summary>
    /// 搜索服务 provider。
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// 服务基础地址。
    /// </summary>
    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// API Key，返回前会被脱敏。
    /// </summary>
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// API 版本。
    /// </summary>
    [JsonPropertyName("api_version")]
    public string ApiVersion { get; set; } = "";

    /// <summary>
    /// 额外请求头。
    /// </summary>
    [JsonPropertyName("extra_headers")]
    public Dictionary<string, string> ExtraHeaders { get; set; } = new();

    /// <summary>
    /// 代理地址。
    /// </summary>
    [JsonPropertyName("proxy")]
    public string? Proxy { get; set; }

    /// <summary>
    /// 搜索结果最大数量。
    /// </summary>
    [JsonPropertyName("max_results")]
    public int? MaxResults { get; set; }

    /// <summary>
    /// 配置档下的模型列表。
    /// </summary>
    [JsonPropertyName("models")]
    public List<CatalogModel> Models { get; set; } = [];
}

/// <summary>
/// 单个模型配置。
/// </summary>
public sealed class CatalogModel
{
    /// <summary>
    /// 模型配置 Id。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// 模型展示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Model";

    /// <summary>
    /// provider 侧模型名称。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>
    /// 向量维度。
    /// </summary>
    [JsonPropertyName("dimension")]
    public string? Dimension { get; set; }

    /// <summary>
    /// 调用 embedding API 时是否发送 dimensions 字段。
    /// </summary>
    [JsonPropertyName("send_dimensions")]
    public bool? SendDimensions { get; set; }

    /// <summary>
    /// 支持的维度集合。
    /// </summary>
    [JsonPropertyName("supported_dimensions")]
    public string? SupportedDimensions { get; set; }

    /// <summary>
    /// 上下文窗口大小。
    /// </summary>
    [JsonPropertyName("context_window")]
    public string? ContextWindow { get; set; }

    /// <summary>
    /// 上下文窗口来源。
    /// </summary>
    [JsonPropertyName("context_window_source")]
    public string? ContextWindowSource { get; set; }

    /// <summary>
    /// 上下文窗口探测时间。
    /// </summary>
    [JsonPropertyName("context_window_detected_at")]
    public string? ContextWindowDetectedAt { get; set; }

    /// <summary>
    /// 语音模型音色。
    /// </summary>
    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    /// <summary>
    /// 响应格式。
    /// </summary>
    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// 语言选项。
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// 图像尺寸。
    /// </summary>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>
    /// 生成质量。
    /// </summary>
    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    /// <summary>
    /// 生成风格。
    /// </summary>
    [JsonPropertyName("style")]
    public string? Style { get; set; }

    /// <summary>
    /// 宽高比。
    /// </summary>
    [JsonPropertyName("aspect_ratio")]
    public string? AspectRatio { get; set; }

    /// <summary>
    /// 生成时长。
    /// </summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    /// <summary>
    /// 分辨率。
    /// </summary>
    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }
}