using AiAgent.Backend.Dtos.Settings;
using AiAgent.Backend.Entities.Settings;
using SqlSugar;

namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型 provider 选项服务实现。
/// </summary>
public sealed class ModelProviderOptionsService : IModelProviderOptionsService
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<ModelProviderOptionsService> _logger;

    /// <summary>
    /// 初始化供应商选项服务，用于从数据库加载前端可选择的模型供应商。
    /// </summary>
    public ModelProviderOptionsService(ISqlSugarClient db, ILogger<ModelProviderOptionsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 从 provider 表读取各服务类型的可选 provider。
    /// </summary>
    public Dictionary<string, List<ProviderOption>> GetProviderChoices()
    {
        try
        {
            var rows = _db.Queryable<AiModelProvider>()
                .Where(x => x.IsEnabled && !x.IsDeleted)
                .OrderBy(x => x.ServiceType)
                .OrderBy(x => x.SortOrder)
                .ToList();

            if (rows.Count > 0)
            {
                return rows
                    .GroupBy(x => x.ServiceType)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Select(ToProviderOption).ToList());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load model provider options from database; using in-memory seed data.");
        }

        return GetFallbackProviderChoices();
    }

    private static ProviderOption ToProviderOption(AiModelProvider provider)
    {
        return new ProviderOption
        {
            Value = provider.ProviderCode,
            Label = provider.ProviderName,
            BaseUrl = provider.BaseUrl ?? "",
            DefaultModel = provider.DefaultModel ?? "",
            DefaultDim = provider.DefaultDimension?.ToString() ?? ""
        };
    }

    private static Dictionary<string, List<ProviderOption>> GetFallbackProviderChoices()
    {
        var rows = ModelProviderSeedData.Providers;
        return rows
            .GroupBy(x => x.ServiceType)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(p => p.SortOrder).Select(ToProviderOption).ToList());
    }
}