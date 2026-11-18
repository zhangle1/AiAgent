using System;
using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class CodexModelPolicyTests
{
    private static SqlSugarScope Database(int version)
    {
        var db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:",
            IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new() { EntityService = (_, column) => {
                if (column.DataType == "nvarchar(max)") column.DataType = "text";
                if (column.IsIdentity) column.DataType = "INTEGER";
            } }
        });
        db.CodeFirst.InitTables<AiSettingSnapshot>();
        db.Insertable(new AiSettingSnapshot {
            SettingKey = "codex_model_policy",
            PayloadJson = JsonSerializer.Serialize(new {
                BuiltinModelsVersion = version,
                AllowedModelIds = new[] { "gpt-5.6-terra" },
                DefaultModelId = "gpt-5.6-terra",
                AllowedReasoningEfforts = new[] { "none", "low", "medium", "max" },
                DefaultReasoningEffort = "none"
            })
        }).ExecuteCommand();
        return db;
    }

    [Fact]
    public void UpgradeAdds61ModelsWithoutReenablingOlderModelsOrChangingDefault()
    {
        using var db = Database(3);
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.Equal(new[] { "gpt-5.6-terra", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6.2-sol", "gpt-6.2-astra" }, policy.AllowedModelIds);
        Assert.Equal("gpt-5.6-terra", policy.DefaultModelId);
    }

    [Fact]
    public void CurrentPolicyKeepsSol61Disabled()
    {
        using var db = Database(4);
        var service = new CodexModelPolicyService(db);
        Assert.DoesNotContain("gpt-6.1-sol", service.GetPolicy().AllowedModelIds);
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-sol", "medium"));
    }

    [Fact]
    public void UpgradeAddsTerra61WithoutReenablingOlderModelsOrChangingDefault()
    {
        using var db = Database(4);
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.Equal(new[] { "gpt-5.6-terra", "gpt-6.1-terra", "gpt-6.2-sol", "gpt-6.2-astra" }, policy.AllowedModelIds);
        Assert.Equal("gpt-5.6-terra", policy.DefaultModelId);
    }

    [Fact]
    public void CurrentPolicyKeepsTerra61Disabled()
    {
        using var db = Database(5);
        var service = new CodexModelPolicyService(db);
        Assert.DoesNotContain("gpt-6.1-terra", service.GetPolicy().AllowedModelIds);
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-terra", "medium"));
    }

    [Fact]
    public void Sol61UsesCompatibleDefaultAndRejectsUnsupportedEfforts()
    {
        using var db = Database(3);
        var service = new CodexModelPolicyService(db);
        var resolved = service.ResolveModel("gpt-6.1-sol", null);
        Assert.Equal("gpt-6.1-sol", resolved.AppServerModelId);
        Assert.Equal("low", resolved.ReasoningEffort);
        Assert.Equal("max", service.ResolveModel("gpt-6.1-sol", "max").ReasoningEffort);
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-sol", "none"));
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-sol", "minimal"));
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-sol", "ultra"));
    }

    [Fact]
    public void Terra61UsesCompatibleDefaultAndRejectsUnsupportedEfforts()
    {
        using var db = Database(4);
        var service = new CodexModelPolicyService(db);
        var resolved = service.ResolveModel("gpt-6.1-terra", null);
        Assert.Equal("gpt-6.1-terra", resolved.AppServerModelId);
        Assert.Equal("low", resolved.ReasoningEffort);
        Assert.Equal("max", service.ResolveModel("gpt-6.1-terra", "max").ReasoningEffort);
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-terra", "none"));
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-terra", "minimal"));
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel("gpt-6.1-terra", "ultra"));
    }

    [Fact]
    public void UpgradeAdds62ModelsWithoutReenablingOlderModelsOrChangingDefault()
    {
        using var db = Database(5);
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.Equal(new[] { "gpt-5.6-terra", "gpt-6.2-sol", "gpt-6.2-astra" }, policy.AllowedModelIds);
        Assert.Equal("gpt-5.6-terra", policy.DefaultModelId);
        Assert.Equal("none", policy.DefaultReasoningEffort);
    }

    [Theory]
    [InlineData("gpt-6.2-sol", "GPT-6.2 Sol")]
    [InlineData("gpt-6.2-astra", "GPT-6.2 Astra")]
    public void Models62UseNativeAppServerAndCompatibleEfforts(string modelId, string displayName)
    {
        using var db = Database(5);
        var service = new CodexModelPolicyService(db);
        var option = Assert.Single(service.GetPolicy().Models, item => item.Id == modelId);
        Assert.Equal(displayName, option.Name);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, option.ReasoningEfforts);
        Assert.Equal("native", option.ImageInput);

        var resolved = service.ResolveModel(modelId, null);
        Assert.Equal(modelId, resolved.AppServerModelId);
        Assert.Null(resolved.ProfileName);
        Assert.Equal("low", resolved.ReasoningEffort);
        Assert.Equal("max", service.ResolveModel(modelId, "max").ReasoningEffort);
        Assert.Throws<InvalidOperationException>(() => service.ResolveModel(modelId, "none"));
    }

    [Fact]
    public void SavingUpgradedPolicyKeeps62ModelsDisabledAfterReload()
    {
        using var db = Database(5);
        var service = new CodexModelPolicyService(db);
        service.UpdatePolicy(new AuthenticatedUser("admin", "admin", "admin"), new CodexModelPolicyUpdateRequest
        {
            AllowedModelIds = ["gpt-5.6-terra"],
            AllowChatModelOverride = false
        });

        var reloaded = new CodexModelPolicyService(db);
        var policy = reloaded.GetPolicy();
        Assert.Equal("gpt-5.6-terra", Assert.Single(policy.AllowedModelIds));
        Assert.False(policy.AllowChatModelOverride);
        Assert.Throws<InvalidOperationException>(() => reloaded.ResolveModel("gpt-6.2-sol", null));
        Assert.Throws<InvalidOperationException>(() => reloaded.ResolveModel("gpt-6.2-astra", null));
    }

    [Fact]
    public void OlderPolicyGetsAllAddedModelGenerations()
    {
        using var db = Database(2);
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.Contains("gpt-6-astra", policy.AllowedModelIds);
        Assert.Contains("gpt-6-sol", policy.AllowedModelIds);
        Assert.Contains("gpt-6-luna", policy.AllowedModelIds);
        Assert.Contains("gpt-6.1-sol", policy.AllowedModelIds);
        Assert.Contains("gpt-6.1-terra", policy.AllowedModelIds);
    }
}
