using System;
using System.Text.Json;
using AiAgent.Backend.Entities.Settings;
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
    public void UpgradeAddsSol61WithoutReenablingOlderModelsOrChangingDefault()
    {
        using var db = Database(3);
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.Equal(new[] { "gpt-5.6-terra", "gpt-6.1-sol" }, policy.AllowedModelIds);
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
    public void OlderPolicyGetsBothModelGenerations()
    {
        using var db = Database(2);
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.Contains("gpt-6-astra", policy.AllowedModelIds);
        Assert.Contains("gpt-6-sol", policy.AllowedModelIds);
        Assert.Contains("gpt-6-luna", policy.AllowedModelIds);
        Assert.Contains("gpt-6.1-sol", policy.AllowedModelIds);
    }
}
