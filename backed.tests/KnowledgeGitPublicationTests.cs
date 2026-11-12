using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using System.Text.Json;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Git;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task TeamGitGroupsSessionsByOwnedProjectAndMigratesLegacyCopiesWithoutRefreshingUnselectedText()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiGitAccount), typeof(AiChatSession), typeof(AiCodeProject));
        var root = Path.Combine(Path.GetTempPath(), "knowledge-project-sessions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["KnowledgeStorage:RootPath"] = root }).Build();
            var paths = new KnowledgePathService(config, NullLogger<KnowledgePathService>.Instance);
            var kb = CreateBase(db, "resource-store"); Directory.CreateDirectory(paths.GetRawPath(kb.Name));
            var source = Path.Combine(paths.GetRawPath(kb.Name), "session.md"); await File.WriteAllTextAsync(source, "latest original");
            var first = new AiCodeProject { Name = "mes-a", DisplayName = "MES标准库" };
            first.Id = db.Insertable(first).ExecuteReturnBigIdentity();
            var second = new AiCodeProject { Name = "mes-b", DisplayName = "MES标准库" };
            second.Id = db.Insertable(second).ExecuteReturnBigIdentity();
            var foreign = new AiCodeProject { Name = "secret", DisplayName = "不可泄露项目" };
            foreign.Id = db.Insertable(foreign).ExecuteReturnBigIdentity();
            string Digest(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("aiagent-platform\n" + id))).ToLowerInvariant();
            string UriFor(string id) => "viking://user/current/sessions/" + Digest(id) + ".md";
            foreach (var (id, projectId, owner) in new[] { ("one", (long?)first.Id, "current"), ("two", (long?)second.Id, "current"), ("unassigned", (long?)null, "current"), ("foreign", (long?)foreign.Id, "other") })
            {
                db.Insertable(new AiChatSession { Id = id, UserId = owner, CodeProjectId = projectId }).ExecuteCommand();
                db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = UriFor(id), OriginalFileName = id + ".md", Extension = ".md", StoragePath = source }).ExecuteCommand();
            }
            var settings = new KnowledgeCompilerSettings(db);
            using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var resources = new KnowledgeResourceService(db, paths, null!, worker, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
            var auth = DispatchProxy.Create<IAuthService, TeamAuth>(); ((TeamAuth)(object)auth).User = new("current", "current", "admin");
            var service = new KnowledgeTeamGitService(paths, config, resources, DispatchProxy.Create<IGitWorkspaceService, TeamGit>(), auth, accessor, db, new EphemeralDataProtectionProvider());
            await service.ConfigureAsync(new() { RemoteUrl = "https://example.invalid/team.git" }, default);
            var workspace = Path.Combine(root, "workspaces", "team-knowledge");
            var legacy = "sessions/current/" + Digest("one") + ".md";
            var otherLegacy = "sessions/other/archived.md";
            foreach (var file in new[] { legacy, otherLegacy }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(workspace, file))!); await File.WriteAllTextAsync(Path.Combine(workspace, file), "published snapshot"); }
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("published snapshot")));
            await File.WriteAllTextAsync(Path.Combine(workspace, "manifest.json"), JsonSerializer.Serialize(new[] {
                new { uri=UriFor("one"), path=legacy, name="old", kind="file", sha256=sha },
                new { uri="viking://user/other/sessions/archived.md", path=otherLegacy, name="old other", kind="file", sha256=sha }
            }));
            var firstPath = Path.Combine(workspace, "sessions", "current", "MES标准库-" + first.Id, Digest("one") + ".md");
            await service.RefreshAsync(new KnowledgeGitRefresh { ResourceUris = [] }, default);
            Assert.Equal("published snapshot", await File.ReadAllTextAsync(firstPath)); Assert.False(File.Exists(Path.Combine(workspace, legacy)));
            var otherPath = Path.Combine(workspace, "sessions", "other", "未分配项目", "archived.md");
            Assert.Equal("published snapshot", await File.ReadAllTextAsync(otherPath));
            await service.RefreshAsync(new KnowledgeGitRefresh { IncludeOwnSessions = true }, default);
            Assert.Equal("latest original", await File.ReadAllTextAsync(firstPath));
            Assert.True(File.Exists(Path.Combine(workspace, "sessions", "current", "MES标准库-" + second.Id, Digest("two") + ".md")));
            Assert.True(File.Exists(Path.Combine(workspace, "sessions", "current", "未分配项目", Digest("unassigned") + ".md")));
            Assert.True(File.Exists(Path.Combine(workspace, "sessions", "current", "未分配项目", Digest("foreign") + ".md")));
            var manifestPath = Path.Combine(workspace, "manifest.json");
            var manifest = await File.ReadAllTextAsync(manifestPath);
            var entries = JsonDocument.Parse(manifest).RootElement.EnumerateArray().ToArray();
            Assert.DoesNotContain(entries, e => e.GetProperty("project_name").GetString() == foreign.DisplayName);
            Assert.Equal(first.Id, entries.Single(e => e.GetProperty("uri").GetString() == UriFor("one")).GetProperty("project_id").GetInt64());
            Assert.Equal(4, Directory.GetFiles(Path.Combine(workspace, "sessions", "current"), "*.md", SearchOption.AllDirectories).Length);
            await service.RefreshAsync(new KnowledgeGitRefresh { IncludeOwnSessions = true }, default);
            Assert.Equal(manifest, await File.ReadAllTextAsync(manifestPath));
            db.Updateable<AiCodeProject>().SetColumns(p => p.DisplayName == "改名/项目").Where(p => p.Id == first.Id).ExecuteCommand();
            await service.RefreshAsync(new KnowledgeGitRefresh { IncludeOwnSessions = true }, default);
            Assert.False(File.Exists(firstPath));
            var renamed = Path.Combine(workspace, "sessions", "current", "改名_项目-" + first.Id, Digest("one") + ".md");
            Assert.Equal("latest original", await File.ReadAllTextAsync(renamed));
            await File.AppendAllTextAsync(otherPath, "tampered");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync(new KnowledgeGitRefresh { IncludeOwnSessions = true }, default));
            Assert.Equal("latest original", await File.ReadAllTextAsync(renamed));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AutomaticOwnSessionsIncludeBothSourcesUpdateDeleteAndNeverExportOtherPersonalData()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiGitAccount), typeof(AiChatSession), typeof(AiCodeProject));
        var root = Path.Combine(Path.GetTempPath(), "knowledge-auto-sessions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["KnowledgeStorage:RootPath"] = root }).Build();
            var paths = new KnowledgePathService(config, NullLogger<KnowledgePathService>.Instance);
            var kb = CreateBase(db, "resource-store");
            Directory.CreateDirectory(paths.GetRawPath(kb.Name));
            var source = Path.Combine(paths.GetRawPath(kb.Name), "session.md");
            await File.WriteAllTextAsync(source, "original session");
            void Add(string uri) => db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = uri, OriginalFileName = "会话.md", Extension = ".md", StoragePath = source }).ExecuteCommand();
            const string own = "viking://user/current/sessions/";
            Add(own + "one.md"); Add(own + "two.md");
            Add("viking://user/other/sessions/private.md"); Add("viking://user/current/memory/private.md");
            var settings = new KnowledgeCompilerSettings(db);
            using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var resources = new KnowledgeResourceService(db, paths, null!, worker, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
            var auth = DispatchProxy.Create<IAuthService, TeamAuth>();
            ((TeamAuth)(object)auth).User = new("current", "current", "admin");
            var service = new KnowledgeTeamGitService(paths, config, resources, DispatchProxy.Create<IGitWorkspaceService, TeamGit>(), auth, accessor, db, new EphemeralDataProtectionProvider());
            await service.ConfigureAsync(new() { RemoteUrl = "https://example.invalid/team.git" }, default);
            var workspace = Path.Combine(root, "workspaces", "team-knowledge"); Directory.CreateDirectory(workspace);
            var request = new KnowledgeGitRefresh { IncludeOwnSessions = true };
            await service.RefreshAsync(request, default);
            var sessions = Path.Combine(workspace, "sessions", "current", "未分配项目");
            Assert.Equal(2, Directory.GetFiles(sessions, "*.md").Length);
            var manifest = await File.ReadAllTextAsync(Path.Combine(workspace, "manifest.json"));
            var pathsInManifest = JsonDocument.Parse(manifest).RootElement.EnumerateArray().Select(e => e.GetProperty("path").GetString()).ToArray();
            Assert.Contains("sessions/current/未分配项目/one.md", pathsInManifest); Assert.Contains("sessions/current/未分配项目/two.md", pathsInManifest);
            Assert.DoesNotContain("private", manifest); Assert.False(Directory.Exists(Path.Combine(workspace, "sessions", "other")));
            await File.AppendAllTextAsync(source, " updated"); await service.RefreshAsync(request, default);
            Assert.Contains("updated", await File.ReadAllTextAsync(Path.Combine(sessions, "one.md")));
            // Automatic scope is server-resolved; the explicit 100-URI request limit does not truncate it.
            for (var index = 0; index < 101; index++) Add(own + "extra-" + index + ".md");
            await service.RefreshAsync(request, default);
            Assert.Equal(103, Directory.GetFiles(sessions, "*.md").Length);
            db.Updateable<AiKnowledgeDocument>().SetColumns(d => d.IsDeleted == true).Where(d => d.ResourceUri == own + "two.md").ExecuteCommand();
            await service.RefreshAsync(request, default);
            Assert.False(File.Exists(Path.Combine(sessions, "two.md")));
            var before = await File.ReadAllTextAsync(Path.Combine(workspace, "manifest.json"));
            await service.RefreshAsync(request, default);
            Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(workspace, "manifest.json")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshAsync(new KnowledgeGitRefresh { IncludeOwnSessions = true, SessionUris = ["viking://user/other/sessions/private.md"] }, default));
            Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TeamGitPublishesOnlyExplicitOwnSessionsAndKeepsPublishedCopyUntilReselected()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiGitAccount), typeof(AiChatSession), typeof(AiCodeProject));
        var root = Path.Combine(Path.GetTempPath(), "knowledge-publication-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["KnowledgeStorage:RootPath"] = root }).Build();
            var paths = new KnowledgePathService(config, NullLogger<KnowledgePathService>.Instance);
            var kb = CreateBase(db, "resource-store");
            Directory.CreateDirectory(paths.GetRawPath(kb.Name));
            var source = Path.Combine(paths.GetRawPath(kb.Name), "conversation.md");
            await File.WriteAllTextAsync(source, "# 工序派工\n\n## 用户\n请检查\n\n## AI\n原始回答");
            const string own = "viking://user/current/sessions/conversation.md";
            foreach (var uri in new[] { own, "viking://user/current/sessions/unselected.md", "viking://user/other/sessions/private.md", "viking://user/current/memory/private.md" })
                db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = uri, OriginalFileName = "工序派工.md", Extension = ".md", StoragePath = source }).ExecuteCommand();
            var a = Path.Combine(paths.GetRawPath(kb.Name), "a.md"); var b = Path.Combine(paths.GetRawPath(kb.Name), "b.md");
            await File.WriteAllTextAsync(a, "a1"); await File.WriteAllTextAsync(b, "b1");
            foreach (var (uri, path) in new[] { ("viking://resources/a.md", a), ("viking://resources/b.md", b) })
                db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = uri, OriginalFileName = Path.GetFileName(path), Extension = ".md", StoragePath = path }).ExecuteCommand();
            var settings = new KnowledgeCompilerSettings(db);
            using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var resources = new KnowledgeResourceService(db, paths, null!, worker, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
            var auth = DispatchProxy.Create<IAuthService, TeamAuth>();
            ((TeamAuth)(object)auth).User = new("current", "current", "admin");
            var service = new KnowledgeTeamGitService(paths, config, resources, DispatchProxy.Create<IGitWorkspaceService, TeamGit>(), auth, accessor, db, new EphemeralDataProtectionProvider());
            await service.ConfigureAsync(new() { RemoteUrl = "https://example.invalid/team.git" }, default);
            var options = (await service.StateAsync(default)).SessionOptions;
            Assert.Equal(2, options.Count); Assert.All(options, node => Assert.StartsWith("viking://user/current/sessions/", node.Uri));
            var workspace = Path.Combine(root, "workspaces", "team-knowledge");
            Directory.CreateDirectory(workspace); // The Git test double does not initialize a working directory.
            var target = Path.Combine(workspace, "sessions", "current", "未分配项目", "conversation.md");
            await service.RefreshAsync(default);
            Assert.False(Directory.Exists(Path.Combine(workspace, "sessions")));
            await File.WriteAllTextAsync(a, "a2"); await File.WriteAllTextAsync(b, "b2");
            await service.RefreshAsync(new KnowledgeGitRefresh { SessionUris = [own, own], ResourceUris = ["viking://resources/a.md"] }, default);
            Assert.Equal("a2", await File.ReadAllTextAsync(Path.Combine(workspace, "resources", "a.md")));
            Assert.Equal("b1", await File.ReadAllTextAsync(Path.Combine(workspace, "resources", "b.md")));
            var original = await File.ReadAllTextAsync(target);
            Assert.Contains("原始回答", original);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(workspace, "sessions"), "*", SearchOption.AllDirectories));
            var manifest = await File.ReadAllTextAsync(Path.Combine(workspace, "manifest.json"));
            Assert.Contains("sessions/current/未分配项目/conversation.md", JsonDocument.Parse(manifest).RootElement.EnumerateArray().Select(e => e.GetProperty("path").GetString())); Assert.DoesNotContain("unselected", manifest); Assert.DoesNotContain("private", manifest);
            await File.AppendAllTextAsync(source, "\n后续私人聊天");
            await service.RefreshAsync(default);
            Assert.Equal(original, await File.ReadAllTextAsync(target));
            await service.RefreshAsync(new KnowledgeGitRefresh { SessionUris = [own] }, default);
            Assert.Contains("后续私人聊天", await File.ReadAllTextAsync(target));
            foreach (var forbidden in new[] { "viking://user/other/sessions/private.md", "viking://user/current/memory/private.md", "viking://user/current/sessions/_parsed/section.md" })
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshAsync(new KnowledgeGitRefresh { SessionUris = [forbidden] }, default));
            var published = await File.ReadAllTextAsync(target);
            File.Delete(source);
            await Assert.ThrowsAnyAsync<Exception>(() => service.RefreshAsync(new KnowledgeGitRefresh { SessionUris = [own] }, default));
            Assert.Equal(published, await File.ReadAllTextAsync(target));
            db.Updateable<AiKnowledgeDocument>().SetColumns(d => d.IsDeleted == true).Where(d => d.ResourceUri == "viking://resources/b.md").ExecuteCommand();
            await service.RefreshAsync(new KnowledgeGitRefresh { ResourceUris = ["viking://resources/"] }, default);
            Assert.False(File.Exists(Path.Combine(workspace, "resources", "b.md")));
            Assert.Equal(published, await File.ReadAllTextAsync(target));
            Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList()); Assert.Empty(db.Queryable<AiKnowledgeParsedDocument>().ToList());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProjectDirectoriesIncludeEmptyAccessibleProjectsKeepUrisOnRenameAndHideRevokedOrDeletedProjects()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiUser), typeof(AiUserSession), typeof(AiUserCodeProject), typeof(AiCodeProject), typeof(AiKnowledgeDirectory));
        db.Insertable(new AiUser { Id = "owner", Username = "owner", Role = "user" }).ExecuteCommand();
        const string token = "synthetic-project-fixture";
        db.Insertable(new AiUserSession { UserId = "owner", TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), ExpiresAt = DateTime.UtcNow.AddHours(1) }).ExecuteCommand();
        var allowed = new AiCodeProject { Name = "mes", DisplayName = "MES标准库" };
        allowed.Id = db.Insertable(allowed).ExecuteReturnBigIdentity();
        var other = new AiCodeProject { Name = "other", DisplayName = "MES标准库" };
        other.Id = db.Insertable(other).ExecuteReturnBigIdentity();
        var deleted = new AiCodeProject { Name = "deleted", DisplayName = "已删除", IsDeleted = true };
        deleted.Id = db.Insertable(deleted).ExecuteReturnBigIdentity();
        db.Insertable(new AiUserCodeProject { UserId = "owner", CodeProjectId = allowed.Id }).ExecuteCommand();
        var kb = CreateBase(db, "resource-store");
        db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = KnowledgeProjectDirectoryService.UriFor(other.Id) + "hidden.md", OriginalFileName = "不可见.md" }).ExecuteCommand();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers.Authorization = "Bearer " + token;
        var resources = new KnowledgeResourceService(db, null!, null!, null!, null!, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!, new());
        var uri = KnowledgeProjectDirectoryService.UriFor(allowed.Id);
        Assert.Contains(resources.Tree(), n => n.Uri == uri && n.Name == "MES标准库" && n.ParentUri == "viking://projects/");
        Assert.DoesNotContain(resources.Tree(), n => n.Uri.StartsWith(KnowledgeProjectDirectoryService.UriFor(other.Id)));
        db.Updateable<AiCodeProject>().SetColumns(p => p.DisplayName == "MES新名称").Where(p => p.Id == allowed.Id).ExecuteCommand();
        Assert.Contains(resources.Tree(), n => n.Uri == uri && n.Name == "MES新名称");
        db.Deleteable<AiUserCodeProject>().Where(p => p.UserId == "owner").ExecuteCommand();
        Assert.DoesNotContain(resources.Tree(), n => n.Uri == uri);
        await Assert.ThrowsAsync<ArgumentException>(() => resources.CreateDirectoryAsync(new() { ParentUri = "viking://projects/", Name = "project-" + other.Id }, default));
        Assert.Empty(db.Queryable<AiKnowledgeDirectory>().ToList());
        db.Updateable<AiUser>().SetColumns(u => u.Role == "admin").Where(u => u.Id == "owner").ExecuteCommand();
        Assert.Contains(resources.Tree(), n => n.Uri == uri);
        Assert.Contains(resources.Tree(), n => n.Uri == KnowledgeProjectDirectoryService.UriFor(other.Id));
        Assert.DoesNotContain(resources.Tree(), n => n.Uri == KnowledgeProjectDirectoryService.UriFor(deleted.Id));
    }
}
