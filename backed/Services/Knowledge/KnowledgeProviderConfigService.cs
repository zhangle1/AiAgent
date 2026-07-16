using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Rag;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>
/// RAG provider 配置读写服务。
/// </summary>
public interface IKnowledgeProviderConfigService
{
    /// <summary>
    /// 读取指定 provider 的检索与分块配置。
    /// </summary>
    KnowledgeProviderConfigDto GetConfig(string provider);

    /// <summary>
    /// 保存指定 provider 的检索与分块配置。
    /// </summary>
    KnowledgeProviderConfigDto SaveConfig(string provider, KnowledgeProviderConfigDto payload);

    /// <summary>
    /// 读取并转换为 RAG pipeline 使用的配置快照。
    /// </summary>
    RagRetrievalOptions GetRetrievalOptions(string provider);
}

/// <summary>
/// 基于设置快照表持久化 RAG provider 配置。
/// </summary>
public sealed class KnowledgeProviderConfigService : IKnowledgeProviderConfigService
{
    private const string SettingPrefix = "knowledge_provider:";
    private readonly ISqlSugarClient _db;

    /// <summary>
    /// 初始化 provider 配置服务。
    /// </summary>
    public KnowledgeProviderConfigService(ISqlSugarClient db)
    {
        _db = db;
    }

    /// <summary>
    /// 读取指定 provider 的检索与分块配置。
    /// </summary>
    public KnowledgeProviderConfigDto GetConfig(string provider)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var row = _db.Queryable<AiSettingSnapshot>()
            .Where(x => x.SettingKey == BuildSettingKey(normalizedProvider))
            .OrderByDescending(x => x.VersionNo)
            .OrderByDescending(x => x.Id)
            .First();

        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson))
        {
            return NormalizeConfig(normalizedProvider, new KnowledgeProviderConfigDto());
        }

        try
        {
            var config = JsonSerializer.Deserialize<KnowledgeProviderConfigDto>(row.PayloadJson) ?? new KnowledgeProviderConfigDto();
            config.UpdatedAt = row.AppliedAt;
            return NormalizeConfig(normalizedProvider, config);
        }
        catch
        {
            return NormalizeConfig(normalizedProvider, new KnowledgeProviderConfigDto());
        }
    }

    /// <summary>
    /// 保存指定 provider 的检索与分块配置。
    /// </summary>
    public KnowledgeProviderConfigDto SaveConfig(string provider, KnowledgeProviderConfigDto payload)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var config = NormalizeConfig(normalizedProvider, payload);
        config.UpdatedAt = DateTime.UtcNow;

        var latestVersion = _db.Queryable<AiSettingSnapshot>()
            .Where(x => x.SettingKey == BuildSettingKey(normalizedProvider))
            .OrderByDescending(x => x.VersionNo)
            .Select(x => x.VersionNo)
            .First();

        var row = new AiSettingSnapshot
        {
            SettingKey = BuildSettingKey(normalizedProvider),
            PayloadJson = JsonSerializer.Serialize(config),
            VersionNo = latestVersion + 1,
            AppliedAt = config.UpdatedAt.Value,
            AppliedBy = "default",
            Remark = $"Knowledge provider {normalizedProvider} config updated"
        };

        _db.Insertable(row).ExecuteCommand();
        return config;
    }

    /// <summary>
    /// 读取并转换为 RAG pipeline 使用的配置快照。
    /// </summary>
    public RagRetrievalOptions GetRetrievalOptions(string provider)
    {
        var config = GetConfig(provider);
        return new RagRetrievalOptions
        {
            RetrievalProfile = config.RetrievalProfile,
            TopK = config.TopK,
            VectorCandidateMultiplier = config.VectorCandidateMultiplier,
            KeywordCandidateMultiplier = config.KeywordCandidateMultiplier,
            ChunkSize = config.ChunkSize,
            ChunkOverlap = config.ChunkOverlap
        };
    }

    private static string BuildSettingKey(string provider)
    {
        return $"{SettingPrefix}{provider}";
    }

    private static string NormalizeProvider(string provider)
    {
        return string.IsNullOrWhiteSpace(provider) ? "llamaindex" : provider.Trim().ToLowerInvariant();
    }

    private static KnowledgeProviderConfigDto NormalizeConfig(string provider, KnowledgeProviderConfigDto config)
    {
        var chunkSize = Math.Max(64, config.ChunkSize);
        var chunkOverlap = Math.Max(0, config.ChunkOverlap);
        if (chunkOverlap >= chunkSize)
        {
            chunkOverlap = Math.Max(0, chunkSize / 5);
        }

        return new KnowledgeProviderConfigDto
        {
            Provider = provider,
            RetrievalProfile = config.RetrievalProfile is "vector" ? "vector" : "hybrid",
            TopK = Math.Max(1, config.TopK),
            VectorCandidateMultiplier = Math.Max(1, config.VectorCandidateMultiplier),
            KeywordCandidateMultiplier = Math.Max(1, config.KeywordCandidateMultiplier),
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap,
            UpdatedAt = config.UpdatedAt
        };
    }
}