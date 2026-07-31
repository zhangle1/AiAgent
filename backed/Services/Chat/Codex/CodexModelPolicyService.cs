using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public sealed record CodexModelDefinition(string Id, string Name, string Description);

public interface ICodexModelPolicyService
{
    CodexModelPolicyDto GetPolicy();
    CodexModelDefinition ResolveModel(string? requestedModelId);
    CodexModelPolicyDto UpdatePolicy(AuthenticatedUser administrator, CodexModelPolicyUpdateRequest request);
}

/// <summary>Stores the small, allow-listed Codex model policy independently from a user's local Codex config file.</summary>
public sealed class CodexModelPolicyService : ICodexModelPolicyService
{
    private const string SettingKey = "codex_model_policy";
    private static readonly IReadOnlyList<CodexModelDefinition> AvailableModels =
    [
        new("gpt-5.6-sol", "GPT-5.6 Sol", "适合复杂、开放式的编码与分析任务。"),
        new("gpt-5.6-terra", "GPT-5.6 Terra", "适合日常开发任务，兼顾速度与质量。")
    ];

    private readonly ISqlSugarClient _db;

    public CodexModelPolicyService(ISqlSugarClient db) => _db = db;

    public CodexModelPolicyDto GetPolicy()
    {
        var policy = LoadPolicy();
        return ToDto(policy);
    }

    public CodexModelDefinition ResolveModel(string? requestedModelId)
    {
        var policy = LoadPolicy();
        var requested = string.IsNullOrWhiteSpace(requestedModelId) ? policy.DefaultModelId : requestedModelId.Trim();
        if (!policy.AllowedModelIds.Contains(requested, StringComparer.Ordinal))
            throw new InvalidOperationException("The requested Codex model is not enabled by the administrator.");
        if (!policy.AllowChatModelOverride && !string.Equals(requested, policy.DefaultModelId, StringComparison.Ordinal))
            throw new InvalidOperationException("The administrator has disabled Codex model switching in chat.");
        return FindModel(requested) ?? throw new InvalidOperationException("The requested Codex model is not supported by this AiAgent version.");
    }

    public CodexModelPolicyDto UpdatePolicy(AuthenticatedUser administrator, CodexModelPolicyUpdateRequest request)
    {
        if (!administrator.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        if (request is null) throw new ArgumentNullException(nameof(request));

        var allowed = (request.AllowedModelIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (allowed.Count == 0) throw new InvalidOperationException("Enable at least one Codex model.");
        if (allowed.Any(value => FindModel(value) is null)) throw new InvalidOperationException("The policy contains an unsupported Codex model.");

        var defaultModel = request.DefaultModelId?.Trim() ?? string.Empty;
        if (!allowed.Contains(defaultModel, StringComparer.Ordinal)) throw new InvalidOperationException("The default Codex model must be enabled.");

        var policy = new StoredPolicy
        {
            AllowedModelIds = allowed,
            DefaultModelId = defaultModel,
            AllowChatModelOverride = request.AllowChatModelOverride ?? true
        };
        var latestVersion = _db.Queryable<AiSettingSnapshot>()
            .Where(item => item.SettingKey == SettingKey)
            .OrderByDescending(item => item.VersionNo)
            .Select(item => item.VersionNo)
            .First();
        _db.Insertable(new AiSettingSnapshot
        {
            SettingKey = SettingKey,
            PayloadJson = JsonSerializer.Serialize(policy),
            VersionNo = latestVersion + 1,
            AppliedAt = DateTime.UtcNow,
            AppliedBy = administrator.Username,
            Remark = "Codex model policy updated"
        }).ExecuteCommand();
        return ToDto(policy);
    }

    private StoredPolicy LoadPolicy()
    {
        var fallback = new StoredPolicy
        {
            AllowedModelIds = AvailableModels.Select(item => item.Id).ToList(),
            DefaultModelId = AvailableModels[0].Id,
            AllowChatModelOverride = true
        };
        var row = _db.Queryable<AiSettingSnapshot>()
            .Where(item => item.SettingKey == SettingKey)
            .OrderByDescending(item => item.AppliedAt)
            .OrderByDescending(item => item.Id)
            .First();
        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson)) return fallback;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPolicy>(row.PayloadJson);
            var allowed = (stored?.AllowedModelIds ?? [])
                .Where(value => FindModel(value) is not null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (allowed.Count == 0) return fallback;
            var defaultModel = !string.IsNullOrWhiteSpace(stored?.DefaultModelId) && allowed.Contains(stored.DefaultModelId, StringComparer.Ordinal)
                ? stored.DefaultModelId
                : allowed[0];
            return new StoredPolicy
            {
                AllowedModelIds = allowed,
                DefaultModelId = defaultModel,
                AllowChatModelOverride = stored?.AllowChatModelOverride ?? true
            };
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static CodexModelDefinition? FindModel(string? id) => AvailableModels.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    private static CodexModelPolicyDto ToDto(StoredPolicy policy) => new()
    {
        Models = AvailableModels.Select(item => new CodexModelOptionDto { Id = item.Id, Name = item.Name, Description = item.Description }).ToList(),
        AllowedModelIds = policy.AllowedModelIds,
        DefaultModelId = policy.DefaultModelId,
        AllowChatModelOverride = policy.AllowChatModelOverride
    };

    private sealed class StoredPolicy
    {
        public List<string> AllowedModelIds { get; set; } = [];
        public string DefaultModelId { get; set; } = string.Empty;
        public bool AllowChatModelOverride { get; set; } = true;
    }
}
