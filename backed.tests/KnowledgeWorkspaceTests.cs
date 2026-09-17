using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using System.IO;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeWorkspaceTests
{
    private static SqlSugarScope Database()
    {
        var db = new SqlSugarScope(new ConnectionConfig {
            DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new() { EntityService = (_, column) => {
                if (column.DataType == "nvarchar(max)") column.DataType = "text";
                if (column.IsIdentity) column.DataType = "INTEGER";
            } }
        });
        db.CodeFirst.InitTables(typeof(AiKnowledgeBase), typeof(AiKnowledgeDocument), typeof(AiKnowledgeParsedDocument), typeof(AiKnowledgeArtifact), typeof(AiKnowledgeJob), typeof(AiSettingSnapshot));
        return db;
    }
    private static AiKnowledgeBase CreateBase(ISqlSugarClient db, string name = "fixture")
    {
        var kb = new AiKnowledgeBase { Name = name, DisplayName = name, MetadataJson = "{\"existing\":true}", ActiveVersionId = 99, Status = "ready" };
        kb.Id = db.Insertable(kb).ExecuteReturnBigIdentity(); return kb;
    }

    [Fact]
    public void OrganizationPreservesExistingMetadataAndRejectsOrphanProjects()
    {
        using var db = Database(); var kb = CreateBase(db); var service = new KnowledgeWorkspaceService(db);
        Assert.Throws<ArgumentException>(() => service.SaveOrganization(kb.Name, new() { Project = "orphan" }));
        service.SaveOrganization(kb.Name, new() { Company = " Company ", Project = " Project " });
        var stored = db.Queryable<AiKnowledgeBase>().InSingle(kb.Id);
        Assert.Contains("\"existing\":true", stored.MetadataJson);
        Assert.Equal("Company", KnowledgeWorkspaceService.ReadOrganization(stored.MetadataJson).Company);
        Assert.Equal(99, stored.ActiveVersionId);
    }

    [Fact]
    public void LatestPagesExcludeDeletedSourcesAndOtherBases()
    {
        using var db = Database(); var kb = CreateBase(db); var other = CreateBase(db, "other");
        var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "source.txt" };
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        foreach (var content in new[] { "old", "latest" })
            db.Insertable(new AiKnowledgeArtifact { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, Content = content }).ExecuteCommand();
        db.Insertable(new AiKnowledgeArtifact { KnowledgeBaseId = other.Id, DocumentId = doc.Id, Content = "other base" }).ExecuteCommand();
        var service = new KnowledgeWorkspaceService(db);
        Assert.Equal("latest", Assert.Single(service.ListPages(kb.Name)).Content);
        db.Updateable<AiKnowledgeDocument>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == doc.Id).ExecuteCommand();
        Assert.Empty(service.ListPages(kb.Name));
    }

    [Fact]
    public async Task QueueDeduplicatesAndRestartMakesInterruptedWorkRetryable()
    {
        using var db = Database(); var kb = CreateBase(db);
        var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id };
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db);
        settings.Save(new() { Generator = "llm_api", MaxSteps = 8 });
        Assert.Equal("llm_api", settings.Get().Generator);
        using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var first = worker.Enqueue(kb.Name, doc.Id);
        Assert.Equal(first.Id, worker.Enqueue(kb.Name, doc.Id).Id);
        using var restarted = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        await restarted.StartAsync(CancellationToken.None);
        Assert.Equal("error", restarted.Latest(kb.Name, doc.Id)!.Status);
        await restarted.StopAsync(CancellationToken.None);
    }

    private sealed class Codex : ICodexChatService
    {
        public Queue<string> Replies { get; } = new();
        public List<ChatCompleteRequest> Requests { get; } = [];
        public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
        {
            Requests.Add(request); return Task.FromResult(new ChatCompleteResponse { Content = Replies.Count > 0 ? Replies.Dequeue() : "{}", Model = "cli-model" });
        }
        public Task HeartbeatAsync(AuthenticatedUser user, CodexRuntimeHeartbeatRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Llm : ILlmChatClient
    {
        public Queue<string> Replies { get; } = new();
        public string? ModelId { get; private set; }
        public Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken)
        {
            ModelId = modelId;
            return Task.FromResult(new LlmChatResult { Text = Replies.Count > 0 ? Replies.Dequeue() : "{}", Provider = "api", Model = "api-model" });
        }
        public IAsyncEnumerable<LlmStreamChunk> StreamAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AdapterRoutesCliAndApiWithoutMixingRuntimeIdentities()
    {
        var cli = new Codex(); var api = new Llm();
        var first = new KnowledgeModelAdapter(api, cli, new() { Generator = "codex", ModelId = "configured-cli" }, "fixture-runtime");
        await first.CompleteAsync("one", CancellationToken.None);
        await first.CompleteAsync("two", CancellationToken.None);
        await new KnowledgeModelAdapter(api, cli, new() { Generator = "codex" }, "second-runtime").CompleteAsync("three", CancellationToken.None);
        Assert.Equal("read-only", cli.Requests[0].CodexSandboxMode);
        Assert.Equal("configured-cli", cli.Requests[0].CodexModelId);
        Assert.Equal(cli.Requests[0].SessionId, cli.Requests[1].SessionId);
        Assert.NotEqual(cli.Requests[0].SessionId, cli.Requests[2].SessionId);
        Assert.Equal("fixture-runtime", cli.Requests[0].MaintenanceWorkspacePath);
        var result = await new KnowledgeModelAdapter(api, cli, new() { Generator = "llm_api", ModelId = "configured-api" }, "unused").CompleteAsync("api", CancellationToken.None);
        Assert.Equal("api", result.Provider);
        Assert.Equal("configured-api", api.ModelId);
        Assert.Equal(3, cli.Requests.Count);
    }

    [Fact]
    public async Task FailedRecompilePreservesSourceAndPreviousDraftAndRagVersion()
    {
        using var db = Database(); var kb = CreateBase(db);
        var root = Path.Combine(Path.GetTempPath(), "aiagent-knowledge-tests-" + Guid.NewGuid().ToString("N"));
        var paths = new KnowledgePathService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = root }).Build(), NullLogger<KnowledgePathService>.Instance);
        var sourcePath = Path.Combine(paths.GetRawPath(kb.Name), "source.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        const string source = "This is the original document evidence for the company project.";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, StoragePath = sourcePath, OriginalFileName = "source.txt" };
            doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
            var settings = new KnowledgeCompilerSettings(db); settings.Save(new() { Generator = "llm_api", MaxSteps = 8 });
            var api = new Llm();
            api.Replies.Enqueue("{\"action\":\"read_source\",\"part\":1}");
            api.Replies.Enqueue(JsonSerializer.Serialize(new { action = "propose_page", title = "Project", markdown = "Project facts", evidence = new[] { new { part = 1, quote = source } } }));
            api.Replies.Enqueue("{\"action\":\"finish\"}");
            var ingestion = new KnowledgeIngestionService(db, paths, null!, api, new Codex(), settings);
            var result = await ingestion.ProcessAsync(kb, doc, new() { Generator = "llm_api" }, CancellationToken.None);
            Assert.True(result.ParsedDocumentId > 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ingestion.ProcessAsync(kb, doc, new() { Generator = "llm_api" }, CancellationToken.None));
            Assert.Single(db.Queryable<AiKnowledgeArtifact>().ToList());
            Assert.Equal(result.ArtifactId, ingestion.GetContent(kb, doc).ArtifactId);
            Assert.Equal(source, await File.ReadAllTextAsync(sourcePath));
            var unchanged = db.Queryable<AiKnowledgeBase>().InSingle(kb.Id);
            Assert.Equal(99, unchanged.ActiveVersionId);
            Assert.Equal("ready", unchanged.Status);
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("aiagent-knowledge-tests-", StringComparison.Ordinal))
                Directory.Delete(resolved, true);
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("llm_api")]
    public async Task WikiSearchWorksWithoutIndexThroughBothModels(string generator)
    {
        using var db = Database(); var kb = CreateBase(db);
        db.Updateable<AiKnowledgeBase>().SetColumns(x => x.ActiveVersionId == null).Where(x => x.Id == kb.Id).ExecuteCommand();
        var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "source.txt" };
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        const string evidence = "This is the evidence preserved in the knowledge representation.";
        var artifact = new AiKnowledgeArtifact { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, Content = evidence, ReviewStatus = "draft" };
        artifact.Id = db.Insertable(artifact).ExecuteReturnBigIdentity();
        var config = new KnowledgeCompilerSettings(db); config.Save(new() { Generator = generator, MaxSteps = 8 });
        Assert.Equal("wiki", config.Get().RetrievalMode);
        var api = new Llm(); var cli = new Codex();
        var replies = generator == "codex" ? cli.Replies : api.Replies;
        replies.Enqueue(JsonSerializer.Serialize(new { action = "read", id = $"{artifact.Id}:0", part = 1 }));
        replies.Enqueue(JsonSerializer.Serialize(new { action = "cite", id = $"{artifact.Id}:0", quote = evidence }));
        replies.Enqueue("{\"action\":\"finish\"}");
        var root = Path.Combine(Path.GetTempPath(), "aiagent-knowledge-tests-" + Guid.NewGuid().ToString("N"));
        var paths = new KnowledgePathService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = root }).Build(), NullLogger<KnowledgePathService>.Instance);
        var service = new KnowledgeWikiRetrievalService(new(db), config, paths, api, cli);
        var search = await service.SearchAsync(kb.Name, "evidence", 5, default);
        Assert.Equal(evidence, Assert.Single(search.Citations).Text);
        Assert.Equal("draft", search.Citations[0].Metadata["review_status"]);
        Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
        if (generator == "codex")
        {
            Assert.All(cli.Requests, request => Assert.Equal("read-only", request.CodexSandboxMode));
            Assert.False(Directory.Exists(cli.Requests[0].MaintenanceWorkspacePath));
            Directory.Delete(Path.Combine(paths.RootPath, ".wiki-query"));
            Directory.Delete(paths.RootPath);
            Directory.Delete(root);
        }
        db.Updateable<AiKnowledgeDocument>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == doc.Id).ExecuteCommand();
        var empty = await service.SearchAsync(kb.Name, "evidence", 5, default);
        Assert.Empty(empty.Citations);
        Assert.Contains("提炼知识", empty.Content);
        config.Save(new() { RetrievalMode = "rag" });
        Assert.False(service.Enabled);
    }

    [Fact]
    public void OlderSettingsDefaultToWikiAndUnknownModeIsRejected()
    {
        Assert.Equal("wiki", JsonSerializer.Deserialize<KnowledgeCompilerSettingsDto>("{\"generator\":\"llm_api\"}")!.RetrievalMode);
        Assert.Throws<ArgumentException>(() => KnowledgeCompilerSettings.Validate(new() { RetrievalMode = "unknown" }));
    }

    [Fact]
    public async Task CreateAndUploadOnlySaveRawWithoutAnIndexRunner()
    {
        using var db = Database();
        var root = Path.Combine(Path.GetTempPath(), "aiagent-knowledge-tests-" + Guid.NewGuid().ToString("N"));
        var paths = new KnowledgePathService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = root }).Build(), NullLogger<KnowledgePathService>.Instance);
        try
        {
            var manager = new KnowledgeBaseManager(db, paths, NullLogger<KnowledgeBaseManager>.Instance);
            // No model or index dependencies are supplied: importing must not call them.
            var app = new KnowledgeAppService(db, new(db), new(db), null!, null!, manager, null!, null!, null!, null!, null!, null!, NullLogger<KnowledgeAppService>.Instance);
            using var first = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("First raw source."));
            var created = await app.CreateKnowledgeBase(new() { Name = "raw-only", Files = [new Microsoft.AspNetCore.Http.FormFile(first, 0, first.Length, "Files", "first.txt") { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "text/plain" }] }, default);
            Assert.Null(created.TaskId);
            Assert.Null(created.KnowledgeBase.ActiveVersionId);
            using var second = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Second raw source."));
            db.Updateable<AiKnowledgeBase>().SetColumns(x => x.ActiveVersionId == 99).Where(x => x.Name == "raw-only").ExecuteCommand();
            var uploaded = await app.UploadFiles("raw-only", [new Microsoft.AspNetCore.Http.FormFile(second, 0, second.Length, "Files", "second.txt") { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "text/plain" }], default);
            Assert.Null(uploaded.TaskId);
            Assert.Equal(99, uploaded.KnowledgeBase.ActiveVersionId);
            Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
            Assert.Equal(2, db.Queryable<AiKnowledgeDocument>().Count());
            Assert.Equal("First raw source.", await File.ReadAllTextAsync(Path.Combine(paths.GetRawPath("raw-only"), "first.txt")));
            Assert.Equal("Second raw source.", await File.ReadAllTextAsync(Path.Combine(paths.GetRawPath("raw-only"), "second.txt")));
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("aiagent-knowledge-tests-", StringComparison.Ordinal) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }
}
