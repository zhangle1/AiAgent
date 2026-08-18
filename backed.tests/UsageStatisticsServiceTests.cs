using AiAgent.Backend.Entities.Usage;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Usage;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class UsageStatisticsServiceTests
{
    [Fact]
    public async Task SummaryAggregatesInDatabaseAndPreservesUtcDayBuckets()
    {
        using var db = CreateDatabase();
        var now = DateTime.UtcNow;
        db.Insertable(new[]
        {
            Record("user-1", "openai", "gpt-5", 10, 20, false, now.AddHours(-1)),
            Record("user-1", "openai", "gpt-5", 30, 40, true, now.AddDays(-1).Date.AddHours(23)),
            Record("user-2", "builtin", null, 5, 6, true, now.AddHours(-2))
        }).ExecuteCommand();

        var service = new UsageStatisticsService(db, NullLogger<UsageStatisticsService>.Instance);
        var result = await service.GetSummaryAsync(new AuthenticatedUser("user-1", "user"), "me", 7, CancellationToken.None);

        Assert.Equal(100, result.TotalTokens);
        Assert.Equal(2, result.TurnCount);
        Assert.Equal(1, result.EstimatedTurnCount);
        Assert.Equal(2, result.Providers.Count);
        Assert.Equal(7, result.Activity.Count);
        Assert.Equal(1, result.Activity[^1].TurnCount);
        Assert.Equal(1, result.Activity[^2].TurnCount);
    }

    [Fact]
    public async Task AdministratorAllScopeAndDayDetailUseSameProviderAggregates()
    {
        using var db = CreateDatabase();
        var day = DateTime.UtcNow.Date.AddDays(-1);
        db.Insertable(new[]
        {
            Record("user-1", "openai", "gpt-5", 10, 20, false, day.AddHours(1)),
            Record("user-2", "openai", "gpt-5", 30, 40, true, day.AddHours(2)),
            Record("user-2", "builtin", null, 5, 6, true, day.AddHours(3))
        }).ExecuteCommand();

        var service = new UsageStatisticsService(db, NullLogger<UsageStatisticsService>.Instance);
        var administrator = new AuthenticatedUser("admin", "admin", "admin");
        var summary = await service.GetSummaryAsync(administrator, "all", 7, CancellationToken.None);
        var detail = await service.GetDayDetailAsync(administrator, "all", day, CancellationToken.None);

        Assert.Equal(111, summary.TotalTokens);
        Assert.Equal(3, summary.TurnCount);
        Assert.Equal(2, summary.EstimatedTurnCount);
        Assert.Equal(summary.TotalTokens, detail.TotalTokens);
        Assert.Equal(2, detail.Providers.Count);
        Assert.Contains(detail.Providers, item => item.ProviderId == "openai" && item.TurnCount == 2 && item.TotalTokens == 100);
    }

    private static SqlSugarScope CreateDatabase()
    {
        var db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = "Data Source=:memory:",
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute
        });
        db.CodeFirst.InitTables<AiUsageRecord>();
        return db;
    }

    private static AiUsageRecord Record(string userId, string providerId, string? model, int prompt, int completion, bool estimated, DateTime createdAt) => new()
    {
        UserId = userId,
        ProviderKind = providerId == "builtin" ? "builtin" : "third_party",
        ProviderId = providerId,
        ModelId = model,
        ModelName = model,
        PromptTokens = prompt,
        CompletionTokens = completion,
        TotalTokens = prompt + completion,
        IsEstimated = estimated,
        CreatedAt = createdAt
    };
}
