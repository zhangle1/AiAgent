using AiAgent.Backend.Dtos.KunBuddy;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.KunBuddy;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AiAgent.Backend.Dtos.Knowledge;
using System.Text;
using System.IO;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task KunBuddySessionStoreIsHiddenAndUnavailableThroughSharedKnowledgeSurfaces()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var service = new KunBuddySessionService(db, files.Paths);
        await service.UploadAsync(new("alice-id", "alice"), "private", ConversationBatch(("one", "private original")), default);
        var manager = new KnowledgeBaseManager(db, files.Paths, NullLogger<KnowledgeBaseManager>.Instance);
        Assert.DoesNotContain(manager.ListKnowledgeBases(), row => row.Name == KnowledgeInternalStores.Sessions);
        Assert.Throws<UnauthorizedAccessException>(() => manager.GetKnowledgeBase(KnowledgeInternalStores.Sessions));
        Assert.Throws<UnauthorizedAccessException>(() => manager.DeleteKnowledgeBase(KnowledgeInternalStores.Sessions));
        Assert.Throws<UnauthorizedAccessException>(() => manager.CreateKnowledgeBase(KnowledgeInternalStores.Sessions, "forbidden", null, "llamaindex"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new KnowledgeSourceSearchService(db, files.Paths).SearchAsync(KnowledgeInternalStores.Sessions, "private", 5, default));
        var retrieval = new AiAgent.Backend.Services.Chat.Retrieval.KnowledgeRetrievalService(db, null!, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => retrieval.SearchAsync(new() { KnowledgeBaseName = KnowledgeInternalStores.Sessions }, "private", 5, default));
        foreach (var name in new[] { KnowledgeInternalStores.Sessions, " KUNBUDDY-SESSION-STORE " })
        {
            var http = new DefaultHttpContext(); http.Request.RouteValues["kbName"] = name;
            var action = new Microsoft.AspNetCore.Mvc.ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(http.Request.RouteValues), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var context = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(action, []);
            await new KnowledgeInternalStoreFilter(files.Paths).OnAuthorizationAsync(context);
            Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(context.Result);
        }
        Assert.Equal("uploaded", db.Queryable<AiKnowledgeDocument>().Single().Status);
        Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
    }

    private static KunBuddySessionUpload ConversationBatch(params (string Id, string Markdown)[] messages) => new()
    { ClientId = "fixture-installation", Title = "原始会话", Entries = messages.Select(item => new KunBuddySessionEntry { Id = item.Id, Markdown = item.Markdown }).ToList() };

    [Fact]
    public async Task KunBuddySessionFolderManualUploadAlsoSavesOnlyRawEvenWhenProcessWasRequested()
    {
        using var db = Database(); using var files = new SemanticFiles();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var settings = new KnowledgeCompilerSettings(db);
        using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var resources = new KnowledgeResourceService(db, files.Paths, null!, worker, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("# 原始会话\n\n不自动处理。"));
        var file = new FormFile(stream, 0, stream.Length, "Files", "conversation.md") { Headers = new HeaderDictionary(), ContentType = "text/markdown" };
        var result = await resources.UploadAsync(new KnowledgeResourceUploadRequest { Uri = "viking://user/current/sessions/", Files = [file], Process = true }, default);
        Assert.Empty(result.Tasks); Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
        Assert.Empty(db.Queryable<AiKnowledgeParsedDocument>().ToList());
        Assert.Equal("uploaded", db.Queryable<AiKnowledgeDocument>().Single().Status);
    }

    [Fact]
    public async Task KunBuddySessionAppendsToOneDocumentAndRetriesDoNotDuplicateOrInvokeModels()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var service = new KunBuddySessionService(db, files.Paths);
        var alice = new AuthenticatedUser("alice-id", "alice");
        var first = await service.UploadAsync(alice, "session-one", ConversationBatch(("one", "## 用户\n\n你好，🟦")), default);
        var again = await service.UploadAsync(alice, "session-one", ConversationBatch(("one", "## 用户\n\n你好，🟦")), default);
        var next = await service.UploadAsync(alice, "session-one", ConversationBatch(("one", "## 用户\n\n你好，🟦"), ("two", "## 助手\n\n原样保存。")), default);
        Assert.Equal(first.DocumentId, next.DocumentId); Assert.Equal(0, again.Appended); Assert.Equal(1, next.Appended);
        var doc = db.Queryable<AiKnowledgeDocument>().Single();
        var raw = await File.ReadAllTextAsync(doc.StoragePath);
        Assert.Contains("你好，🟦", raw); Assert.Contains("原样保存。", raw);
        Assert.Equal(1, raw.Split("你好，🟦").Length - 1);
        Assert.StartsWith("viking://user/alice/sessions/", doc.ResourceUri);
        Assert.Equal("uploaded", doc.Status);
        Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
        Assert.Empty(db.Queryable<AiKnowledgeParsedDocument>().ToList());
        Assert.Empty(db.Queryable<AiKnowledgeArtifact>().ToList());
    }

    [Fact]
    public async Task KunBuddySessionRetainsRevisionsAndStaleRetriesCannotRevertThem()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var service = new KunBuddySessionService(db, files.Paths);
        var alice = new AuthenticatedUser("alice-id", "alice");
        await service.UploadAsync(alice, "same", ConversationBatch(("message", "old original")), default);
        await service.UploadAsync(alice, "same", ConversationBatch(("message", "new original")), default);
        var stale = await service.UploadAsync(alice, "same", ConversationBatch(("message", "old original")), default);
        Assert.Equal(0, stale.Appended);
        var text = await File.ReadAllTextAsync(db.Queryable<AiKnowledgeDocument>().Single().StoragePath);
        Assert.Contains("old original", text); Assert.Contains("new original", text); Assert.Contains("原消息修订", text);
        Assert.Equal(1, text.Split("old original").Length - 1);
    }

    [Fact]
    public async Task KunBuddySessionFragmentsRejoinWithoutAddingCharactersInsideOriginalText()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var service = new KunBuddySessionService(db, files.Paths);
        var original = "## 用户\n\n" + string.Concat(Enumerable.Repeat("原始🟦", 50000)) + "\n\n";
        var middle = original.Length / 2;
        // Split at a known BMP boundary so this tests storage joining rather than malformed JSON.
        while (char.IsSurrogate(original[middle])) middle++;
        await service.UploadAsync(new("alice-id", "alice"), "fragments", ConversationBatch(("message:0", original[..middle])), default);
        await service.UploadAsync(new("alice-id", "alice"), "fragments", ConversationBatch(("message:1", original[middle..])), default);
        var text = await File.ReadAllTextAsync(db.Queryable<AiKnowledgeDocument>().Single().StoragePath);
        Assert.Contains(original, text);
    }

    [Fact]
    public async Task KunBuddySessionConcurrentBatchesAndOwnerIsolationKeepIndependentFiles()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var service = new KunBuddySessionService(db, files.Paths);
        var alice = new AuthenticatedUser("alice-id", "alice");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => service.UploadAsync(alice, "same", ConversationBatch(("entry-" + i, "original-" + i)), default)));
        await service.UploadAsync(new("bob-id", "bob"), "same", ConversationBatch(("entry-0", "bob private")), default);
        var docs = db.Queryable<AiKnowledgeDocument>().ToList(); Assert.Equal(2, docs.Count);
        var aliceDoc = docs.Single(doc => doc.ResourceUri!.StartsWith("viking://user/alice/"));
        var text = await File.ReadAllTextAsync(aliceDoc.StoragePath);
        for (var i = 0; i < 8; i++) Assert.Contains("original-" + i, text);
        Assert.DoesNotContain("bob private", text);
    }

    [Fact]
    public async Task KunBuddySessionDirectoriesArePersonalAndSuperadminCanReadAllSessionFoldersOnly()
    {
        using var db = Database(); using var files = new SemanticFiles();
        db.CodeFirst.InitTables(typeof(AiUser), typeof(AiUserSession), typeof(AiKnowledgeDirectory));
        var auth = new AuthService(db, new ConfigurationBuilder().Build(), new SubscriptionEnvironment(Path.GetTempPath()));
        foreach (var name in new[] { "alice", "bob", "superadmin" }) await auth.CreateUserAsync(name, "fixture-password", null, default);
        db.Updateable<AiUser>().SetColumns(row => row.Role == "admin").Where(row => row.Username == "superadmin").ExecuteCommand();
        var service = new KunBuddySessionService(db, files.Paths);
        var alice = db.Queryable<AiUser>().First(row => row.Username == "alice");
        var bob = db.Queryable<AiUser>().First(row => row.Username == "bob");
        var own = await service.UploadAsync(new(alice.Id, alice.Username), "same", ConversationBatch(("one", "alice private")), default);
        var foreign = await service.UploadAsync(new(bob.Id, bob.Username), "same", ConversationBatch(("one", "bob private")), default);
        var kb = db.Queryable<AiKnowledgeBase>().First();
        var memory = files.Document(kb, "viking://user/bob/memory/private.md"); db.Insertable(memory).ExecuteCommand();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var settings = new KnowledgeCompilerSettings(db);
        using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance, httpContextAccessor: accessor);
        var ingestion = new FinishingIngestion();
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), null!, null!);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, semantic);
        async Task Login(string name) { var (_, token) = await auth.LoginAsync(name, "fixture-password", default); accessor.HttpContext!.Request.Headers.Authorization = "Bearer " + token; }
        await Login("alice");
        Assert.Contains(resources.Tree(), node => node.Uri == "viking://user/alice/sessions/" && node.Name == "会话");
        Assert.DoesNotContain(resources.Tree(), node => node.Uri == foreign.Uri);
        Assert.Throws<UnauthorizedAccessException>(() => resources.File(foreign.Uri));
        await Login("superadmin");
        Assert.Contains(resources.Tree(), node => node.Uri == own.Uri); Assert.Contains(resources.Tree(), node => node.Uri == foreign.Uri);
        Assert.Contains("bob private", await File.ReadAllTextAsync(resources.File(foreign.Uri).Path));
        Assert.Contains("bob private", (await resources.ReadAsync(foreign.Uri, default)).SourceText);
        Assert.DoesNotContain(resources.Tree(), node => node.Uri == memory.ResourceUri);
        Assert.Throws<UnauthorizedAccessException>(() => resources.File(memory.ResourceUri!));
        Assert.Throws<UnauthorizedAccessException>(() => resources.Parse(foreign.Uri));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resources.CreateDirectoryAsync(new() { ParentUri = "viking://user/bob/sessions/", Name = "forbidden" }, default));
        Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
        await Login("alice");
        resources.Parse(own.Uri);
        var job = Assert.Single(db.Queryable<AiKnowledgeJob>().ToList());
        Assert.Equal("viking://user/alice/", job.OwnerRoot);
        Assert.Equal(own.DocumentId, job.DocumentId);
    }
}
