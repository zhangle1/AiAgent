using AiAgent.Backend.Dtos.Settings;

namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型 provider 下拉选项服务。
/// </summary>
public interface IModelProviderOptionsService
{
    /// <summary>
    /// 获取各服务类型可选择的 provider 列表。
    /// </summary>
    Dictionary<string, List<ProviderOption>> GetProviderChoices();
}