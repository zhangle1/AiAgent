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
        public List<ChatCompleteRequest> Requests { get; } = [];
        public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
        {
            Requests.Add(request); return Task.FromResult(new ChatCompleteResponse { Content = "{}", Model = "cli-model" });
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
}
