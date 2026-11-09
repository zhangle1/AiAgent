using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Auth;
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
    public async Task TeamGitPublishesOnlyExplicitOwnSessionsAndKeepsPublishedCopyUntilReselected()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiGitAccount));
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
            var target = Path.Combine(workspace, "sessions", "current", "conversation.md");
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
            Assert.Contains("sessions/current/conversation.md", manifest); Assert.DoesNotContain("unselected", manifest); Assert.DoesNotContain("private", manifest);
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
