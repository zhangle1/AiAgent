using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Settings;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.Knowledge;

public sealed class KnowledgeCompilerSettings(ISqlSugarClient db)
{
    private const string Key = "knowledge_compiler";
    public KnowledgeCompilerSettingsDto Get()
    {
        var row = db.Queryable<AiSettingSnapshot>().Where(x => x.SettingKey == Key).OrderByDescending(x => x.Id).First();
        return row is null ? new() : JsonSerializer.Deserialize<KnowledgeCompilerSettingsDto>(row.PayloadJson) ?? new();
    }

    public KnowledgeCompilerSettingsDto Save(KnowledgeCompilerSettingsDto config)
    {
        Validate(config);
        var version = db.Queryable<AiSettingSnapshot>().Where(x => x.SettingKey == Key).Max(x => x.VersionNo);
        db.Insertable(new AiSettingSnapshot { SettingKey = Key, PayloadJson = JsonSerializer.Serialize(config),
            VersionNo = version + 1, AppliedAt = DateTime.UtcNow, AppliedBy = "knowledge-settings" }).ExecuteCommand();
        return config;
    }

    public static void Validate(KnowledgeCompilerSettingsDto config)
    {
        if (config.Generator is not ("codex" or "llm_api")) throw new ArgumentException("Generator must be codex or llm_api.");
        if (config.MaxSteps is < 8 or > 96 || config.TimeoutMinutes is < 1 or > 60) throw new ArgumentException("Invalid compiler action budget or timeout.");
        if (config.ModelId?.Length > 256 || config.ReasoningEffort?.Length > 32) throw new ArgumentException("Model setting is too long.");
    }
}
