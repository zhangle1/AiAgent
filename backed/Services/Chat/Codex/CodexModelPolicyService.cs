using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Chat;

public sealed record CodexModelDefinition(
    string Id,
    string Name,
    string Description,
    string? AppServerModelId,
    string? ProfileName,
    bool SupportsReasoningEffort,
    bool IsBuiltin);

public sealed record CodexResolvedModel(CodexModelDefinition Definition, string? ReasoningEffort)
{
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string? AppServerModelId => Definition.AppServerModelId;
    public string? ProfileName => Definition.ProfileName;
}

public interface ICodexModelPolicyService
{
    CodexModelPolicyDto GetPolicy();
    CodexResolvedModel ResolveModel(string? requestedModelId, string? requestedReasoningEffort);
    CodexModelPolicyDto UpdatePolicy(AuthenticatedUser administrator, CodexModelPolicyUpdateRequest request);
}

/// <summary>Stores administrator allow-lists for Codex models and named local CLI profiles.</summary>
public sealed class CodexModelPolicyService : ICodexModelPolicyService
{
    private const string SettingKey = "codex_model_policy";
    private static readonly Regex ProfileNamePattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IReadOnlyList<string> SupportedReasoningEfforts = ["minimal", "low", "medium", "high", "xhigh"];
    private static readonly IReadOnlyList<CodexModelDefinition> BuiltinModels =
    [
        new("gpt-5.6-sol", "GPT-5.6 Sol", "适合复杂、开放式的编码与分析任务。", "gpt-5.6-sol", null, true, true),
        new("gpt-5.6-terra", "GPT-5.6 Terra", "适合日常开发任务，兼顾速度与质量。", "gpt-5.6-terra", null, true, true),
        new("gpt-5.6-luna", "GPT-5.6 Luna", "适合清晰、重复或高频的小型任务。", "gpt-5.6-luna", null, true, true)
    ];

    private readonly ISqlSugarClient _db;

    public CodexModelPolicyService(ISqlSugarClient db) => _db = db;

    public CodexModelPolicyDto GetPolicy() => ToDto(LoadPolicySafely());

    public CodexResolvedModel ResolveModel(string? requestedModelId, string? requestedReasoningEffort)
    {
        var policy = LoadPolicySafely();
        var requested = string.IsNullOrWhiteSpace(requestedModelId) ? policy.DefaultModelId : requestedModelId.Trim();
        if (!policy.AllowedModelIds.Contains(requested, StringComparer.Ordinal))
            throw new InvalidOperationException("The requested Codex model is not enabled by the administrator.");
        if (!policy.AllowChatModelOverride && !string.Equals(requested, policy.DefaultModelId, StringComparison.Ordinal))
            throw new InvalidOperationException("The administrator has disabled Codex model switching in chat.");

        var model = FindModel(policy, requested)
            ?? throw new InvalidOperationException("The requested Codex model is not supported by this AiAgent version.");
        if (!model.SupportsReasoningEffort)
        {
            if (!string.IsNullOrWhiteSpace(requestedReasoningEffort))
                throw new InvalidOperationException("The selected Codex profile does not support an app-server reasoning-effort override.");
            return new CodexResolvedModel(model, null);
        }

        var effort = string.IsNullOrWhiteSpace(requestedReasoningEffort) ? policy.DefaultReasoningEffort : requestedReasoningEffort.Trim();
        if (!policy.AllowedReasoningEfforts.Contains(effort, StringComparer.Ordinal))
            throw new InvalidOperationException("The requested Codex reasoning effort is not enabled by the administrator.");
        if (!policy.AllowChatReasoningEffortOverride && !string.Equals(effort, policy.DefaultReasoningEffort, StringComparison.Ordinal))
            throw new InvalidOperationException("The administrator has disabled Codex reasoning-effort switching in chat.");
        return new CodexResolvedModel(model, effort);
    }

    public CodexModelPolicyDto UpdatePolicy(AuthenticatedUser administrator, CodexModelPolicyUpdateRequest request)
    {
        if (!administrator.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        if (request is null) throw new ArgumentNullException(nameof(request));

        var current = LoadPolicySafely();
        var profiles = request.ProfileModels is null ? current.ProfileModels : NormalizeProfiles(request.ProfileModels);
        var models = GetAllModels(profiles);
        var allowed = (request.AllowedModelIds ?? current.AllowedModelIds)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (allowed.Count == 0) throw new InvalidOperationException("Enable at least one Codex model.");
        if (allowed.Any(value => models.All(model => !string.Equals(model.Id, value, StringComparison.Ordinal))))
            throw new InvalidOperationException("The policy contains an unsupported Codex model.");

        var defaultModel = request.DefaultModelId?.Trim() ?? current.DefaultModelId;
        if (!allowed.Contains(defaultModel, StringComparer.Ordinal)) throw new InvalidOperationException("The default Codex model must be enabled.");

        var efforts = (request.AllowedReasoningEfforts ?? current.AllowedReasoningEfforts)
            .Where(value => SupportedReasoningEfforts.Contains(value, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (efforts.Count == 0) throw new InvalidOperationException("Enable at least one Codex reasoning effort.");
        var defaultEffort = request.DefaultReasoningEffort?.Trim() ?? current.DefaultReasoningEffort;
        if (!efforts.Contains(defaultEffort, StringComparer.Ordinal)) throw new InvalidOperationException("The default Codex reasoning effort must be enabled.");

        var policy = new StoredPolicy
        {
            ProfileModels = profiles,
            AllowedModelIds = allowed,
            DefaultModelId = defaultModel,
            AllowChatModelOverride = request.AllowChatModelOverride ?? current.AllowChatModelOverride,
            AllowedReasoningEfforts = efforts,
            DefaultReasoningEffort = defaultEffort,
            AllowChatReasoningEffortOverride = request.AllowChatReasoningEffortOverride ?? current.AllowChatReasoningEffortOverride
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
        var fallback = CreateFallback();
        var row = _db.Queryable<AiSettingSnapshot>()
            .Where(item => item.SettingKey == SettingKey)
            .OrderByDescending(item => item.AppliedAt)
            .OrderByDescending(item => item.Id)
            .First();
        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson)) return fallback;
        try
        {
            var raw = JsonSerializer.Deserialize<StoredPolicy>(row.PayloadJson);
            if (raw is null) return fallback;
            var profiles = NormalizeStoredProfiles(raw.ProfileModels ?? []);
            var models = GetAllModels(profiles);
            var allowed = (raw.AllowedModelIds ?? [])
                .Where(id => models.Any(model => string.Equals(model.Id, id, StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (allowed.Count == 0) return fallback;
            var rawEfforts = raw.AllowedReasoningEfforts is { Count: > 0 } ? raw.AllowedReasoningEfforts : SupportedReasoningEfforts;
            var efforts = rawEfforts
                .Where(value => SupportedReasoningEfforts.Contains(value, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (efforts.Count == 0) efforts = ["medium"];
            var defaultModel = allowed.Contains(raw.DefaultModelId, StringComparer.Ordinal) ? raw.DefaultModelId : allowed[0];
            var defaultEffort = efforts.Contains(raw.DefaultReasoningEffort, StringComparer.Ordinal) ? raw.DefaultReasoningEffort : efforts[0];
            return new StoredPolicy
            {
                ProfileModels = profiles,
                AllowedModelIds = allowed,
                DefaultModelId = defaultModel,
                AllowChatModelOverride = raw.AllowChatModelOverride,
                AllowedReasoningEfforts = efforts,
                DefaultReasoningEffort = defaultEffort,
                AllowChatReasoningEffortOverride = raw.AllowChatReasoningEffortOverride
            };
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private StoredPolicy LoadPolicySafely()
    {
        try
        {
            return LoadPolicy();
        }
        catch
        {
            // A malformed historical snapshot must not make the chat settings page unavailable.
            return CreateFallback();
        }
    }

    private static StoredPolicy CreateFallback() => new()
    {
        AllowedModelIds = BuiltinModels.Select(item => item.Id).ToList(),
        DefaultModelId = "gpt-5.6-terra",
        AllowChatModelOverride = true,
        AllowedReasoningEfforts = SupportedReasoningEfforts.ToList(),
        DefaultReasoningEffort = "medium",
        AllowChatReasoningEffortOverride = true
    };

    private static List<StoredProfileModel> NormalizeProfiles(IEnumerable<CodexProfileModelDto> profiles)
    {
        var result = new List<StoredProfileModel>();
        foreach (var input in profiles)
        {
            var profileName = input.ProfileName?.Trim() ?? string.Empty;
            if (!ProfileNamePattern.IsMatch(profileName)) throw new InvalidOperationException("Codex profile names may only contain letters, numbers, hyphens, and underscores.");
            if (result.Any(item => string.Equals(item.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Each Codex profile may only be configured once.");
            var displayName = input.DisplayName?.Trim() ?? string.Empty;
            if (displayName.Length is < 2 or > 80 || displayName.Any(char.IsControl))
                throw new InvalidOperationException("The Codex profile display name must contain 2 to 80 printable characters.");
            var modelId = string.IsNullOrWhiteSpace(input.ModelId) ? null : input.ModelId.Trim();
            if (modelId?.Length > 160 || modelId?.Any(char.IsControl) == true)
                throw new InvalidOperationException("The Codex profile model id is invalid.");
            var description = input.Description?.Trim() ?? string.Empty;
            if (description.Length > 240 || description.Any(char.IsControl)) throw new InvalidOperationException("The Codex profile description is invalid.");
            result.Add(new StoredProfileModel
            {
                DisplayName = displayName,
                ProfileName = profileName,
                ModelId = modelId,
                Description = description,
                SupportsReasoningEffort = input.SupportsReasoningEffort
            });
        }
        return result;
    }

    private static List<StoredProfileModel> NormalizeStoredProfiles(IEnumerable<StoredProfileModel> profiles) =>
        NormalizeProfiles(profiles.Select(item => new CodexProfileModelDto
        {
            DisplayName = item.DisplayName,
            ProfileName = item.ProfileName,
            ModelId = item.ModelId,
            Description = item.Description,
            SupportsReasoningEffort = item.SupportsReasoningEffort
        }));

    private static IReadOnlyList<CodexModelDefinition> GetAllModels(IEnumerable<StoredProfileModel> profiles)
    {
        var models = BuiltinModels.ToList();
        foreach (var profile in profiles)
        {
            models.Add(new CodexModelDefinition(
                GetProfileModelId(profile.ProfileName),
                profile.DisplayName,
                string.IsNullOrWhiteSpace(profile.Description) ? $"Codex profile: {profile.ProfileName}" : profile.Description,
                profile.ModelId,
                profile.ProfileName,
                profile.SupportsReasoningEffort,
                false));
        }
        return models;
    }

    private static CodexModelDefinition? FindModel(StoredPolicy policy, string? id) =>
        GetAllModels(policy.ProfileModels).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    private static string GetProfileModelId(string profileName) => $"profile:{profileName.ToLowerInvariant()}";

    private static CodexModelPolicyDto ToDto(StoredPolicy policy) => new()
    {
        Models = GetAllModels(policy.ProfileModels).Select(item => new CodexModelOptionDto
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            ModelId = item.AppServerModelId,
            ProfileName = item.ProfileName,
            SupportsReasoningEffort = item.SupportsReasoningEffort,
            IsBuiltin = item.IsBuiltin
        }).ToList(),
        AllowedModelIds = policy.AllowedModelIds,
        DefaultModelId = policy.DefaultModelId,
        AllowChatModelOverride = policy.AllowChatModelOverride,
        AllowedReasoningEfforts = policy.AllowedReasoningEfforts,
        DefaultReasoningEffort = policy.DefaultReasoningEffort,
        AllowChatReasoningEffortOverride = policy.AllowChatReasoningEffortOverride,
        ProfileModels = policy.ProfileModels.Select(item => new CodexProfileModelDto
        {
            DisplayName = item.DisplayName,
            ProfileName = item.ProfileName,
            ModelId = item.ModelId,
            Description = item.Description,
            SupportsReasoningEffort = item.SupportsReasoningEffort
        }).ToList()
    };

    private sealed class StoredPolicy
    {
        public List<StoredProfileModel> ProfileModels { get; set; } = [];
        public List<string> AllowedModelIds { get; set; } = [];
        public string DefaultModelId { get; set; } = string.Empty;
        public bool AllowChatModelOverride { get; set; } = true;
        public List<string> AllowedReasoningEfforts { get; set; } = [];
        public string DefaultReasoningEffort { get; set; } = "medium";
        public bool AllowChatReasoningEffortOverride { get; set; } = true;
    }

    private sealed class StoredProfileModel
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string? ModelId { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool SupportsReasoningEffort { get; set; }
    }
}
