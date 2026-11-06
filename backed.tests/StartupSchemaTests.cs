using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using AiAgent.Backend.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed class StartupSchemaTests(ITestOutputHelper output)
{
    [Fact]
    public void IndexInitializationUsesOneRoundTripAndKeepsGuardedDefinitions()
    {
        var client = DispatchProxy.Create<ISqlSugarClient, SchemaClientProbe>();
        var adoType = typeof(ISqlSugarClient).GetProperty(nameof(ISqlSugarClient.Ado))!.PropertyType;
        var ado = DispatchProxy.Create(adoType, typeof(SchemaAdoProbe));
        ((SchemaClientProbe)(object)client).Ado = ado;
        var probe = (SchemaAdoProbe)ado;
        var initializer = new ModelSchemaInitializer(client, NullLogger<ModelSchemaInitializer>.Instance);

        var timer = Stopwatch.StartNew();
        typeof(ModelSchemaInitializer).GetMethod("EnsureIndexes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(initializer, null);
        timer.Stop();

        var sql = string.Join("\n", probe.Statements);
        var guards = Regex.Matches(sql, "IF NOT EXISTS \\(SELECT 1 FROM sys.indexes").Count;
        var indexes = Regex.Matches(sql, "CREATE (?:UNIQUE )?INDEX").Count;
        output.WriteLine($"Index statements: {indexes}; database round trips: {probe.Statements.Count}; elapsed with synthetic 5ms round-trip latency: {timer.ElapsedMilliseconds}ms.");
        Assert.True(guards >= 40);
        Assert.Equal(guards, indexes);
        Assert.Contains("IX_ai_usage_record_User_Time", sql);
        Assert.Contains("UX_ai_git_account_User_Provider_Username", sql);
        Assert.Contains("WHERE IsDeleted = 0", sql);
        Assert.Single(probe.Statements);
    }

    public class SchemaClientProbe : DispatchProxy
    {
        public object? Ado { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == "get_Ado" ? Ado : throw new NotSupportedException(method?.Name);
    }

    public class SchemaAdoProbe : DispatchProxy
    {
        public List<string> Statements { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "ExecuteCommand") throw new NotSupportedException(method?.Name);
            Statements.Add((string)args![0]!);
            Thread.Sleep(5);
            return 0;
        }
    }
}
