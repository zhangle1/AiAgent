using AiAgent.Backend.Models.Settings;

namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型服务目录读写与应用服务。
/// </summary>
public interface IModelCatalogService
{
    /// <summary>
    /// 加载模型服务目录。
    /// </summary>
    ModelCatalog Load(bool redactSecrets = false);

    /// <summary>
    /// 保存模型服务目录草稿。
    /// </summary>
    ModelCatalog Save(ModelCatalog catalog);

    /// <summary>
    /// 将模型服务目录应用到运行时配置。
    /// </summary>
    ApplyResult Apply(ModelCatalog? catalog = null);
}

/// <summary>
/// 模型目录应用结果。
/// </summary>
public sealed class ApplyResult
{
    /// <summary>
    /// catalog 持久化路径。
    /// </summary>
    public string CatalogPath { get; set; } = "";

    /// <summary>
    /// 已应用的服务类型。
    /// </summary>
    public List<string> Services { get; set; } = [];

    /// <summary>
    /// 应用时间。
    /// </summary>
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
}