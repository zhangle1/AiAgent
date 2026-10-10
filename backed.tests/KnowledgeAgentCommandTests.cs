using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Knowledge.KnowAgent;
using AiAgent.Backend.Services.TaskCenter;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task KnowledgeCommandsListRealChildrenAndDenyFilesOutsideTheSelectedSubtree()
    {
        using var db = Database(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db, "resource-store");
        foreach (var uri in new[] { "viking://resources/docs/a.md", "viking://resources/other/b.md", "viking://user/bob/private.md" })
            db.Insertable(new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = uri.Split('/').Last(), Extension = ".md", ResourceUri = uri }).ExecuteCommand();
        using var worker = new KnowledgeCompilationWorker(db, new(db), null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var resources = new KnowledgeResourceService(db, null!, null!, worker, new(db), null!, null!, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        var tools = new KnowledgeAgentTools(resources);
        using var listing = JsonDocument.Parse(await tools.ExecuteAsync("viking://resources/docs/", new() { Command = "ls" }, default));
        Assert.Equal(1, listing.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("a.md", listing.RootElement.GetProperty("nodes")[0].GetProperty("name").GetString());
        using var alias = JsonDocument.Parse(await tools.ExecuteAsync("viking://user/current/", new() { Command = "ls", Uri = "viking://user/", TargetUri = "viking://user/" }, default));
        Assert.Equal("viking://user/current/", alias.RootElement.GetProperty("uri").GetString());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ExecuteAsync("viking://resources/docs/", new() { Command = "read", TargetUri = "viking://resources/other/b.md" }, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => tools.ExecuteAsync("viking://resources/docs/", new() { Command = "read", TargetUri = "viking://user/bob/private.md" }, default));
    }

    [Fact]
    public void CompileConfigurationIsFrozenAndTaskCenterRetryKeepsTheOriginalSelection()
    {
        using var db = Database(); var kb = CreateBase(db);
        var document = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "source.md" };
        document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
        using var worker = new KnowledgeCompilationWorker(db, new(db), null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var configuration = new KnowledgeCompilerSettingsDto { Generator = "codex", ModelId = "profile-team", ReasoningEffort = "low", MaxSteps = 16, TimeoutMinutes = 7 };
        var first = worker.Enqueue(kb.Name, document.Id, true, configuration);
        configuration.ModelId = "changed-after-queue";
        Assert.Throws<InvalidOperationException>(() => worker.Enqueue(kb.Name, document.Id, true, configuration));
        db.Updateable<AiKnowledgeJob>().SetColumns(x => x.Status == "error").Where(x => x.Id == first.Id).ExecuteCommand();
        var retry = new TaskCenterService(db, worker).Retry("knowledge", first.Id);
        var stored = db.Queryable<AiKnowledgeJob>().InSingle(retry.Id);
        var snapshot = JsonSerializer.Deserialize<KnowledgeCompilerSettingsDto>(stored.ConfigurationJson!)!;
        Assert.Equal("profile-team", snapshot.ModelId);
        Assert.Equal("codex", snapshot.Generator);
        Assert.Equal("low", snapshot.ReasoningEffort);
        Assert.Equal(16, snapshot.MaxSteps);
        Assert.Equal(7, snapshot.TimeoutMinutes);
        Assert.True(stored.ParseOnly);
        Assert.NotEqual(first.Id, retry.Id);
    }
}
