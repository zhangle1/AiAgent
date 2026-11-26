using System.IO;
using System.Security.Cryptography;
using System.Text;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task DeletionHidesSourceAndParsedProjectionsButPreservesBytesAndHistory()
    {
        using var db = Database(); using var files = new SemanticFiles();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiUser), typeof(AiUserSession));
        const string token = "synthetic-deletion-test-token";
        db.Insertable(new AiUser { Id = "delete-admin", Username = "delete-admin", Role = "admin" }).ExecuteCommand();
        db.Insertable(new AiUserSession { UserId = "delete-admin", TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), ExpiresAt = DateTime.UtcNow.AddMinutes(10) }).ExecuteCommand();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers.Authorization = "Bearer " + token;
        var kb = CreateBase(db, "resource-store");
        var doc = files.Document(kb, "viking://resources/available.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), settings);
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var job = new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, Status = "error", ErrorMessage = "synthetic previous failure" };
        job.Id = db.Insertable(job).ExecuteReturnBigIdentity();
        var resource = new KnowledgeResourceService(db, files.Paths, ingestion, null!, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
        var before = resource.Tree().Where(n => n.DocumentId == doc.Id).ToList();
        Assert.True(before.Single(n => n.Uri == doc.ResourceUri).CanDelete);
        Assert.Contains(before, n => n.SourceUri == doc.ResourceUri && n.Kind == "file");
        Assert.All(before.Where(n => n.SourceUri is not null), n => Assert.False(n.CanDelete));
        var raw = File.ReadAllBytes(doc.StoragePath);
        var parsedCount = db.Queryable<AiKnowledgeParsedDocument>().Count();
        var result = await resource.DeleteAsync(doc.ResourceUri!, doc.Id, new("delete-admin", "delete-admin", "admin"), default);
        Assert.True(result.Deleted); Assert.Equal(doc.Id, result.DocumentId); Assert.Equal(doc.ResourceUri, result.Uri);
        Assert.DoesNotContain(resource.Tree(), n => n.DocumentId == doc.Id);
        foreach (var node in before.Where(n => n.Kind == "file")) Assert.Throws<KeyNotFoundException>(() => resource.File(node.Uri));
        Assert.Equal(raw, File.ReadAllBytes(doc.StoragePath));
        Assert.Equal(parsedCount, db.Queryable<AiKnowledgeParsedDocument>().Count());
        Assert.Equal("synthetic previous failure", db.Queryable<AiKnowledgeJob>().InSingle(job.Id).ErrorMessage);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => resource.DeleteAsync(doc.ResourceUri!, doc.Id, new("delete-admin", "delete-admin", "admin"), default));
    }

    [Theory]
    [InlineData("viking://resources/shared.md", "user", "alice", false)]
    [InlineData("viking://resources/shared.md", "admin", "alice", true)]
    [InlineData("viking://user/alice/sessions/own.md", "user", "alice", true)]
    [InlineData("viking://user/alice2/sessions/other.md", "user", "alice", false)]
    [InlineData("viking://user/bob/sessions/other.md", "admin", "alice", false)]
    public async Task ResourceDeletionEnforcesOwnerAndSharedAdministratorPermissions(string uri, string role, string username, bool allowed)
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, uri); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var resource = new KnowledgeResourceService(db, files.Paths, null!, null!, null!, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        if (allowed) await resource.DeleteAsync(uri, doc.Id, new(username, username, role), default);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resource.DeleteAsync(uri, doc.Id, new(username, username, role), default));
        Assert.Equal(allowed, db.Queryable<AiKnowledgeDocument>().InSingle(doc.Id).IsDeleted);
        Assert.True(File.Exists(doc.StoragePath));
    }

    [Theory]
    [InlineData("queued", "error")]
    [InlineData("pending", "uploaded")]
    [InlineData("processing", "error")]
    [InlineData("cancelling", "processed")]
    [InlineData("error", "parsing")]
    [InlineData("success", "indexing")]
    public async Task ResourceDeletionRejectsActiveTasksAndDocumentProcessing(string jobStatus, string documentStatus)
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/busy.md"); doc.Status = documentStatus; doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, Status = jobStatus }).ExecuteCommand();
        var resource = new KnowledgeResourceService(db, files.Paths, null!, null!, null!, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resource.DeleteAsync(doc.ResourceUri!, doc.Id, new("admin", "admin", "admin"), default));
        Assert.Contains("活动处理任务", error.Message);
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(doc.Id).IsDeleted);
        Assert.True(File.Exists(doc.StoragePath));
    }

    [Fact]
    public async Task ResourceDeletionRejectsMismatchedSelectionGeneratedFilesDirectoriesAndCancellation()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/keep.md"); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var resource = new KnowledgeResourceService(db, files.Paths, null!, null!, null!, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        var admin = new AuthenticatedUser("admin", "admin", "admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() => resource.DeleteAsync("viking://resources/other.md", doc.Id, admin, default));
        foreach (var uri in new[] { "viking://resources/", "viking://resources/folder/", "viking://resources/_parsed/book/chapter.md", "viking://resources/_compiled/book.md" })
            await Assert.ThrowsAsync<ArgumentException>(() => resource.DeleteAsync(uri, doc.Id, admin, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resource.DeleteAsync(doc.ResourceUri!, doc.Id, admin, new CancellationToken(true)));
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(doc.Id).IsDeleted);
        Assert.True(File.Exists(doc.StoragePath));
    }
}
