using AiAgent.Backend.Services.TaskQueue;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Knowledge.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    private sealed class SemanticModel : IKnowledgeModel
    {
        public List<string> Prompts { get; } = [];
        public Task<CompilerReply> CompleteAsync(string prompt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Prompts.Add(prompt);
            return Task.FromResult(new CompilerReply("# 测试\n\n这是目录简介。\n\n## 详细说明\n项目发布要求。", "fake", "fake-model"));
        }
    }

    [Fact]
    public async Task SemanticOverviewExtractsAbstractWithoutAnExtraCallAndUsesHostLinks()
    {
        var model = new SemanticModel();
        var generator = new KnowledgeSemanticGenerator(model, 8);
        var result = await generator.OverviewAsync("文档", [new("发布[要求].md", "viking://resources/docs/release.md", "file", "每周三发布")], default);
        Assert.Single(model.Prompts);
        Assert.Equal("这是目录简介。", result.Abstract);
        Assert.Contains("[发布\\[要求\\].md](viking://resources/docs/release.md)", result.Overview);
    }

    [Fact]
    public async Task SemanticLongSourceReadsEveryPartAndBudgetAndCancellationAreEnforced()
    {
        var model = new SemanticModel();
        await new KnowledgeSemanticGenerator(model, 8).SummarizeAsync(new string('a', 16000) + "END_OF_SOURCE", default);
        Assert.Equal(3, model.Prompts.Count);
        Assert.Contains("END_OF_SOURCE", model.Prompts[1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeSemanticGenerator(model, 1).SummarizeAsync(new string('x', 16001), default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeSemanticGenerator(model, 8).SummarizeAsync("text", cancelled.Token));
    }

    [Fact]
    public async Task SemanticLargeDirectoryBatchesBeforeMerging()
    {
        var model = new SemanticModel();
        var entries = Enumerable.Range(0, 30).Select(i => new SemanticEntry($"文档{i}", $"viking://resources/{i}.md", "file", new string('摘', 2000))).ToList();
        var result = await new KnowledgeSemanticGenerator(model, 8).OverviewAsync("docs", entries, default);
        Assert.True(model.Prompts.Count > 1);
        Assert.All(entries, e => Assert.Contains(e.Uri, result.Overview));
        Assert.All(model.Prompts, p => Assert.True(p.Length < 30000));
    }

    [Fact]
    public async Task SemanticStorePublishesMatchingPairAndPreservesOldVersionOnCancellation()
    {
        using var fixture = new SemanticFiles();
        var store = new KnowledgeSemanticStore(fixture.Paths);
        const string uri = "viking://resources/docs/";
        var original = new KnowledgeSemanticStore.Snapshot("v1", "# Overview", "Abstract", "model", DateTime.UtcNow);
        await store.SaveAsync(uri, original, default);
        var restarted = new KnowledgeSemanticStore(fixture.Paths);
        Assert.Equal(original, restarted.Read(uri, "v1"));
        Assert.Null(restarted.Read(uri, "v2"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(uri, original with { Fingerprint = "v2", Overview = "new" }, cancelled.Token));
        Assert.Equal(original, restarted.Read(uri, "v1"));
        Assert.Single(Directory.GetFiles(fixture.Paths.RootPath, ".overview.md", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("viking://resources/docs/", "llm_api")]
    [InlineData("viking://user/alice/docs/", "codex")]
    public async Task ResourceSemanticsAreBottomUpCachedAndInvalidatedOnDeletion(string directory, string generator)
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db); var doc = files.Document(kb, directory + "manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var config = new KnowledgeCompilerSettingsDto { Generator = generator, MaxSteps = 12 };
        var settings = new KnowledgeCompilerSettings(db); settings.Save(config);
        var api = new Llm(); var cli = new Codex();
        var replies = generator == "codex" ? cli.Replies : api.Replies;
        for (var i = 0; i < 3; i++) replies.Enqueue("# 发布指南\n\n发布规则简介。\n\n## 详细说明\n每周三发布。");
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        Assert.Equal(3, replies.Count); // Parsing text never calls the language model.
        var store = new KnowledgeSemanticStore(files.Paths);
        var service = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, store, api, cli);
        await service.GenerateAsync(kb, doc, config, (_, _) => { }, default);
        Assert.Empty(replies);
        Assert.Equal("发布规则简介。", service.Read(directory)!.Abstract);
        Assert.Contains(doc.ResourceUri!, service.Read(directory)!.Overview);
        Assert.NotNull(service.Read(KnowledgeResourceService.Parent(directory)));
        if (generator == "codex")
        {
            Assert.Null(service.Read("viking://user/"));
            Assert.All(cli.Requests, r => Assert.Equal("read-only", r.CodexSandboxMode));
        }
        // Retrying through parse with unchanged text must reuse completed semantics.
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        await service.GenerateAsync(kb, doc, config, (_, _) => { }, default); // No replies left: must use cache.
        Assert.Equal(99, db.Queryable<AiKnowledgeBase>().InSingle(kb.Id).ActiveVersionId);
        Assert.Empty(db.Queryable<AiKnowledgeArtifact>().ToList());
        db.Updateable<AiKnowledgeDocument>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == doc.Id).ExecuteCommand();
        Assert.Null(service.Read(directory));
    }

    [Fact]
    public async Task SemanticBudgetRetryResumesAndChangedBodyInvalidatesAncestors()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/docs/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var api = new Llm(); var cli = new Codex(); var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        var service = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var config = new KnowledgeCompilerSettingsDto { Generator = "llm_api", MaxSteps = 1 };
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        api.Replies.Enqueue("# 文档\n\n文件摘要。");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(kb, doc, config, (_, _) => { }, default));
        Assert.NotNull(service.Read(doc.ResourceUri!));
        Assert.Null(service.Read("viking://resources/docs/"));
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        config.MaxSteps = 2;
        api.Replies.Enqueue("# docs\n\n目录摘要。");
        api.Replies.Enqueue("# resources\n\n根目录摘要。");
        await service.GenerateAsync(kb, doc, config, (_, _) => { }, default);
        Assert.Empty(api.Replies);
        Assert.Equal("目录摘要。", service.Read("viking://resources/docs/")!.Abstract);
        File.WriteAllText(doc.StoragePath, "# 新规则\n\n每周五发布。");
        doc.FileHash = files.Paths.ComputeFileHash(doc.StoragePath);
        db.Updateable(doc).ExecuteCommand();
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        Assert.Null(service.Read(doc.ResourceUri!));
        Assert.Null(service.Read("viking://resources/docs/"));
        Assert.Null(service.Read("viking://resources/"));
    }

    [Fact]
    public async Task SemanticFailureLeavesParsedBodyReadableAndQueueReportsError()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/docs/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); settings.Save(new() { Generator = "llm_api" });
        var api = new Llm(); api.Replies.Enqueue("");
        var cli = new Codex();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance, semantic);
        await worker.StartAsync(default);
        try
        {
            worker.Enqueue(kb.Name, doc.Id);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (worker.Latest(kb.Name, doc.Id)!.Status is "queued" or "processing") await Task.Delay(20, deadline.Token);
            Assert.Equal("error", worker.Latest(kb.Name, doc.Id)!.Status);
            Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
            Assert.Equal("processed", db.Queryable<AiKnowledgeDocument>().InSingle(doc.Id).Status);
            Assert.Null(semantic.Read("viking://resources/docs/"));
            var center = new AiAgent.Backend.Services.TaskCenter.TaskCenterService(db, worker);
            var failed = Assert.Single(center.List().Tasks);
            Assert.Equal("failed", failed.Status);
            Assert.Equal("semantic", failed.Stage);
            Assert.False(string.IsNullOrWhiteSpace(failed.ErrorMessage));
            Assert.Contains("语义", failed.ErrorMessage);
            foreach (var name in new[] { "文件", "目录", "根目录" }) api.Replies.Enqueue($"# {name}\n\n{name}简介。");
            var retry = center.Retry("knowledge", failed.Id);
            Assert.NotEqual(failed.Id, retry.Id);
            while (worker.Latest(kb.Name, doc.Id)!.Status is "queued" or "processing") await Task.Delay(20, deadline.Token);
            var completed = center.List().Tasks.Single(x => x.Id == retry.Id);
            Assert.Equal("completed", completed.Status);
            Assert.Equal("completed", completed.Stage);
            Assert.NotNull(semantic.Read("viking://resources/docs/"));
            Assert.Equal(2, center.List(status: "failed").Summary.Total);
            Assert.Single(center.List(status: "failed").Tasks);
        }
        finally { await worker.StopAsync(default); }
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("missing-overview")]
    [InlineData("empty-abstract")]
    public async Task DamagedSemanticsDoNotHideParsedBodyAndCanBeRebuilt(string damage)
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var api = new Llm(); var cli = new Codex(); var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        var service = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var config = new KnowledgeCompilerSettingsDto { Generator = "llm_api", MaxSteps = 8 };
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        api.Replies.Enqueue("# 文件\n\n文件摘要。"); api.Replies.Enqueue("# 目录\n\n目录简介。");
        await service.GenerateAsync(kb, doc, config, (_, _) => { }, default);
        var root = Path.Combine(files.Paths.RootPath, ".resource-semantic", KnowledgeSemanticStore.Hash(doc.ResourceUri!));
        if (damage == "manifest") File.WriteAllText(Path.Combine(root, "current.json"), "{broken");
        else if (damage == "missing-overview") File.Delete(Directory.GetFiles(root, ".overview.md", SearchOption.AllDirectories).Single());
        else File.WriteAllText(Directory.GetFiles(root, ".abstract.md", SearchOption.AllDirectories).Single(), "");
        Assert.Null(service.Read(doc.ResourceUri!));
        Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
        api.Replies.Enqueue("# 文件\n\n恢复后的文件摘要。");
        await service.GenerateAsync(kb, doc, config, (_, _) => { }, default);
        Assert.Empty(api.Replies);
        Assert.Equal("恢复后的文件摘要。", service.Read(doc.ResourceUri!)!.Abstract);
    }

    [Fact]
    public async Task ResourceQueuePublishesL0L1BeforeReportingSuccess()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/docs/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); settings.Save(new() { Generator = "llm_api" });
        var api = new Llm(); var cli = new Codex();
        foreach (var name in new[] { "文件", "目录", "资料根目录" }) api.Replies.Enqueue($"# {name}\n\n{name}简介。");
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance, semantic);
        await worker.StartAsync(default);
        try
        {
            worker.Enqueue(kb.Name, doc.Id);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (worker.Latest(kb.Name, doc.Id)!.Status is "queued" or "processing") await Task.Delay(20, deadline.Token);
            Assert.Equal("success", worker.Latest(kb.Name, doc.Id)!.Status);
            Assert.Equal(100, worker.Latest(kb.Name, doc.Id)!.Progress);
            Assert.Empty(api.Replies);
            Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
            Assert.Equal("目录简介。", semantic.Read("viking://resources/docs/")!.Abstract);
            Assert.Contains(doc.ResourceUri!, semantic.Read("viking://resources/docs/")!.Overview);
            Assert.NotNull(semantic.Read("viking://resources/"));
            Assert.Equal(99, db.Queryable<AiKnowledgeBase>().InSingle(kb.Id).ActiveVersionId);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task MissingSemanticServiceFailsVisiblyAfterSavingBody()
    {
        using var db = Database(); using var files = new SemanticFiles();
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), settings);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            worker.Enqueue(kb.Name, doc.Id);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (worker.Latest(kb.Name, doc.Id)!.Status is "queued" or "processing") await Task.Delay(20, deadline.Token);
            var task = Assert.Single(new AiAgent.Backend.Services.TaskCenter.TaskCenterService(db, worker).List().Tasks);
            Assert.Equal("failed", task.Status);
            Assert.Equal("semantic", task.Stage);
            Assert.Contains("语义生成服务未配置", task.ErrorMessage);
            Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
        }
        finally { await worker.StopAsync(default); }
    }

    private sealed class SemanticFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "aiagent-semantic-tests-" + Guid.NewGuid().ToString("N"));
        public KnowledgePathService Paths { get; }
        public SemanticFiles() => Paths = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = _root }).Build(), NullLogger<KnowledgePathService>.Instance);
        public AiKnowledgeDocument Document(AiKnowledgeBase kb, string uri)
        {
            var raw = Paths.GetRawPath(kb.Name); Directory.CreateDirectory(raw);
            var path = Path.Combine(raw, Guid.NewGuid().ToString("N") + ".md"); File.WriteAllText(path, "# 发布\n\n每周三发布。");
            return new() { KnowledgeBaseId = kb.Id, OriginalFileName = "manual.md", Extension = ".md", StoragePath = path, ResourceUri = uri, FileHash = Paths.ComputeFileHash(path) };
        }
        public void Dispose()
        {
            var full = Path.GetFullPath(_root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("aiagent-semantic-tests-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
