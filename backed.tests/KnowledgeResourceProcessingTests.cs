using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public void ResourceProcessingListsEveryVisibleFileWithLatestWikiTaskAndPreservesBodyStatus()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db, "resource-store");
        AiKnowledgeDocument Document(string name, string uri, string status = "processed")
        {
            var document = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = name,
                ResourceUri = uri, Extension = ".md", Status = status };
            document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
            return document;
        }
        var old = Document("old.md", "viking://resources/old.md");
        var raw = Document("raw.md", "viking://resources/raw.md", "pending");
        var personal = Document("mine.md", "viking://user/current/mine.md");
        var hidden = Document("private.md", "viking://user/another/private.md");
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = old.Id,
            JobType = "wiki_compile", Status = "success", Stage = "completed" }).ExecuteCommand();
        var latest = new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = old.Id,
            JobType = "wiki_compile", Status = "error", Stage = "semantic", Progress = 35,
            ErrorMessage = "Synthetic semantic failure", Message = "正文已保存" };
        latest.Id = db.Insertable(latest).ExecuteReturnBigIdentity();
        // A recent unrelated task must not override the file's wiki task.
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = old.Id,
            JobType = "reindex", Status = "success" }).ExecuteCommand();
        for (var index = 0; index < 120; index++)
            db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = personal.Id,
                JobType = "wiki_compile", Status = "success", Stage = "completed" }).ExecuteCommand();
        db.Insertable(new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = hidden.Id,
            JobType = "wiki_compile", Status = "error", ErrorMessage = "Private failure" }).ExecuteCommand();

        using var worker = new KnowledgeCompilationWorker(db, new(db), null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var service = new KnowledgeResourceService(db, null!, null!, worker, new(db), null!, null!,
            NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        var rows = service.Processing();
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, row => row.Node.DocumentId == hidden.Id);
        var failed = Assert.Single(rows, row => row.Node.DocumentId == old.Id);
        Assert.Equal(latest.Id, failed.TaskId);
        Assert.Equal("error", failed.TaskStatus);
        Assert.Equal("semantic", failed.Stage);
        Assert.Equal("Synthetic semantic failure", failed.ErrorMessage);
        Assert.Equal("processed", failed.Node.Status);
        Assert.Null(Assert.Single(rows, row => row.Node.DocumentId == raw.Id).TaskId);
    }
}
