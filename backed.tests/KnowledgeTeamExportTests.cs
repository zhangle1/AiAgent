using System.IO;
using System.Reflection;
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
    public class TeamAuth : DispatchProxy
    {
        public AuthenticatedUser User = new("fixture", "fixture", "admin");
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IAuthService.TryGetCurrentUserAsync) ? Task.FromResult<AuthenticatedUser?>(User) : throw new NotSupportedException();
    }
    public class TeamGit : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch {
            nameof(IGitWorkspaceService.ConfigureTeamAsync) => Task.CompletedTask,
            nameof(IGitWorkspaceService.ValidateTeamAsync) => Task.FromResult(new GitDeliveryValidation()),
            _ => throw new NotSupportedException()
        };
    }

    [Fact]
    public async Task TeamExportExcludesPersonalSourcesKeepsPreviousCopyOnFailureAndRequiresAdmin()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiGitAccount));
        var root = Path.Combine(Path.GetTempPath(), "team-export-tests-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspaces", "team-knowledge"); Directory.CreateDirectory(workspace);
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["KnowledgeStorage:RootPath"] = root }).Build();
            var paths = new KnowledgePathService(config, NullLogger<KnowledgePathService>.Instance);
            var kb = CreateBase(db, "resource-store"); Directory.CreateDirectory(paths.GetRawPath(kb.Name));
            var source = Path.Combine(paths.GetRawPath(kb.Name), "guide.md"); await File.WriteAllTextAsync(source, "team guide");
            foreach (var uri in new[] { "viking://projects/订单系统/需求/guide.md", "viking://user/current/private.md", "viking://user/other/secret.md" })
                db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "guide.md", ResourceUri = uri, StoragePath = source, Extension = ".md" }).ExecuteCommand();
            var settings = new KnowledgeCompilerSettings(db);
            using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var resources = new KnowledgeResourceService(db, paths, null!, worker, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
            var auth = DispatchProxy.Create<IAuthService, TeamAuth>();
            var service = new KnowledgeTeamGitService(paths, config, resources, DispatchProxy.Create<IGitWorkspaceService, TeamGit>(), auth, accessor, db, new EphemeralDataProtectionProvider());
            await service.ConfigureAsync(new() { RemoteUrl = "https://example.invalid/team.git" }, default);
            await service.RefreshAsync(default);
            var projected = Path.Combine(workspace, "projects", "订单系统", "需求", "guide.md");
            Assert.Equal("team guide", await File.ReadAllTextAsync(projected));
            var manifest = await File.ReadAllTextAsync(Path.Combine(workspace, "manifest.json"));
            Assert.DoesNotContain("private", manifest); Assert.DoesNotContain("secret", manifest);
            Assert.False(Directory.Exists(Path.Combine(workspace, "user")));
            File.Delete(source);
            await Assert.ThrowsAnyAsync<Exception>(() => service.RefreshAsync(default));
            Assert.Equal("team guide", await File.ReadAllTextAsync(projected));
            Assert.Empty(Directory.EnumerateDirectories(root, ".team-export-*"));
            ((TeamAuth)(object)auth).User = new("fixture", "fixture", "user");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshAsync(default));
        }
        finally { Directory.Delete(root, true); }
    }
}
