using System.Reflection;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Settings;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public void KnowledgeModelCatalogUsesAnIndependentClientForLoadSaveAndApply()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiModelProfile), typeof(AiModel), typeof(AiModelProvider));
        var root = DispatchProxy.Create<ISqlSugarClient, UnavailableRootClient>();
        ((UnavailableRootClient)(object)root).Database = db;
        var catalog = new ModelCatalogService(root);
        var loaded = catalog.Load();
        Assert.NotEmpty(loaded.Services.Llm.Profiles);
        var saved = catalog.Save(loaded);
        Assert.NotEmpty(saved.Services.Llm.Profiles);
        Assert.NotNull(catalog.Apply());
    }

    // Simulate a shared scope whose connection belongs to another concurrent lifecycle.
    public class UnavailableRootClient : DispatchProxy
    {
        public ISqlSugarClient Database { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method!.Name == "CopyNew") return Database.CopyNew();
            throw new InvalidOperationException("ExecuteReader 要求已打开且可用的 Connection。连接的当前状态为已关闭。");
        }
    }
}
