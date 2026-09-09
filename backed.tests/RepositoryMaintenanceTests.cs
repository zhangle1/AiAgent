using System.Diagnostics;
using System.IO;
using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.CodeRepository;
using AiAgent.Backend.Services.Git;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class RepositoryMaintenanceTests
{
    private static readonly AuthenticatedUser Owner = new("owner", "owner", "user", true);
    private sealed class Access : IProjectAccessService
    {
        public bool CanAccess(AuthenticatedUser user, long projectId) => projectId == 1;
        public List<long> GetAccessibleProjectIds(AuthenticatedUser user) => [1];
    }
    private static SqlSugarScope Database()
    {
        var db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                // Production entities use SQL Server large-text types; this fixture uses SQLite.
                EntityService = (_, column) => { if (column.DataType == "nvarchar(max)") column.DataType = "text"; }
            }
        });
        db.CodeFirst.InitTables<AiRepositoryMaintenancePlan, AiRepositoryMaintenanceRun>();
        return db;
    }
    private static RepositoryMaintenanceService Service(ISqlSugarClient db) => new(db, new Access(), null!, null!, new(), new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build());

    [Fact]
    public void SavingSettingsTwicePersistsSettingsAndCannotResetActiveRun()
    {
        using var db = Database(); var service = Service(db);
        service.Save(Owner, 1, new());
        var run = service.Enqueue(Owner, 1);
        var updated = service.Save(Owner, 1, new() { Enabled = true, IntervalHours = 12 });
        Assert.Equal(run.Id, updated.ActiveRunId);
        Assert.Equal(12, updated.Settings.IntervalHours);
        Assert.NotNull(updated.NextRunAt);
        Assert.Throws<InvalidOperationException>(() => service.Enqueue(Owner, 1));
        Assert.Single(service.List(Owner, 1));
    }

    [Fact]
    public async Task CancelledQueuedRunNeverStartsAgentOrGitAndReleasesProject()
    {
        using var db = Database(); var service = Service(db);
        service.Save(Owner, 1, new());
        var run = service.Enqueue(Owner, 1);
        service.Cancel(Owner, 1, run.Id);
        await service.TickAsync(CancellationToken.None);
        Assert.Equal("cancelled", Assert.Single(service.List(Owner, 1)).Status);
        Assert.Null(service.Get(Owner, 1).ActiveRunId);
        Assert.Equal("queued", service.Enqueue(Owner, 1).Status);
    }

    [Fact]
    public async Task CorruptScheduledPlanDoesNotBlockQueuedCancellation()
    {
        using var db = Database(); var service = Service(db);
        db.CodeFirst.InitTables<AiAgent.Backend.Entities.Auth.AiUser>();
        db.Insertable(new AiAgent.Backend.Entities.Auth.AiUser { Id = Owner.Id, Username = Owner.Username, CanCommitCode = true }).ExecuteCommand();
        db.Insertable(new AiRepositoryMaintenancePlan
        {
            ProjectId = 1, UserId = Owner.Id, Enabled = true, SettingsJson = "{",
            NextRunAt = DateTime.UtcNow.AddHours(-1), UpdatedAt = DateTime.UtcNow
        }).ExecuteCommand();
        var queued = new AiRepositoryMaintenanceRun { ProjectId = 1, Status = "queued", CancelRequested = true, CreatedAt = DateTime.UtcNow };
        db.Insertable(queued).ExecuteCommand();
        await service.TickAsync(CancellationToken.None);
        Assert.False(db.Queryable<AiRepositoryMaintenancePlan>().InSingle(1).Enabled);
        Assert.Equal("cancelled", db.Queryable<AiRepositoryMaintenanceRun>().InSingle(queued.Id).Status);
    }

    [Fact]
    public async Task CorruptQueuedSettingsFailAndReleaseProject()
    {
        using var db = Database(); var service = Service(db);
        service.Save(Owner, 1, new());
        var run = service.Enqueue(Owner, 1);
        db.Updateable<AiRepositoryMaintenanceRun>().SetColumns(x => x.SettingsJson == "{")
            .Where(x => x.Id == run.Id).ExecuteCommand();
        await service.TickAsync(CancellationToken.None);
        Assert.Equal("failed", Assert.Single(service.List(Owner, 1)).Status);
        Assert.Null(service.Get(Owner, 1).ActiveRunId);
        Assert.Equal("queued", service.Enqueue(Owner, 1).Status);
    }

    [Fact]
    public void PushQueueClaimsProjectAndRejectsDuplicateOrConcurrentRun()
    {
        using var db = Database(); var service = Service(db);
        service.Save(Owner, 1, new());
        var ready = new AiRepositoryMaintenanceRun { ProjectId = 1, UserId = Owner.Id, Status = "ready", StartedAt = DateTime.UtcNow.AddDays(-1), FinishedAt = DateTime.UtcNow };
        db.Insertable(ready).ExecuteCommand();
        Assert.Equal("push_queued", service.RequestPush(Owner, 1, ready.Id).Status);
        Assert.Equal(ready.Id, service.Get(Owner, 1).ActiveRunId);
        Assert.Null(db.Queryable<AiRepositoryMaintenanceRun>().InSingle(ready.Id).StartedAt);
        Assert.Throws<InvalidOperationException>(() => service.RequestPush(Owner, 1, ready.Id));
        Assert.Throws<InvalidOperationException>(() => service.Enqueue(Owner, 1));
    }

    [Fact]
    public void AccessAndCommitPermissionAreRequiredForMutations()
    {
        using var db = Database(); var service = Service(db);
        Assert.Throws<UnauthorizedAccessException>(() => service.Get(Owner, 2));
        Assert.Throws<UnauthorizedAccessException>(() => service.Save(new("reader", "reader"), 1, new()));
        Assert.Throws<ArgumentException>(() => service.Save(Owner, 1, new() { AutoPush = true }));
    }

    [Theory]
    [InlineData("analyze")]
    [InlineData("optimize")]
    public void AutomaticDeliveryIsRejectedWithoutChangingSavedPlan(string mode)
    {
        using var db = Database(); var service = Service(db);
        service.Save(Owner, 1, new());
        Assert.Throws<ArgumentException>(() => service.Save(Owner, 1, new() { Mode = mode, AutoPush = true }));
        Assert.False(service.Get(Owner, 1).Settings.AutoPush);
        Assert.Equal("analyze", service.Get(Owner, 1).Settings.Mode);
    }

    [Fact]
    public void LegacyAutomaticDeliveryPlanCannotBeEnqueued()
    {
        using var db = Database(); var service = Service(db);
        service.Save(Owner, 1, new());
        var legacy = System.Text.Json.JsonSerializer.Serialize(new MaintenanceSettings { Mode = "optimize", AutoPush = true });
        db.Updateable<AiRepositoryMaintenancePlan>().SetColumns(x => x.SettingsJson == legacy).Where(x => x.ProjectId == 1).ExecuteCommand();
        Assert.Throws<ArgumentException>(() => service.Enqueue(Owner, 1));
        Assert.Empty(service.List(Owner, 1));
        Assert.Null(service.Get(Owner, 1).ActiveRunId);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/../../outside")]
    public void PathTraversalIsRejected(string path)
    {
        Assert.Throws<InvalidOperationException>(() => RepositoryMaintenanceBuildService.SafePath(Path.GetTempPath(), path));
    }

    [Theory]
    [InlineData("front/.env.production")]
    [InlineData("backed/appsettings.json")]
    [InlineData("front/node_modules/package/index.js")]
    [InlineData("private/key.pem")]
    public void ProtectedFilesCannotBeDelivered(string path) => Assert.True(RepositoryMaintenanceBuildService.IsForbiddenChange(path));

    [Fact]
    public async Task GitDeliveryRejectsChangedSnapshotThenPushesOnlyMaintenanceBranch()
    {
        // This test uses a disposable local bare remote, never a configured user repository.
        var root = Path.Combine(Path.GetTempPath(), "aiagent-maintenance-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var remote = Path.Combine(root, "remote.git");
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            await Git(root, "init", "--bare", remote);
            await Git(root, "init", "-b", "main", source);
            await Git(source, "config", "user.name", "Test");
            await Git(source, "config", "user.email", "test@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(source, "code.txt"), "baseline\n");
            await Git(source, "add", "code.txt"); await Git(source, "commit", "-m", "initial");
            await Git(source, "remote", "add", "origin", remote);
            await Git(source, "push", "-u", "origin", "main");
            var service = new GitWorkspaceService();
            var baseline = await service.PrepareMaintenanceAsync(source, checkout, "maintenance/test", CancellationToken.None);
            var file = Path.Combine(checkout, "code.txt");
            await File.WriteAllTextAsync(file, "improved\n");
            var validation = await service.ValidateDeliveryAsync("maintenance:" + checkout, checkout, CancellationToken.None);
            Assert.Equal("code.txt", Assert.Single(validation.Files));
            Assert.True(validation.IsValid, string.Join(";", validation.Checks.Where(x => !x.Passed).Select(x => x.Message)));
            await File.WriteAllTextAsync(file, "changed after build\n");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PushMaintenanceAsync(checkout, "maintenance/test", baseline, validation.SnapshotSha256, "maintenance", CancellationToken.None));
            Assert.Equal(baseline, (await Git(checkout, "rev-parse", "HEAD")).Trim());
            await File.WriteAllTextAsync(file, "improved\n");
            var result = await service.PushMaintenanceAsync(checkout, "maintenance/test", baseline, validation.SnapshotSha256, "maintenance", CancellationToken.None);
            Assert.True(result.Ok, result.Output);
            Assert.Equal(baseline, (await Git(remote, "rev-parse", "refs/heads/main")).Trim());
            Assert.Equal(result.CommitSha, (await Git(remote, "rev-parse", "refs/heads/maintenance/test")).Trim());
            Assert.Equal("baseline\n", await File.ReadAllTextAsync(Path.Combine(source, "code.txt")));
        }
        finally
        {
            // Git object files may be read-only on Windows; normalize only this test's unique directory.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static async Task<string> Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
