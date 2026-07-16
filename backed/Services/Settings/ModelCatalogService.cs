using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Models.Settings;
using SqlSugar;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型服务目录读写实现，负责数据库持久化、脱敏和应用快照。
/// </summary>
public sealed class ModelCatalogService : IModelCatalogService
{
    private static readonly string[] ServiceNames = ["llm", "embedding", "search", "tts", "stt", "imagegen", "videogen"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null, WriteIndented = true };

    private readonly ISqlSugarClient _db;
    private readonly object _gate = new();

    /// <summary>
    /// 初始化模型目录服务，负责读取、保存和应用各类模型配置。
    /// </summary>
    public ModelCatalogService(ISqlSugarClient db)
    {
        _db = db;
    }

    /// <summary>
    /// 从数据库加载模型服务目录，可选择对密钥字段脱敏。
    /// </summary>
    public ModelCatalog Load(bool redactSecrets = false)
    {
        lock (_gate)
        {
            EnsureCatalogRows();
            var catalog = ReadFromDb();
            return redactSecrets ? RedactSecrets(catalog) : catalog;
        }
    }

    /// <summary>
    /// 保存模型服务目录，并保留前端传回的脱敏密钥原值。
    /// </summary>
    public ModelCatalog Save(ModelCatalog catalog)
    {
        lock (_gate)
        {
            EnsureCatalogRows();
            var current = ReadFromDb();
            PreserveRedactedSecrets(catalog, current);
            var normalized = Normalize(catalog);
            SaveToDb(normalized, writeSnapshot: false);
            return RedactSecrets(ReadFromDb());
        }
    }

    /// <summary>
    /// 应用模型服务目录并写入设置快照。
    /// </summary>
    public ApplyResult Apply(ModelCatalog? catalog = null)
    {
        lock (_gate)
        {
            EnsureCatalogRows();
            var normalized = Normalize(catalog ?? ReadFromDb());
            SaveToDb(normalized, writeSnapshot: true);
            return new ApplyResult
            {
                CatalogPath = "database: ai_model_profile / ai_model",
                Services = [.. ServiceNames],
                AppliedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private void EnsureCatalogRows()
    {
        var exists = _db.Queryable<AiModelProfile>().Any(x => !x.IsDeleted);
        if (!exists)
        {
            SaveToDb(DefaultCatalog(), writeSnapshot: false);
        }
    }

    private ModelCatalog ReadFromDb()
    {
        var catalog = new ModelCatalog { Version = 1 };
        var serviceNames = ServiceNames.ToList();
        var profiles = _db.Queryable<AiModelProfile>()
            .Where(x => serviceNames.Contains(x.ServiceType) && !x.IsDeleted)
            .OrderBy(x => x.ServiceType)
            .OrderBy(x => x.SortOrder)
            .ToList();

        var profileIds = profiles.Select(x => x.Id).ToList();
        var models = profileIds.Count == 0
            ? new List<AiModel>()
            : _db.Queryable<AiModel>()
                .Where(x => profileIds.Contains(x.ProfileId) && !x.IsDeleted)
                .OrderBy(x => x.ProfileId)
                .OrderBy(x => x.SortOrder)
                .ToList();

        foreach (var serviceName in ServiceNames)
        {
            SetService(catalog, serviceName, BuildService(serviceName, profiles, models));
        }

        return Normalize(catalog);
    }

    private static CatalogService BuildService(string serviceName, List<AiModelProfile> profiles, List<AiModel> models)
    {
        var serviceProfiles = profiles.Where(x => x.ServiceType == serviceName).ToList();
        var service = new CatalogService();

        foreach (var profileRow in serviceProfiles)
        {
            var profile = new CatalogProfile
            {
                Id = profileRow.Id.ToString(),
                Name = profileRow.ProfileName,
                Binding = serviceName == "search" ? null : profileRow.ProviderCode,
                Provider = serviceName == "search" ? profileRow.ProviderCode : null,
                BaseUrl = profileRow.BaseUrl ?? "",
                ApiKey = DecodeSecret(profileRow.ApiKeyCipher),
                ApiVersion = profileRow.ApiVersion ?? "",
                ExtraHeaders = DeserializeDictionary(profileRow.ExtraHeadersJson),
                Proxy = profileRow.ProxyUrl,
                MaxResults = profileRow.MaxResults,
                Models = models
                    .Where(x => x.ProfileId == profileRow.Id)
                    .Select(ToCatalogModel)
                    .ToList()
            };
            service.Profiles.Add(profile);
        }

        var activeProfile = serviceProfiles.FirstOrDefault(x => x.IsActive) ?? serviceProfiles.FirstOrDefault();
        service.ActiveProfileId = activeProfile?.Id.ToString();
        if (serviceName != "search" && activeProfile is not null)
        {
            var activeModel = models.FirstOrDefault(x => x.ProfileId == activeProfile.Id && x.IsActive)
                ?? models.FirstOrDefault(x => x.ProfileId == activeProfile.Id);
            service.ActiveModelId = activeModel?.Id.ToString();
        }

        return service;
    }

    private void SaveToDb(ModelCatalog catalog, bool writeSnapshot)
    {
        var providerRows = _db.Queryable<AiModelProvider>()
            .Where(x => !x.IsDeleted)
            .ToList()
            .ToDictionary(x => $"{x.ServiceType}:{x.ProviderCode}", StringComparer.OrdinalIgnoreCase);

        try
        {
            _db.Ado.BeginTran();
            foreach (var (serviceName, service) in EnumerateNamedServices(catalog))
            {
                SaveService(serviceName, service, providerRows);
            }

            if (writeSnapshot)
            {
                _db.Insertable(new AiSettingSnapshot
                {
                    SettingKey = "model_catalog",
                    PayloadJson = JsonSerializer.Serialize(RedactSecrets(catalog), JsonOptions),
                    AppliedAt = DateTime.UtcNow,
                    VersionNo = catalog.Version <= 0 ? 1 : catalog.Version
                }).ExecuteCommand();
            }

            _db.Ado.CommitTran();
        }
        catch
        {
            _db.Ado.RollbackTran();
            throw;
        }
    }

    private void SaveService(string serviceName, CatalogService service, Dictionary<string, AiModelProvider> providerRows)
    {
        var now = DateTime.UtcNow;
        var existingProfiles = _db.Queryable<AiModelProfile>()
            .Where(x => x.ServiceType == serviceName && !x.IsDeleted)
            .ToList();
        var existingProfileMap = existingProfiles.ToDictionary(x => x.Id);
        var keepProfileIds = new List<long>();

        _db.Updateable<AiModelProfile>()
            .SetColumns(x => new AiModelProfile { IsActive = false, UpdatedAt = now })
            .Where(x => x.ServiceType == serviceName && !x.IsDeleted)
            .ExecuteCommand();

        foreach (var profile in service.Profiles)
        {
            var profileId = SaveProfile(serviceName, service, profile, existingProfileMap, providerRows, now);
            keepProfileIds.Add(profileId);
            SaveModels(serviceName, service, profileId, profile, now);
            profile.Id = profileId.ToString();
        }

        var deleteProfileIds = existingProfiles.Select(x => x.Id).Except(keepProfileIds).ToList();
        if (deleteProfileIds.Count > 0)
        {
            _db.Updateable<AiModelProfile>()
                .SetColumns(x => new AiModelProfile { IsDeleted = true, IsActive = false, UpdatedAt = now })
                .Where(x => deleteProfileIds.Contains(x.Id))
                .ExecuteCommand();
            _db.Updateable<AiModel>()
                .SetColumns(x => new AiModel { IsDeleted = true, IsActive = false, UpdatedAt = now })
                .Where(x => deleteProfileIds.Contains(x.ProfileId))
                .ExecuteCommand();
        }
    }

    private long SaveProfile(
        string serviceName,
        CatalogService service,
        CatalogProfile profile,
        Dictionary<long, AiModelProfile> existingProfileMap,
        Dictionary<string, AiModelProvider> providerRows,
        DateTime now)
    {
        var providerCode = serviceName == "search" ? profile.Provider ?? "none" : profile.Binding ?? "openai";
        providerRows.TryGetValue($"{serviceName}:{providerCode}", out var provider);
        var isExisting = false;
        var row = new AiModelProfile { CreatedAt = now };
        if (long.TryParse(profile.Id, out var id) && existingProfileMap.TryGetValue(id, out var existing))
        {
            row = existing;
            isExisting = true;
        }

        row.ServiceType = serviceName;
        row.ProfileName = profile.Name;
        row.ProviderId = provider?.Id;
        row.ProviderCode = providerCode;
        row.ProviderName = provider?.ProviderName ?? providerCode;
        row.BindingType = provider?.BindingType ?? providerCode;
        row.BaseUrl = profile.BaseUrl;
        row.ApiKeyCipher = EncodeSecret(profile.ApiKey);
        row.ApiVersion = profile.ApiVersion;
        row.AuthType = provider?.AuthType ?? "bearer";
        row.ExtraHeadersJson = JsonSerializer.Serialize(profile.ExtraHeaders ?? new Dictionary<string, string>(), JsonOptions);
        row.ProxyUrl = profile.Proxy;
        row.MaxResults = profile.MaxResults;
        row.IsActive = profile.Id == service.ActiveProfileId || service.Profiles.Count == 1;
        row.IsDeleted = false;
        row.UpdatedAt = now;

        if (isExisting)
        {
            _db.Updateable(row).ExecuteCommand();
            return row.Id;
        }

        return _db.Insertable(row).ExecuteReturnBigIdentity();
    }

    private void SaveModels(string serviceName, CatalogService service, long profileId, CatalogProfile profile, DateTime now)
    {
        var existingModels = _db.Queryable<AiModel>()
            .Where(x => x.ProfileId == profileId && !x.IsDeleted)
            .ToList();
        var existingModelMap = existingModels.ToDictionary(x => x.Id);
        var keepModelIds = new List<long>();

        _db.Updateable<AiModel>()
            .SetColumns(x => new AiModel { IsActive = false, UpdatedAt = now })
            .Where(x => x.ProfileId == profileId && !x.IsDeleted)
            .ExecuteCommand();

        foreach (var model in profile.Models)
        {
            var isExisting = false;
            var row = new AiModel { CreatedAt = now };
            if (long.TryParse(model.Id, out var id) && existingModelMap.TryGetValue(id, out var existing))
            {
                row = existing;
                isExisting = true;
            }

            row.ProfileId = profileId;
            row.ServiceType = serviceName;
            row.ModelName = model.Name;
            row.ModelId = model.Model;
            row.ContextWindow = ParseInt(model.ContextWindow);
            row.Dimension = ParseInt(model.Dimension);
            row.SendDimensions = model.SendDimensions;
            row.SupportedDimensions = model.SupportedDimensions;
            row.Voice = model.Voice;
            row.ResponseFormat = model.ResponseFormat;
            row.Language = model.Language;
            row.Size = model.Size;
            row.Quality = model.Quality;
            row.Style = model.Style;
            row.AspectRatio = model.AspectRatio;
            row.DurationSeconds = ParseInt(model.Duration);
            row.Resolution = model.Resolution;
            row.IsActive = model.Id == service.ActiveModelId || profile.Models.Count == 1;
            row.IsDeleted = false;
            row.UpdatedAt = now;

            if (isExisting)
            {
                _db.Updateable(row).ExecuteCommand();
            }
            else
            {
                row.Id = _db.Insertable(row).ExecuteReturnBigIdentity();
            }

            model.Id = row.Id.ToString();
            keepModelIds.Add(row.Id);
        }

        var deleteModelIds = existingModels.Select(x => x.Id).Except(keepModelIds).ToList();
        if (deleteModelIds.Count > 0)
        {
            _db.Updateable<AiModel>()
                .SetColumns(x => new AiModel { IsDeleted = true, IsActive = false, UpdatedAt = now })
                .Where(x => deleteModelIds.Contains(x.Id))
                .ExecuteCommand();
        }
    }

    private static CatalogModel ToCatalogModel(AiModel row)
    {
        return new CatalogModel
        {
            Id = row.Id.ToString(),
            Name = row.ModelName,
            Model = row.ModelId,
            ContextWindow = row.ContextWindow?.ToString(),
            Dimension = row.Dimension?.ToString(),
            SendDimensions = row.SendDimensions,
            SupportedDimensions = row.SupportedDimensions,
            Voice = row.Voice,
            ResponseFormat = row.ResponseFormat,
            Language = row.Language,
            Size = row.Size,
            Quality = row.Quality,
            Style = row.Style,
            AspectRatio = row.AspectRatio,
            Duration = row.DurationSeconds?.ToString(),
            Resolution = row.Resolution
        };
    }

    private static ModelCatalog DefaultCatalog() => new()
    {
        Version = 1,
        Services = new ModelCatalogServices
        {
            Llm = ServiceWithModel("llm", "Default LLM Endpoint", "deepseek", "https://api.deepseek.com", "deepseek-chat", model => model.ContextWindow = "65536"),
            Embedding = ServiceWithModel("embedding", "OpenAI Embedding", "openai", "https://api.openai.com/v1", "text-embedding-3-small", model =>
            {
                model.Dimension = "1536";
                model.SendDimensions = true;
            }),
            Search = SearchService(),
            Tts = ServiceWithModel("tts", "OpenAI TTS", "openai", "https://api.openai.com/v1", "gpt-4o-mini-tts", model => model.Voice = "alloy"),
            Stt = ServiceWithModel("stt", "OpenAI STT", "openai", "https://api.openai.com/v1", "gpt-4o-mini-transcribe"),
            Imagegen = ServiceWithModel("imagegen", "OpenAI Image", "openai", "https://api.openai.com/v1", "gpt-image-1", model => model.Size = "1024x1024"),
            Videogen = ServiceWithModel("videogen", "DashScope Video", "dashscope", "https://dashscope.aliyuncs.com/api/v1", "wan2.1-t2v-turbo", model => model.Duration = "5")
        }
    };

    private static CatalogService ServiceWithModel(string service, string profileName, string binding, string baseUrl, string modelName, Action<CatalogModel>? configureModel = null)
    {
        var profileId = $"{service}-profile-default";
        var modelId = $"{service}-model-default";
        var model = new CatalogModel { Id = modelId, Name = modelName, Model = modelName };
        configureModel?.Invoke(model);
        return new CatalogService
        {
            ActiveProfileId = profileId,
            ActiveModelId = modelId,
            Profiles = [new CatalogProfile { Id = profileId, Name = profileName, Binding = binding, BaseUrl = baseUrl, Models = [model] }]
        };
    }

    private static CatalogService SearchService()
    {
        const string profileId = "search-profile-default";
        return new CatalogService
        {
            ActiveProfileId = profileId,
            ActiveModelId = null,
            Profiles = [new CatalogProfile { Id = profileId, Name = "No Search", Provider = "none", BaseUrl = "", Models = [] }]
        };
    }

    private static ModelCatalog Normalize(ModelCatalog? catalog)
    {
        catalog ??= DefaultCatalog();
        catalog.Version = catalog.Version <= 0 ? 1 : catalog.Version;
        catalog.Services ??= new ModelCatalogServices();
        catalog.Services.Llm ??= CatalogService.CreateModelService();
        catalog.Services.Embedding ??= CatalogService.CreateModelService();
        catalog.Services.Search ??= CatalogService.CreateSearchService();
        catalog.Services.Tts ??= CatalogService.CreateModelService();
        catalog.Services.Stt ??= CatalogService.CreateModelService();
        catalog.Services.Imagegen ??= CatalogService.CreateModelService();
        catalog.Services.Videogen ??= CatalogService.CreateModelService();

        NormalizeService(catalog.Services.Llm, "llm", true);
        NormalizeService(catalog.Services.Embedding, "embedding", true);
        NormalizeService(catalog.Services.Search, "search", false);
        NormalizeService(catalog.Services.Tts, "tts", true);
        NormalizeService(catalog.Services.Stt, "stt", true);
        NormalizeService(catalog.Services.Imagegen, "imagegen", true);
        NormalizeService(catalog.Services.Videogen, "videogen", true);
        return catalog;
    }

    private static void NormalizeService(CatalogService service, string name, bool hasModels)
    {
        service.Profiles ??= [];
        foreach (var profile in service.Profiles)
        {
            profile.Id = Blank(profile.Id) ? $"{name}-profile-{Guid.NewGuid():N}" : profile.Id.Trim();
            profile.Name = Blank(profile.Name) ? "Untitled Profile" : profile.Name.Trim();
            profile.BaseUrl = profile.BaseUrl?.Trim() ?? "";
            profile.ApiKey = profile.ApiKey?.Trim() ?? "";
            profile.ApiVersion = profile.ApiVersion?.Trim() ?? "";
            profile.Binding = hasModels ? (Blank(profile.Binding) ? "openai" : profile.Binding) : null;
            profile.Provider = hasModels ? null : (Blank(profile.Provider) ? "none" : profile.Provider);
            profile.Models ??= [];

            if (!hasModels)
            {
                profile.Models.Clear();
                continue;
            }

            foreach (var model in profile.Models)
            {
                model.Id = Blank(model.Id) ? $"{name}-model-{Guid.NewGuid():N}" : model.Id.Trim();
                model.Name = Blank(model.Name) ? $"Model {profile.Models.IndexOf(model) + 1}" : model.Name.Trim();
                model.Model = model.Model?.Trim() ?? "";
            }
        }

        var activeProfile = service.Profiles.FirstOrDefault(x => x.Id == service.ActiveProfileId) ?? service.Profiles.FirstOrDefault();
        service.ActiveProfileId = activeProfile?.Id;
        service.ActiveModelId = hasModels
            ? activeProfile?.Models.FirstOrDefault(x => x.Id == service.ActiveModelId)?.Id ?? activeProfile?.Models.FirstOrDefault()?.Id
            : null;
    }

    private static ModelCatalog RedactSecrets(ModelCatalog catalog)
    {
        var clone = JsonSerializer.Deserialize<ModelCatalog>(JsonSerializer.Serialize(catalog, JsonOptions), JsonOptions) ?? new ModelCatalog();
        foreach (var profile in EnumerateServices(clone).SelectMany(x => x.Profiles))
        {
            if (!string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                profile.ApiKey = "********";
            }
        }

        return clone;
    }

    private static void PreserveRedactedSecrets(ModelCatalog incoming, ModelCatalog current)
    {
        var currentProfiles = EnumerateServices(current)
            .SelectMany(x => x.Profiles)
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, x => x.ApiKey);

        foreach (var profile in EnumerateServices(incoming).SelectMany(x => x.Profiles))
        {
            if (profile.ApiKey == "********" && currentProfiles.TryGetValue(profile.Id, out var apiKey))
            {
                profile.ApiKey = apiKey;
            }
        }
    }

    private static IEnumerable<CatalogService> EnumerateServices(ModelCatalog catalog)
    {
        yield return catalog.Services.Llm;
        yield return catalog.Services.Embedding;
        yield return catalog.Services.Search;
        yield return catalog.Services.Tts;
        yield return catalog.Services.Stt;
        yield return catalog.Services.Imagegen;
        yield return catalog.Services.Videogen;
    }

    private static IEnumerable<(string Name, CatalogService Service)> EnumerateNamedServices(ModelCatalog catalog)
    {
        yield return ("llm", catalog.Services.Llm);
        yield return ("embedding", catalog.Services.Embedding);
        yield return ("search", catalog.Services.Search);
        yield return ("tts", catalog.Services.Tts);
        yield return ("stt", catalog.Services.Stt);
        yield return ("imagegen", catalog.Services.Imagegen);
        yield return ("videogen", catalog.Services.Videogen);
    }

    private static void SetService(ModelCatalog catalog, string name, CatalogService service)
    {
        switch (name)
        {
            case "llm": catalog.Services.Llm = service; break;
            case "embedding": catalog.Services.Embedding = service; break;
            case "search": catalog.Services.Search = service; break;
            case "tts": catalog.Services.Tts = service; break;
            case "stt": catalog.Services.Stt = service; break;
            case "imagegen": catalog.Services.Imagegen = service; break;
            case "videogen": catalog.Services.Videogen = service; break;
        }
    }

    private static Dictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? EncodeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.StartsWith("b64:", StringComparison.Ordinal) ? value : $"b64:{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}";
    }

    private static string DecodeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!value.StartsWith("b64:", StringComparison.Ordinal)) return value;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value[4..]));
        }
        catch
        {
            return "";
        }
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var result) ? result : null;

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}