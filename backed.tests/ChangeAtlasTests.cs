using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Dtos.ChangeAtlas;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.ChangeAtlas;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Git;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using Xunit;

namespace AiAgent.Backend.Tests;

public sealed class ChangeAtlasTests
{
    private static readonly AuthenticatedUser Owner = new("atlas-owner", "owner");
    public class AtlasAuth : DispatchProxy
    {
        public AuthenticatedUser? User = Owner;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IAuthService.TryGetCurrentUserAsync)
            ? Task.FromResult(User) : throw new NotSupportedException();
    }
    private sealed class Access : IProjectAccessService
    {
        public bool Enabled = true;
        public bool CanAccess(AuthenticatedUser user, long projectId) => Enabled && projectId == 1;
        public List<long> GetAccessibleProjectIds(AuthenticatedUser user) => Enabled ? [1] : [];
    }
    private sealed class Model : ICodexChatService
    {
        public int Calls;
        public ChatCompleteRequest? Last;
        public bool InvalidCitation;
        public bool AnswerOnly;
        public Action? AfterCall;
        public Task<CodexQuotaResponse> ReadQuotaAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task HeartbeatAsync(AuthenticatedUser user, CodexRuntimeHeartbeatRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, AiAgent.Backend.Services.Chat.Agentic.AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
        {
            Calls++; Last = request;
            var start = request.Message.IndexOf("<untrusted_evidence_json>\n", StringComparison.Ordinal) + "<untrusted_evidence_json>\n".Length;
            var end = request.Message.LastIndexOf("\n</untrusted_evidence_json>", StringComparison.Ordinal);
            using var bundle = JsonDocument.Parse(request.Message[start..end]);
            var id = bundle.RootElement.GetProperty("evidence")[0].GetProperty("id").GetString();
            AfterCall?.Invoke();
            var content = JsonSerializer.Serialize(new { summary = "数量计算发生变化", module_label = "领料", claims = new[] { new { kind = "inference", text = "建议核对数量换算", evidence_ids = new[] { InvalidCitation ? "invented" : id } } }, checks = new[] { "验证小数比例" }, unknowns = Array.Empty<string>() });
            return Task.FromResult(new ChatCompleteResponse { Model = "test-model", Content = AnswerOnly ? "" : content, Answer = content });
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "aiagent-atlas-tests-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public SqlSugarScope Db { get; }
        public Access Access { get; } = new();
        public ChangeAtlasGitReader Git { get; } = new();
        public ChangeAtlasStore Store { get; }
        public ChangeAtlasService Service { get; }
        public long RepositoryId { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Source);
            Db = new SqlSugarScope(new ConnectionConfig
            {
                DbType = DbType.Sqlite, ConnectionString = "Data Source=" + Path.Combine(Root, "fixture.sqlite") + ";Pooling=False", IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new() { EntityService = (_, column) => { if (column.DataType == "nvarchar(max)") column.DataType = "text"; if (column.IsPrimarykey && column.IsIdentity) column.DataType = "INTEGER"; } }
            });
            Db.CodeFirst.InitTables<AiCodeRepository, AiCodeProject, AiGitAccount>();
            RepositoryId = Db.Insertable(new AiCodeRepository { Name = "atlas-test", DisplayName = "测试仓库", ProjectId = 1, RootPath = Source }).ExecuteReturnBigIdentity();
            Store = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = Path.Combine(Root, "store") }).Build());
            Service = new(Db, Access, Git, Store, new EphemeralDataProtectionProvider());
        }
        public async Task Initialize()
        {
            await Command(Source, "init", "-b", "main");
            Directory.CreateDirectory(Path.Combine(Source, "Services", "Material"));
            await File.WriteAllTextAsync(Path.Combine(Source, "Services", "Material", "换算.cs"), "class Material { int Qty = 1; }\n");
            await Commit(Source, "初始换算");
            await File.WriteAllTextAsync(Path.Combine(Source, "Services", "Material", "换算.cs"), "class Material { int Qty = 2; }\n");
            await Commit(Source, "修正换算", "陈工", "chen@example.invalid");
        }
        public void Dispose()
        {
            Db.Dispose();
            // All paths are generated inside this fixture's private temporary root.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }

    [Fact]
    public async Task ImportsUsePinnedObjectsAndRecheckAccess()
    {
        using var f = new Fixture(); await f.Initialize();
        await File.WriteAllTextAsync(Path.Combine(f.Source, "a.ts"), "import {b} from './b';\n");
        await File.WriteAllTextAsync(Path.Combine(f.Source, "b.ts"), "export const b = 1;\n");
        await Commit(f.Source, "合成导入夹具");
        var snapshot = await f.Service.CreateAsync(Owner, 1, new AtlasCreateRequest { Days = 7, Ref = "HEAD" }, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(f.Source, "a.ts"), "import './missing';\n");
        var graph = await f.Service.ImportsAsync(Owner, 1, snapshot.Id, f.RepositoryId, CancellationToken.None);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("import {b} from './b';", edge.Text);
        Assert.Equal("a.ts", graph.Files.Single(x => x.Id == edge.Source).Path);
        Assert.Equal("b.ts", graph.Files.Single(x => x.Id == edge.Target).Path);
        Assert.Null(await f.Git.SourceAsync(f.Store.Cache(Owner.Id, f.RepositoryId), graph.HeadSha, "missing.ts", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.ImportsAsync(Owner, 1, snapshot.Id, -1, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.ImportsAsync(new("other", "other"), 1, snapshot.Id, f.RepositoryId, CancellationToken.None));
        f.Access.Enabled = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ImportsAsync(Owner, 1, snapshot.Id, f.RepositoryId, CancellationToken.None));
    }

    [Fact]
    public async Task SnapshotAndEvidenceStayPinnedAndNeverChangeDirtyWorktree()
    {
        using var f = new Fixture(); await f.Initialize();
        var path = Path.Combine(f.Source, "Services", "Material", "换算.cs");
        await File.WriteAllTextAsync(path, "uncommitted must remain");
        await File.WriteAllTextAsync(Path.Combine(f.Source, "untracked.txt"), "keep");
        var before = await Command(f.Source, "status", "--porcelain");
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        Assert.Equal("directory", snapshot.Coverage);
        Assert.Equal(2, snapshot.Commits.Count);
        Assert.Equal(before, await Command(f.Source, "status", "--porcelain"));
        Assert.Equal("uncommitted must remain", await File.ReadAllTextAsync(path));
        Assert.DoesNotContain(snapshot.Files, file => file.Path == "untracked.txt");
        var commit = snapshot.Commits.First(x => x.Subject == "修正换算");
        var file = Assert.Single(snapshot.Files);
        var evidence = await f.Service.EvidenceAsync(Owner, 1, snapshot.Id, commit.Id, file.Id, CancellationToken.None);
        Assert.Contains("Qty = 2", evidence.Patch);
        Assert.DoesNotContain("uncommitted", evidence.Patch);
        await File.WriteAllTextAsync(path, "class Material { int Qty = 3; }\n");
        await Commit(f.Source, "再次修正");
        var next = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        Assert.NotEqual(snapshot.Repositories[0].HeadSha, next.Repositories[0].HeadSha);
        var old = await f.Service.EvidenceAsync(Owner, 1, snapshot.Id, commit.Id, file.Id, CancellationToken.None);
        Assert.Equal(evidence.Patch, old.Patch);
    }

    [Fact]
    public async Task RemoteRefreshFindsExternalPushAndForcePushWithoutTouchingSource()
    {
        using var f = new Fixture(); await f.Initialize();
        var remote = Path.Combine(f.Root, "remote.git");
        await Command(f.Root, "init", "--bare", remote);
        await Command(f.Source, "remote", "add", "origin", remote);
        await Command(f.Source, "push", "-u", "origin", "main");
        await Command(remote, "symbolic-ref", "HEAD", "refs/heads/main");
        var external = Path.Combine(f.Root, "external");
        await Command(f.Root, "clone", remote, external);
        var baseline = (await Command(f.Source, "rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(external, "external.cs"), "class External {}\n");
        await Commit(external, "外部推送"); await Command(external, "push");
        await File.WriteAllTextAsync(Path.Combine(f.Source, "untracked.txt"), "keep");
        var status = await Command(f.Source, "status", "--porcelain");
        var first = await f.Git.CaptureAsync(f.Source, f.Store.Cache(Owner.Id, f.RepositoryId), Guid.NewGuid().ToString("N"), "HEAD", true, DateTime.UtcNow.Date.AddDays(-6), null, CancellationToken.None);
        Assert.Equal("remote", first.Source); Assert.Equal("refs/remotes/origin/main", first.Ref);
        Assert.Contains(first.Commits, commit => commit.Subject == "外部推送");
        Assert.Equal(baseline, (await Command(f.Source, "rev-parse", "HEAD")).Trim());
        Assert.Equal(status, await Command(f.Source, "status", "--porcelain"));
        // Force-push only to this fixture's synthetic local remote.
        await Command(external, "push", "--force", "origin", baseline + ":refs/heads/main");
        var second = await f.Git.CaptureAsync(f.Source, f.Store.Cache(Owner.Id, f.RepositoryId), Guid.NewGuid().ToString("N"), "HEAD", true, DateTime.UtcNow.Date.AddDays(-6), new("fixture-user", "synthetic-%&!-token"), CancellationToken.None);
        Assert.Equal(baseline, second.Head);
        Assert.Empty(Directory.EnumerateFiles(f.Store.Cache(Owner.Id, f.RepositoryId), "askpass-*"));
        var introduced = first.Commits.First(x => x.Subject == "外部推送");
        var preserved = await f.Git.PatchAsync(f.Store.Cache(Owner.Id, f.RepositoryId), introduced.Sha, introduced.Parent, "external.cs", CancellationToken.None);
        Assert.Contains("class External", preserved.Patch);
    }

    [Fact]
    public async Task EmptyRepositoryAndRemoteFailureHaveHonestCoverage()
    {
        using var f = new Fixture(); await Command(f.Source, "init", "-b", "main");
        var empty = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        Assert.Empty(empty.Commits); Assert.Contains(empty.Warnings, warning => warning.Contains("还没有提交"));
        await f.Initialize();
        await Command(f.Source, "remote", "add", "origin", Path.Combine(f.Root, "missing.git"));
        var local = await f.Service.CreateAsync(Owner, 1, new() { RefreshRemote = true }, CancellationToken.None);
        Assert.Equal("local", Assert.Single(local.Repositories).Source);
        Assert.Contains(local.Warnings, warning => warning.Contains("远端同步失败"));
        Assert.DoesNotContain(local.Warnings, warning => warning.Contains(f.Root));
    }

    [Fact]
    public async Task SnapshotOwnershipProjectAccessAndRepositoryMembershipAreRechecked()
    {
        using var f = new Fixture(); await f.Initialize();
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(new("other", "other"), 1, snapshot.Id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(Owner, 2, snapshot.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.GetAsync(Owner, 1, "../../other", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CreateAsync(Owner, 1, new() { RepositoryId = 99999 }, CancellationToken.None));
        f.Db.Updateable<AiCodeRepository>().SetColumns(x => x.ProjectId == 2).Where(x => x.Id == f.RepositoryId).ExecuteCommand();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(Owner, 1, snapshot.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SecretContentAndArbitraryEvidenceAreNotRead()
    {
        using var f = new Fixture(); await f.Initialize();
        await File.WriteAllTextAsync(Path.Combine(f.Source, ".env.test"), "TOKEN=synthetic-fixture-value\n");
        await Commit(f.Source, "加入合成配置");
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        var secret = snapshot.Files.First(x => x.Path == ".env.test");
        var commit = snapshot.Commits.First(x => x.Subject == "加入合成配置");
        var evidence = await f.Service.EvidenceAsync(Owner, 1, snapshot.Id, commit.Id, secret.Id, CancellationToken.None);
        Assert.True(evidence.Suppressed); Assert.DoesNotContain("synthetic-fixture-value", evidence.Patch);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.EvidenceAsync(Owner, 1, snapshot.Id, commit.Id, "../config", CancellationToken.None));
        var unrelated = snapshot.Commits.First(x => x.Subject == "初始换算");
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.EvidenceAsync(Owner, 1, snapshot.Id, unrelated.Id, secret.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AiUsesBoundedIndependentReadOnlyContextCachesAndRejectsInventedCitations()
    {
        using var f = new Fixture(); await f.Initialize();
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        var model = new Model(); var service = new ChangeAtlasAnalysisService(f.Service, f.Store, model);
        var request = new AtlasAnalysisRequest { ModuleId = snapshot.Modules[0].Id, Until = snapshot.CreatedAt.ToString("yyyy-MM-dd") };
        var result = await service.AnalyzeAsync(Owner, 1, snapshot.Id, request, CancellationToken.None);
        Assert.Equal("test-model", result.Model); Assert.Single(result.Claims);
        Assert.Equal("read-only", model.Last!.CodexSandboxMode); Assert.Equal(Owner.Id, model.Last.RuntimeUserId);
        Assert.StartsWith("atlas-", model.Last.SessionId); Assert.NotEqual(f.Source, model.Last.MaintenanceWorkspacePath);
        Assert.Null(model.Last.CodeProjectId); Assert.DoesNotContain(f.Source, model.Last.Message);
        await service.AnalyzeAsync(Owner, 1, snapshot.Id, request, CancellationToken.None); Assert.Equal(1, model.Calls);
        request.Question = "新的问题"; model.InvalidCitation = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(Owner, 1, snapshot.Id, request, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(Owner, 1, snapshot.Id, request, CancellationToken.None));
        Assert.Equal(3, model.Calls);
    }

    [Fact]
    public async Task StreamPublishesOnlyValidatedResultsAndReturnsErrorsAsEvents()
    {
        using var f = new Fixture(); await f.Initialize();
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        var model = new Model();
        var auth = DispatchProxy.Create<IAuthService, AtlasAuth>();
        var context = new DefaultHttpContext();
        using var output = new MemoryStream(); context.Response.Body = output;
        var api = new ChangeAtlasAppService(f.Service, new(f.Service, f.Store, model), auth, new HttpContextAccessor { HttpContext = context });
        var request = new AtlasAnalysisRequest { ModuleId = snapshot.Modules[0].Id };
        await api.AnalyzeStream(1, snapshot.Id, request, CancellationToken.None);
        var success = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.StartsWith("text/event-stream", context.Response.ContentType);
        Assert.Contains("event: status", success); Assert.Contains("event: result", success); Assert.DoesNotContain("event: error", success);
        output.SetLength(0); request.Question = "引用校验失败"; model.InvalidCitation = true;
        await api.AnalyzeStream(1, snapshot.Id, request, CancellationToken.None);
        var failure = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("event: error", failure); Assert.DoesNotContain("event: result", failure);
        output.SetLength(0); ((AtlasAuth)auth).User = null;
        await api.AnalyzeStream(1, snapshot.Id, request, CancellationToken.None);
        Assert.Equal(401, context.Response.StatusCode); Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task AiReusesRuntimeButNotConversationAndAcceptsAnswerField()
    {
        using var f = new Fixture(); await f.Initialize();
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        var model = new Model { AnswerOnly = true };
        var service = new ChangeAtlasAnalysisService(f.Service, f.Store, model);
        var request = new AtlasAnalysisRequest { ModuleId = snapshot.Modules[0].Id };
        var statuses = new List<string>();
        await service.AnalyzeAsync(Owner, 1, snapshot.Id, request, CancellationToken.None, status => { statuses.Add(status); return Task.CompletedTask; });
        var first = model.Last!;
        request.Question = "从开发视角说明";
        await service.AnalyzeAsync(Owner, 1, snapshot.Id, request, CancellationToken.None);
        Assert.Equal(first.ClientRuntimeId, model.Last!.ClientRuntimeId);
        Assert.NotEqual(first.SessionId, model.Last.SessionId);
        Assert.Equal(3, statuses.Count);
    }

    [Fact]
    public async Task AccessRevocationDuringAiCallPreventsPublishingResult()
    {
        using var f = new Fixture(); await f.Initialize();
        var snapshot = await f.Service.CreateAsync(Owner, 1, new(), CancellationToken.None);
        var model = new Model { AfterCall = () => f.Access.Enabled = false };
        var service = new ChangeAtlasAnalysisService(f.Service, f.Store, model);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AnalyzeAsync(Owner, 1, snapshot.Id, new() { ModuleId = snapshot.Modules[0].Id }, CancellationToken.None));
    }

    [Fact]
    public void FiltersKeepSnapshotStructureAndDoNotInventPersonnelIdentity()
    {
        var snapshot = new AtlasSnapshot { CreatedAt = new DateTime(2026, 11, 2), StartAt = new DateTime(2026, 11, 1), Modules = [new("m", 1, "Material", "Services/Material")], Files = [new("f", 1, "m", "Services/Material/a.cs", true)] };
        snapshot.Commits = [new("c1", 1, "a", null, false, "p1", "署名一", new(2026, 11, 1), "first", [new("f", 1, 0)], false), new("c2", 1, "b", "a", false, "p2", "署名二", new(2026, 11, 2), "second", [new("f", 1, 1)], false)];
        Assert.Equal("c1", Assert.Single(ChangeAtlasService.SelectCommits(snapshot, new() { ModuleId = "m", AuthorId = "p1", Until = "2026-11-02" })).Id);
        Assert.Equal("c2", Assert.Single(ChangeAtlasService.SelectCommits(snapshot, new() { ModuleId = "m", Daily = true, Until = "2026-11-02" })).Id);
        Assert.Single(snapshot.Files); Assert.Equal(2, snapshot.Commits.Count);
        Assert.Throws<ArgumentException>(() => ChangeAtlasService.SelectCommits(snapshot, new() { ModuleId = "m", Until = "2026-11-03" }));
        Assert.Equal("backed/Services/Git", ChangeAtlasService.ModuleDirectory("backed/Services/Git/A.cs"));
        var stats = ChangeAtlasGitReader.ParseNumstat("-\t-\t二进制.bin\0" + "2\t1\t目录/有空格 a.cs\0");
        Assert.Null(stats[0].Added); Assert.Equal("目录/有空格 a.cs", stats[1].Path);
        Assert.True(ChangeAtlasService.Sensitive("config/.npmrc"));
        Assert.True(ChangeAtlasService.Sensitive("private/id_ecdsa"));
        Assert.True(ChangeAtlasService.Sensitive("secrets/service.json"));
        Assert.False(ChangeAtlasService.Sensitive("Services/Material/a.cs"));
    }

    private static Task<string> Commit(string directory, string message, string author = "Test", string email = "test@example.invalid") => CommitCore(directory, message, author, email);
    private static async Task<string> CommitCore(string directory, string message, string author, string email)
    {
        await Command(directory, "add", "--all");
        return await Command(directory, "-c", "user.name=" + author, "-c", "user.email=" + email, "-c", "commit.gpgsign=false", "commit", "-m", message);
    }
    private static async Task<string> Command(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
