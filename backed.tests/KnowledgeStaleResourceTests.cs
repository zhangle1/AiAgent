using System.IO;
using System.Text.Json;
using System.Reflection;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Theory]
    [InlineData("outside_root")]
    [InlineData("missing")]
    public async Task LegacySourceReturnsUnavailableStateWithoutEscapingStorageBoundary(string reason)
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        using var current = new SemanticFiles();
        using var previous = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        const string uri = "viking://user/current/sessions/old.md";
        var document = (reason == "outside_root" ? previous : current).Document(kb, uri);
        document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
        if (reason == "missing") File.Delete(document.StoragePath);
        var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, current.Paths, null!, new Llm(), new Codex(), settings);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, current.Paths, new(current.Paths), new Llm(), new Codex());
        var resources = new KnowledgeResourceService(db, current.Paths, ingestion, worker, settings, new Llm(), new Codex(),
            NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, semantic);

        var result = await resources.ReadAsync(uri, default);

        Assert.Null(result.SourceText);
        var node = JsonSerializer.SerializeToElement(result.Node);
        Assert.Equal(reason, node.GetProperty("source_status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(node.GetProperty("source_message").GetString()));
        Assert.DoesNotContain(previous.Paths.RootPath, JsonSerializer.Serialize(result));
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);
    }

    [Fact]
    public async Task CleanupOnlyRemovesOwnedStaleRecordsAndPreservesFilesAndActiveTasks()
    {
        using var db = Database();
        using var current = new SemanticFiles();
        using var previous = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        AiKnowledgeDocument Add(string uri, bool valid = false)
        {
            var doc = (valid ? current : previous).Document(kb, uri);
            doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
            return doc;
        }
        var stale = Add("viking://user/alice/sessions/old.md");
        var valid = Add("viking://user/alice/sessions/current.md", true);
        var other = Add("viking://user/bob/sessions/old.md");
        var shared = Add("viking://resources/old.md");
        var busy = Add("viking://user/alice/sessions/busy.md");
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = busy.Id, JobType = "wiki_compile", Status = "processing" }).ExecuteCommand();
        var service = CleanupService(db, current.Paths, new("alice-id", "alice"));

        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);
        Assert.Equal(new[] { stale.Id, busy.Id }, preview.Items.Select(item => item.DocumentId));
        Assert.False(preview.Items.Single(item => item.DocumentId == busy.Id).CanCleanup);
        Assert.All(db.Queryable<AiKnowledgeDocument>().ToList(), doc => Assert.False(doc.IsDeleted));
        Assert.DoesNotContain(previous.Paths.RootPath, JsonSerializer.Serialize(preview));

        var result = await service.CleanupAsync(new() { DocumentIds = [stale.Id], SnapshotSha256 = preview.SnapshotSha256 }, default);
        Assert.NotNull(result);
        Assert.Equal(1, result.RemovedCount);
        Assert.True(db.Queryable<AiKnowledgeDocument>().InSingle(stale.Id).IsDeleted);
        Assert.NotNull(db.Queryable<AiKnowledgeDocument>().InSingle(stale.Id).DeletedAt);
        Assert.All(new[] { valid.Id, other.Id, shared.Id, busy.Id }, id => Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(id).IsDeleted));
        Assert.True(File.Exists(stale.StoragePath));
        Assert.Contains("每周三发布", await File.ReadAllTextAsync(stale.StoragePath));
        var after = await service.PreviewAsync(default);
        Assert.NotNull(after);
        Assert.DoesNotContain(after.Items, item => item.DocumentId == stale.Id);
    }

    [Fact]
    public async Task CleanupAdminCanManageSharedStaleRecordsButNotAnotherUsersSessions()
    {
        using var db = Database(); using var current = new SemanticFiles(); using var previous = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        var shared = previous.Document(kb, "viking://projects/example/old.md"); shared.Id = db.Insertable(shared).ExecuteReturnBigIdentity();
        var privateDoc = previous.Document(kb, "viking://user/bob/sessions/secret.md"); privateDoc.Id = db.Insertable(privateDoc).ExecuteReturnBigIdentity();
        var service = CleanupService(db, current.Paths, new("admin-id", "admin", "admin"));
        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);
        Assert.True(preview.IncludesShared);
        Assert.Equal(shared.Id, Assert.Single(preview.Items).DocumentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupAsync(new() { DocumentIds = [shared.Id, privateDoc.Id], SnapshotSha256 = preview.SnapshotSha256 }, default));
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(shared.Id).IsDeleted);
        await service.CleanupAsync(new() { DocumentIds = [shared.Id], SnapshotSha256 = preview.SnapshotSha256 }, default);
        Assert.True(db.Queryable<AiKnowledgeDocument>().InSingle(shared.Id).IsDeleted);
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(privateDoc.Id).IsDeleted);
    }

    [Theory]
    [InlineData("restored")]
    [InlineData("task_started")]
    [InlineData("record_changed")]
    public async Task CleanupRejectsStalePreviewAfterSourceOrTaskChanges(string change)
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        var document = files.Document(kb, "viking://user/alice/sessions/old.md");
        File.Delete(document.StoragePath);
        document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
        var service = CleanupService(db, files.Paths, new("alice-id", "alice"));
        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);
        Assert.Single(preview.Items);
        if (change == "restored") await File.WriteAllTextAsync(document.StoragePath, "restored source");
        if (change == "task_started") db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = document.Id, Status = "queued" }).ExecuteCommand();
        if (change == "record_changed") db.Updateable<AiKnowledgeDocument>().SetColumns(d => d.OriginalFileName == "renamed.md").Where(d => d.Id == document.Id).ExecuteCommand();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupAsync(new() { DocumentIds = [document.Id], SnapshotSha256 = preview.SnapshotSha256 }, default));
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);
    }

    [Fact]
    public async Task CleanupRejectsValidRecordsEvenWhenIdsAreForgedAndRollsBackBatch()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        var stale = files.Document(kb, "viking://user/alice/sessions/stale.md"); File.Delete(stale.StoragePath);
        stale.Id = db.Insertable(stale).ExecuteReturnBigIdentity();
        var valid = files.Document(kb, "viking://user/alice/sessions/valid.md"); valid.Id = db.Insertable(valid).ExecuteReturnBigIdentity();
        var service = CleanupService(db, files.Paths, new("alice-id", "alice"));
        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupAsync(new() { DocumentIds = [stale.Id, valid.Id], SnapshotSha256 = preview.SnapshotSha256 }, default));
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(stale.Id).IsDeleted);
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(valid.Id).IsDeleted);
        Assert.True(File.Exists(valid.StoragePath));
    }

    [Fact]
    public async Task CleanupDatabaseFailureRollsBackEverySelectedRecord()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        var ids = new List<long>();
        foreach (var name in new[] { "first", "second" })
        {
            var document = files.Document(kb, $"viking://user/alice/sessions/{name}.md");
            File.Delete(document.StoragePath);
            ids.Add(db.Insertable(document).ExecuteReturnBigIdentity());
        }
        db.Ado.ExecuteCommand($"CREATE TRIGGER fail_cleanup BEFORE UPDATE ON ai_knowledge_document WHEN NEW.Id = {ids[1]} AND NEW.IsDeleted = 1 BEGIN SELECT RAISE(ABORT, 'fixture cleanup failure'); END;");
        var service = CleanupService(db, files.Paths, new("alice-id", "alice"));
        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CleanupAsync(new() { DocumentIds = ids, SnapshotSha256 = preview.SnapshotSha256 }, default));

        Assert.All(ids, id => Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(id).IsDeleted));
    }

    [Fact]
    public async Task CleanupDisconnectAfterWritingRollsBackTheWholeBatch()
    {
        using var db = Database(); using var files = new SemanticFiles();
        using var cancellation = new CancellationTokenSource();
        var kb = CreateBase(db, "resource-store");
        var document = files.Document(kb, "viking://user/alice/sessions/stale.md");
        File.Delete(document.StoragePath);
        document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
        var auth = DispatchProxy.Create<IAuthService, TeamAuth>();
        ((TeamAuth)(object)auth).User = new("alice-id", "alice");
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var service = new KnowledgeResourceCleanupService(db, files.Paths, auth, new HttpContextAccessor { HttpContext = context });
        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);
        var writes = 0;
        db.Aop.OnLogExecuted = (sql, _) => {
            if (sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                writes++;
                cancellation.Cancel();
            }
        };

        var result = await service.CleanupAsync(new() { DocumentIds = [document.Id], SnapshotSha256 = preview.SnapshotSha256 }, cancellation.Token);

        Assert.Equal(1, writes);
        Assert.Null(result);
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);
    }

    [Fact]
    public async Task CleanupRejectsActiveAndUnauthenticatedRequests()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db, "resource-store");
        var document = files.Document(kb, "viking://user/alice/sessions/old.md"); File.Delete(document.StoragePath);
        document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = document.Id, Status = "cancelling" }).ExecuteCommand();
        var service = CleanupService(db, files.Paths, new("alice-id", "alice"));
        var preview = await service.PreviewAsync(default);
        Assert.NotNull(preview);
        Assert.False(Assert.Single(preview.Items).CanCleanup);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupAsync(new() { DocumentIds = [document.Id], SnapshotSha256 = preview.SnapshotSha256 }, default));
        var anonymous = CleanupService(db, files.Paths, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.PreviewAsync(default));
        Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../old.md")]
    [InlineData("old.md")]
    public void LegacyInvalidPathIsClassifiedBeforeFilesystemAccess(string path)
    {
        using var files = new SemanticFiles();
        var source = KnowledgeSourceFile.Inspect(files.Paths, new() { Name = "fixture" }, new() { StoragePath = path });
        Assert.Equal("invalid_path", source.Status);
        Assert.True(source.CanClean);
        Assert.Null(source.Path);
    }

    private static KnowledgeResourceCleanupService CleanupService(SqlSugar.ISqlSugarClient db, IKnowledgePathService paths, AuthenticatedUser user)
    {
        var auth = DispatchProxy.Create<IAuthService, TeamAuth>();
        ((TeamAuth)(object)auth).User = user;
        return new(db, paths, auth, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
    }
}
