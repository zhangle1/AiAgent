using System.Text;
using System.IO;
using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Knowledge.KnowAgent;
using AiAgent.Backend.Services.TaskQueue;
using AiAgent.Backend.Services.TaskCenter;
using AiAgent.Backend.Services.Settings;
using AiAgent.Backend.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task DirectoryOverviewRefreshesAfterEverySiblingPublishesWithoutUsingAnotherTasksSkill()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        const string owner = "viking://user/current/", target = "viking://projects/BOS/";
        var a = files.Document(kb, "viking://resources/a.md"); a.Id = db.Insertable(a).ExecuteReturnBigIdentity();
        var b = files.Document(kb, "viking://resources/b.md"); b.Id = db.Insertable(b).ExecuteReturnBigIdentity();
        var first = new KnowledgeCompilerSettingsDto { TargetUri = target, SkillInstructions = "A 规则" };
        var second = new KnowledgeCompilerSettingsDto { TargetUri = target, SkillInstructions = "B 规则" };
        foreach (var (doc, config) in new[] { (a, first), (b, second) })
            db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = doc.Id, OwnerRoot = owner, JobType = "wiki_compile", ConfigurationJson = JsonSerializer.Serialize(config) }).ExecuteCommand();
        var api = new Llm(); var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, new Codex(), new(db));
        await ingestion.ParseResourceAsync(kb, a, new(), default); await ingestion.ParseResourceAsync(kb, b, new(), default);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, new Codex());
        api.Replies.Enqueue("# A\n\nA 摘要。"); api.Replies.Enqueue("# 目录\n\n仅 A 已完成。");
        await semantic.GenerateAsync(kb, a, first, (_, _) => { }, default, owner);
        Assert.Null(semantic.Read(KnowledgeResourceSemanticService.TargetUri(b.ResourceUri!, owner, target, b.Id)));
        api.Replies.Enqueue("# B\n\nB 摘要。"); api.Replies.Enqueue("# 目录\n\nA 和 B 均已完成。");
        await semantic.GenerateAsync(kb, b, second, (_, _) => { }, default, owner);
        Assert.Contains("A 和 B 均已完成", semantic.Read(target + "_compiled/")!.Overview);
        Assert.Equal(4, api.Inputs.Count);
        Assert.Contains("A 规则", api.Inputs[0][0].Content); Assert.Contains("B 规则", api.Inputs[2][0].Content);
    }
    private sealed class CompilationCatalog : IModelCatalogService
    {
        public ModelCatalog Load(bool redactSecrets = false) => new() { Services = new() { Llm = new() {
            ActiveModelId = "one", Profiles = [new() { Id = "fixture", Name = "Fixture", Models = [new() { Id = "one", Model = "fixture-model" }] }]
        } } };
        public ModelCatalog Save(ModelCatalog catalog) => throw new NotSupportedException();
        public ApplyResult Apply(ModelCatalog? catalog = null) => throw new NotSupportedException();
    }
    [Theory]
    [InlineData("viking://projects/BOS/")]
    [InlineData("viking://resources/")]
    [InlineData("viking://user/current/memory/")]
    public async Task CompilationPublishesToChosenDirectoryUsesSkillAndKeepsRawData(string target)
    {
        using var db = Database(); using var files = new SemanticFiles();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        if (target.Contains("BOS")) db.Insertable(new AiKnowledgeDirectory { Uri = target, ParentUri = "viking://projects/", Name = "BOS" }).ExecuteCommand();
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var api = new Llm(); var accessor = new HttpContextAccessor(); var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, new Codex(), settings);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, new Codex());
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance, semantic, accessor);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, api, new Codex(), NullLogger<KnowledgeBaseManager>.Instance, accessor, semantic);
        var skills = new KnowledgeCompilationSkillService(db, accessor, api);
        var selected = skills.Save(new() { Name = "BOS", Description = "业务规则", Instructions = "保留插件生命周期以及版本约束。" });
        var agent = new KnowledgeAgentService(new CompilationCatalog(), api, null!, settings, resources, skills);
        var queued = agent.Compile(new() { Uris = [doc.ResourceUri!], Configuration = new() { Generator = "llm_api", ModelId = "one", TargetUri = target,
            SkillId = selected.Id, SkillInstructions = "client text must not override saved skill" } });
        var job = db.Queryable<AiKnowledgeJob>().InSingle(Assert.Single(queued.Tasks).Id);
        var snapshot = JsonSerializer.Deserialize<KnowledgeCompilerSettingsDto>(job.ConfigurationJson!)!;
        Assert.Equal(target, snapshot.TargetUri); Assert.Equal(selected.Instructions, snapshot.SkillInstructions);
        selected.Instructions = "修改后的技能"; skills.Save(selected);
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        api.Replies.Enqueue("# BOS\n\n保留生命周期。"); api.Replies.Enqueue("# 目录\n\nBOS 知识概览。");
        await semantic.GenerateAsync(kb, doc, snapshot, (_, _) => { }, default, job.OwnerRoot);
        Assert.All(api.Inputs, input => Assert.Contains("保留插件生命周期以及版本约束", input[0].Content));
        var output = KnowledgeResourceSemanticService.TargetUri(doc.ResourceUri!, job.OwnerRoot, target, doc.Id);
        Assert.Contains(resources.Tree(), n => n.Uri == output);
        Assert.Contains(resources.Tree(), n => n.Uri == target + "_compiled/" && n.Name == "编译产物");
        Assert.Contains("保留生命周期", (await resources.ReadAsync(output, default)).SourceText);
        Assert.Contains((await resources.ReadAsync(doc.ResourceUri!, default)).CompiledOutputs, item => item.Uri == output);
        Assert.Equal("ready", (await resources.ReadAsync(target + "_compiled/", default)).SemanticStatus);
        Assert.Contains("每周三发布", File.ReadAllText(doc.StoragePath));
        Assert.Throws<ArgumentException>(() => agent.Compile(new() { Uris = [output], Configuration = snapshot }));
        db.Updateable<AiKnowledgeJob>().SetColumns(x => x.Status == "error").Where(x => x.Id == job.Id).ExecuteCommand();
        var retried = new TaskCenterService(db, worker).Retry("knowledge", job.Id);
        var retriedConfig = JsonSerializer.Deserialize<KnowledgeCompilerSettingsDto>(db.Queryable<AiKnowledgeJob>().InSingle(retried.Id).ConfigurationJson!)!;
        Assert.Equal(snapshot.SkillInstructions, retriedConfig.SkillInstructions); Assert.Equal(target, retriedConfig.TargetUri);
    }

    [Fact]
    public void CompilationRejectsForeignDestinationsAndPublicPublicationOfPrivateSources()
    {
        using var db = Database(); using var files = new SemanticFiles(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://user/current/private.md"); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var accessor = new HttpContextAccessor(); var api = new Llm(); var settings = new KnowledgeCompilerSettings(db);
        using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance, httpContextAccessor: accessor);
        var resources = new KnowledgeResourceService(db, files.Paths, null!, worker, settings, api, new Codex(), NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
        var agent = new KnowledgeAgentService(new CompilationCatalog(), api, null!, settings, resources, new(db, accessor, api));
        Assert.Throws<UnauthorizedAccessException>(() => resources.CompilationTarget("viking://user/bob/memory/"));
        Assert.Throws<ArgumentException>(() => resources.CompilationTarget("viking://projects/missing/"));
        Assert.Throws<ArgumentException>(() => agent.Compile(new() { Uris = [doc.ResourceUri!], Configuration = new() { ModelId = "one", TargetUri = "viking://resources/" } }));
        Assert.Throws<UnauthorizedAccessException>(() => worker.Enqueue(kb.Name, doc.Id, true, new() { TargetUri = "viking://resources/" }));
        Assert.Empty(db.Queryable<AiKnowledgeJob>().ToList());
    }

    [Fact]
    public async Task SkillConversationReturnsEditableDraftAndStoredSkillsAreOwnerScoped()
    {
        using var db = Database(); db.CodeFirst.InitTables(typeof(AiUser), typeof(AiUserSession));
        foreach (var name in new[] { "alice", "bob" })
        {
            db.Insertable(new AiUser { Id = name, Username = name }).ExecuteCommand();
            db.Insertable(new AiUserSession { UserId = name, TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name))), ExpiresAt = DateTime.UtcNow.AddDays(1) }).ExecuteCommand();
        }
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() }; accessor.HttpContext.Request.Headers.Authorization = "Bearer alice";
        var api = new Llm(); var skills = new KnowledgeCompilationSkillService(db, accessor, api);
        var builtin = skills.Require(null); Assert.True(builtin.Builtin); Assert.StartsWith("---\nname: llm-wiki", builtin.SkillMarkdown);
        api.Replies.Enqueue(JsonSerializer.Serialize(new { reply = "草稿已生成", skill = new { name = "业务技能", description = "保留业务规则", instructions = "保留实体、规则、来源。" } }));
        using var draftJson = JsonDocument.Parse(JsonSerializer.Serialize(await skills.DraftAsync(new() { ModelId = "one", Message = "生成业务技能" }, default)));
        var draft = draftJson.RootElement.GetProperty("skill").Deserialize<KnowledgeCompilationSkillDto>()!;
        Assert.Empty(draft.Id); Assert.Single(skills.List());
        var saved = skills.Save(draft); Assert.Equal(saved.Instructions, skills.Require(saved.Id).Instructions);
        Assert.Contains("description:", saved.SkillMarkdown);
        accessor.HttpContext.Request.Headers.Authorization = "Bearer bob";
        Assert.Single(skills.List()); Assert.Throws<ArgumentException>(() => skills.Require(saved.Id)); Assert.Throws<ArgumentException>(() => skills.Save(saved));
        accessor.HttpContext = null;
    }
}
