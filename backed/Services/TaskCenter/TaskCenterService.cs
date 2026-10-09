using AiAgent.Backend.Services.TaskQueue;
using AiAgent.Backend.Dtos.TaskCenter;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using SqlSugar;

namespace AiAgent.Backend.Services.TaskCenter;

/// <summary>Unified task-center seam. Knowledge is the first adapter; more task domains can join without changing the UI contract.</summary>
public interface ITaskCenterService
{
    TaskCenterResponseDto List(string? domain = null, string? status = null, int limit = 100);
    TaskCenterTaskDto Cancel(string domain, long id);
    TaskCenterTaskDto Retry(string domain, long id);
}

public sealed class TaskCenterService(ISqlSugarClient database, KnowledgeCompilationWorker knowledgeWorker) : ITaskCenterService
{
    public TaskCenterResponseDto List(string? domain = null, string? status = null, int limit = 100)
    {
        var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim().ToLowerInvariant();
        if (normalizedDomain is not null && normalizedDomain != "knowledge")
            return new TaskCenterResponseDto();

        knowledgeWorker.RetryPendingFinishes();
        using var db = database.CopyNew();
        var knowledgeBases = db.Queryable<AiKnowledgeBase>().Where(x => !x.IsDeleted).ToList();
        var visibleIds = knowledgeBases.Select(x => x.Id).ToList();
        List<AiKnowledgeJob> rows = visibleIds.Count == 0
            ? new List<AiKnowledgeJob>()
            : db.Queryable<AiKnowledgeJob>().Where(x => visibleIds.Contains(x.KnowledgeBaseId) && x.JobType == "wiki_compile")
                .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToList();
        var documents = rows.Where(x => x.DocumentId.HasValue).Select(x => x.DocumentId!.Value).Distinct().ToList();
        var documentRows = documents.Count == 0
            ? new List<AiKnowledgeDocument>()
            : db.Queryable<AiKnowledgeDocument>().Where(x => documents.Contains(x.Id)).ToList();
        foreach (var row in rows) knowledgeWorker.ApplyPendingFinish(row);
        var tasks = rows.Select(row => Map(row, knowledgeBases.FirstOrDefault(x => x.Id == row.KnowledgeBaseId), documentRows.FirstOrDefault(x => x.Id == row.DocumentId)))
            .ToList();
        return new TaskCenterResponseDto { Tasks = tasks.Where(row => string.IsNullOrWhiteSpace(status) ||
            row.Status.Equals(status, StringComparison.OrdinalIgnoreCase) || (status == "processing" && row.Status == "cancelling")).ToList(), Summary = Summarize(tasks) };
    }

    public TaskCenterTaskDto Cancel(string domain, long id)
    {
        EnsureKnowledge(domain);
        var task = Find(id);
        if (task.JobType != "wiki_compile") throw new InvalidOperationException("该知识任务暂不支持取消。");
        knowledgeWorker.Cancel(task.KnowledgeBaseName, id);
        return ReadTask(id);
    }

    public TaskCenterTaskDto Retry(string domain, long id)
    {
        EnsureKnowledge(domain);
        var task = Find(id);
        if (task.JobType != "wiki_compile" || !task.DocumentId.HasValue) throw new InvalidOperationException("该知识任务暂不支持重试。");
        var retried = knowledgeWorker.Enqueue(task.KnowledgeBaseName, task.DocumentId.Value);
        return ReadTask(retried.Id);
    }

    private TaskCenterTaskDto ReadTask(long id)
    {
        var task = Find(id);
        using var db = database.CopyNew();
        knowledgeWorker.ApplyPendingFinish(task.Job);
        return Map(task.Job, db.Queryable<AiKnowledgeBase>().InSingle(task.Job.KnowledgeBaseId),
            task.DocumentId is {} documentId ? db.Queryable<AiKnowledgeDocument>().InSingle(documentId) : null);
    }

    private AiKnowledgeJobWithName Find(long id)
    {
        knowledgeWorker.RetryPendingFinishes();
        using var db = database.CopyNew();
        var row = db.Queryable<AiKnowledgeJob>().Where(x => x.Id == id).First() ?? throw new KeyNotFoundException("后台任务不存在。");
        var kb = db.Queryable<AiKnowledgeBase>().Where(x => x.Id == row.KnowledgeBaseId && !x.IsDeleted).First() ?? throw new KeyNotFoundException("任务所属知识库不存在。");
        return new(row, kb.Name);
    }

    private static TaskCenterTaskDto Map(AiKnowledgeJob row, AiKnowledgeBase? kb, AiKnowledgeDocument? document) => new()
    {
        Id = row.Id,
        Domain = "knowledge",
        TaskType = row.JobType,
        Title = document is null ? $"知识库 {kb?.DisplayName ?? row.KnowledgeBaseId.ToString()}" : $"{kb?.DisplayName ?? "知识库"} / {document.OriginalFileName}",
        ResourceId = document is null ? kb?.Name : $"{kb?.Name}/{document.Id}",
        ResourceUri = kb is null ? null : document is null ? KnowledgeContextUri.Root(kb.Name) : KnowledgeContextUri.Document(document, kb.Name),
        Status = NormalizeStatus(row.Status),
        Stage = row.Stage,
        Progress = row.Progress,
        Message = row.Message,
        ErrorMessage = row.ErrorMessage,
        CreatedAt = row.CreatedAt,
        StartedAt = row.StartedAt,
        UpdatedAt = row.UpdatedAt,
        FinishedAt = row.FinishedAt,
        Cancellable = row.Status is "queued" or "processing" or "cancelling",
        Retryable = document is { IsDeleted: false } && row.Status is ("error" or "cancelled" or "success")
    };

    private static string NormalizeStatus(string status) => status switch
    {
        "success" => "completed",
        "error" => "failed",
        _ => status
    };

    private static TaskCenterSummaryDto Summarize(IReadOnlyList<TaskCenterTaskDto> tasks) => new()
    {
        Total = tasks.Count,
        Queued = tasks.Count(x => x.Status == "queued"),
        Processing = tasks.Count(x => x.Status is "processing" or "cancelling"),
        Completed = tasks.Count(x => x.Status == "completed"),
        Failed = tasks.Count(x => x.Status == "failed"),
        Cancelled = tasks.Count(x => x.Status == "cancelled")
    };

    private static void EnsureKnowledge(string domain)
    {
        if (!string.Equals(domain, "knowledge", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("暂不支持该任务域。");
    }

    private sealed record AiKnowledgeJobWithName(AiKnowledgeJob Job, string KnowledgeBaseName)
    {
        public long Id => Job.Id;
        public string JobType => Job.JobType;
        public long? DocumentId => Job.DocumentId;
    }
}
