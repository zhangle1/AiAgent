using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Usage;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class AdminUsageReportTests
{
    [Fact]
    public async Task ReportFiltersOwnersProjectsAndInclusiveDatesAndDeduplicatesSessions()
    {
        using var db = Database();
        db.Insertable(new[] {
            new AiChatSession { Id = "s1", UserId = "u1", CodeProjectId = 1 },
            new AiChatSession { Id = "s2", UserId = "u2", CodeProjectId = 2, IsDeleted = true }
        }).ExecuteCommand();
        db.Insertable(new[] {
            Row("u1", "s1", "2026-01-01"), Row("u1", "s1", "2026-01-02"),
            Row("u2", "s2", "2026-01-02"), Row("u2", "s1", "2026-01-02"),
            Row("u1", null, "2026-01-02"), Row("u1", "s1", "2026-01-04")
        }).ExecuteCommand();
        var service = new AdminService(db, null!);
        var admin = new AuthenticatedUser("admin", "admin", "admin");
        var report = await service.GetUsageReportAsync(admin, "day", 30, null, default, "2026-01-01", "2026-01-03");
        Assert.Equal(5, report.TurnCount);
        Assert.Equal(3, report.Metrics.SessionCount); // owner + session, including an orphan
        Assert.Equal(2, report.Metrics.ActiveDays);
        Assert.Equal(1.67, report.Metrics.DailyTurns);
        Assert.Equal(3, report.Buckets.Count);
        Assert.Equal(0, report.Buckets[2].TurnCount);
        Assert.Equal(DateTimeKind.Utc, report.From.Kind);
        var project = await service.GetUsageReportAsync(admin, "month", 30, "u1", default, "2026-01-01", "2026-01-03", 1);
        Assert.Equal(2, project.TurnCount);
        Assert.Equal(1, project.Metrics.SessionCount);
        Assert.Equal(0.67, project.Buckets.Single().Metrics.DailyTurns);
        Assert.Single(project.UserProjects);
        var orphan = await service.GetUsageReportAsync(admin, "day", 30, null, default, "2026-01-01", "2026-01-03", 0);
        Assert.Equal(2, orphan.TurnCount);
        Assert.Equal(1, orphan.Metrics.SessionCount);
        var deleted = await service.GetUsageReportAsync(admin, "day", 30, null, default, "2026-01-01", "2026-01-03", 2);
        Assert.Equal(1, deleted.TurnCount);
    }

    [Fact]
    public async Task ReportRejectsNonAdministratorsAndInvalidRanges()
    {
        using var db = Database();
        var service = new AdminService(db, null!);
        var admin = new AuthenticatedUser("admin", "admin", "admin");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetUsageReportAsync(new("user", "user"), "day", 30, null, default));
        foreach (var dates in new[] { ("2026-02-30", "2026-03-01"), ("2026-02-02", "2026-02-01"), ("2026-02-01", (string?)null), ("2000-01-01", "2026-01-01") })
            await Assert.ThrowsAsync<ArgumentException>(() => service.GetUsageReportAsync(admin, "day", 30, null, default, dates.Item1, dates.Item2));
        var empty = await service.GetUsageReportAsync(admin, "month", 30, null, default, "2026-01-31", "2026-02-01");
        Assert.Equal(2, empty.Buckets.Count);
        Assert.Equal(0, empty.Metrics.DailyTurns);
    }

    private static AiUsageRecord Row(string user, string? session, string date) => new() {
        UserId = user, SessionId = session, CreatedAt = DateTime.Parse(date).AddHours(23), TotalTokens = 10
    };
    private static SqlSugarScope Database()
    {
        var db = new SqlSugarScope(new ConnectionConfig { DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new() { EntityService = (_, column) => {
                if (column.IsIdentity) column.DataType = "integer";
                if (column.DataType == "nvarchar(max)") column.DataType = "text";
            } } });
        db.CodeFirst.InitTables<AiUsageRecord, AiChatSession, AiUser, AiCodeProject>();
        return db;
    }
}
