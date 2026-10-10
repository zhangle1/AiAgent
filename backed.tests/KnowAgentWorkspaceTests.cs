using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Knowledge.Core;
using AiAgent.Backend.Services.Knowledge.KnowAgent;
using AiAgent.Backend.Services.TaskQueue;
using AiAgent.Backend.Services.TaskCenter;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task WikiCompilationPublishesToOwnerWorkspaceAndRetainsEvidence()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var api = new Llm(); api.Replies.Enqueue("Release rules.");
        api.Replies.Enqueue(System.Text.Json.JsonSerializer.Serialize(new { pages = new[] { new {
            title = "Release", markdown = "Release rules.", evidence = new[] { new { part = 1, quote = File.ReadAllText(doc.StoragePath) } }
        } } }));
        var service = new KnowledgeIngestionService(db, files.Paths, null!, api, new Codex(), new(db));
        const string owner = "viking://user/alice/";
        await service.ProcessAsync(kb, doc, new() { Generator = "llm_api", OwnerRoot = owner }, default);
        var artifact = Assert.Single(db.Queryable<AiKnowledgeArtifact>().ToList());
        Assert.Equal(owner, artifact.OwnerRoot);
        Assert.Contains("每周三发布", artifact.Content);
        var output = Assert.Single(Directory.GetFiles(Path.Combine(files.Paths.RootPath, ".user-workspaces", KnowledgeSemanticStore.Hash(owner), "wiki"), "*.md"));
        Assert.Equal(artifact.Content, File.ReadAllText(output));
        Assert.Empty(new KnowledgeWorkspaceService(db).ListPages(kb.Name, "viking://user/bob/"));
    }

    [Fact]
    public void TokenSplittingPreservesAllUnicodeAndReservesOutput()
    {
        var source = string.Concat(Enumerable.Repeat("知识😀abc", 1000));
        var parts = ContextBudget.Split(source, 97).ToList();
        Assert.Equal(source, string.Concat(parts));
        Assert.All(parts, p => { Assert.InRange(ContextBudget.Estimate(p), 1, 97); Assert.False(char.IsLowSurrogate(p[0])); });
        Assert.Throws<InvalidOperationException>(() => ContextBudget.Validate(new string('a', 60000)));
    }

    [Fact]
    public async Task KnowAgentCompressesContextBeforeEvidenceGenerationAndSharesBudget()
    {
        var model = new SemanticModel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowAgent(model, 1)
            .CompileAsync("source", null, default, new string('a', 16000)));
        Assert.Single(model.Prompts);
        Assert.StartsWith("Compress", model.Prompts[0]);
    }

    [Fact]
    public async Task PersonalSummariesAreScopedPersistedAndInvalidatedWithoutChangingSources()
    {
        using var db = Database(); using var files = new SemanticFiles();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/docs/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        const string owner = "viking://user/current/";
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, OwnerRoot = owner, JobType = "wiki_compile" }).ExecuteCommand();
        var api = new Llm(); var cli = new Codex(); var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var original = File.ReadAllText(doc.StoragePath);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        for (var i = 0; i < 5; i++) api.Replies.Enqueue("# Summary\n\nWorkspace summary.");
        await semantic.GenerateAsync(kb, doc, new() { Generator = "llm_api", MaxSteps = 8 }, (_, _) => { }, default, owner);
        Assert.Empty(api.Replies);
        Assert.NotNull(semantic.Read(owner, owner));
        Assert.Null(semantic.Read("viking://resources/docs/"));
        Assert.Empty(semantic.WorkspaceNodes("viking://user/bob/"));
        var target = KnowledgeResourceSemanticService.TargetUri(doc.ResourceUri!, owner);
        Assert.Contains(doc.ResourceUri!, semantic.Read(target, owner)!.Overview);
        Assert.Null(semantic.Read(target, "viking://user/bob/"));
        Assert.True(Directory.Exists(Path.Combine(files.Paths.RootPath, ".user-workspaces", KnowledgeSemanticStore.Hash(owner))));
        Assert.Equal(original, File.ReadAllText(doc.StoragePath));
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance, semantic, new HttpContextAccessor());
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, api, cli,
            NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), semantic);
        Assert.Contains(resources.Tree(), n => n.Uri == target);
        var read = await resources.ReadAsync(target, default);
        Assert.Contains("Workspace summary", read.SourceText);
        var download = resources.File(target);
        Assert.Contains("Workspace summary", File.ReadAllText(download.Path));
        Assert.NotEqual(doc.StoragePath, download.Path);
        Assert.Single(resources.Processing());
        db.Updateable<AiKnowledgeDocument>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == doc.Id).ExecuteCommand();
        Assert.Null(semantic.Read(owner, owner));
        Assert.Empty(semantic.WorkspaceNodes(owner));
    }

    [Fact]
    public void QueueAndTaskCenterKeepDifferentOwnersSeparate()
    {
        using var db = Database(); var kb = CreateBase(db);
        var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "source.md" };
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var other = new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, OwnerRoot = "viking://user/bob/", JobType = "wiki_compile" };
        other.Id = db.Insertable(other).ExecuteReturnBigIdentity();
        using var worker = new KnowledgeCompilationWorker(db, new(db), null!, NullLogger<KnowledgeCompilationWorker>.Instance, httpContextAccessor: new HttpContextAccessor());
        var own = worker.Enqueue(kb.Name, doc.Id);
        Assert.NotEqual(other.Id, own.Id);
        Assert.Equal("viking://user/current/", db.Queryable<AiKnowledgeJob>().InSingle(own.Id).OwnerRoot);
        Assert.Equal(own.Id, worker.Enqueue(kb.Name, doc.Id).Id);
        var center = new TaskCenterService(db, worker);
        Assert.Equal(own.Id, Assert.Single(center.List().Tasks).Id);
        Assert.Throws<KeyNotFoundException>(() => center.Cancel("knowledge", other.Id));
        Assert.Throws<KeyNotFoundException>(() => center.Retry("knowledge", other.Id));
    }

    [Fact]
    public void PersonalWikiDoesNotLeakThroughPagesContentOrContextTree()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        db.CodeFirst.InitTables(typeof(AiKnowledgeContextNode));
        var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var own = new AiKnowledgeArtifact { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, Content = "my wiki", OwnerRoot = "viking://user/current/" };
        own.Id = db.Insertable(own).ExecuteReturnBigIdentity();
        var other = new AiKnowledgeArtifact { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, Content = "bob secret", OwnerRoot = "viking://user/bob/" };
        other.Id = db.Insertable(other).ExecuteReturnBigIdentity();
        var accessor = new HttpContextAccessor();
        Assert.Equal("my wiki", Assert.Single(new KnowledgeWorkspaceService(db, accessor).ListPages(kb.Name)).Content);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), new(db), httpContextAccessor: accessor);
        Assert.Equal("my wiki", ingestion.GetContent(kb, doc).ArtifactContent);
        var context = new KnowledgeContextService(db, accessor);
        context.EnsureArtifactNode(kb, other, doc.OriginalFileName);
        var tree = context.ListTree(kb);
        Assert.DoesNotContain(tree, n => n.ArtifactId == other.Id);
        Assert.Contains(tree, n => n.ArtifactId == own.Id && n.Uri.StartsWith(own.OwnerRoot + "wiki/"));
    }
}
