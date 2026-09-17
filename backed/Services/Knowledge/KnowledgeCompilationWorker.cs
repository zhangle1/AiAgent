using System.Threading.Channels;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Serial compilation queue, independent from the existing RAG index worker.</summary>
public sealed class KnowledgeCompilationWorker(ISqlSugarClient db, KnowledgeCompilerSettings settings,
    IKnowledgeIngestionService ingestion, ILogger<KnowledgeCompilationWorker> logger) : BackgroundService
{
    private readonly object _sync = new();
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(32) { SingleReader = true });

    public KnowledgeCompilationJobDto Enqueue(string name, long documentId)
    {
        var kb = FindBase(name);
        var document = db.Queryable<AiKnowledgeDocument>().Where(x => x.Id == documentId && x.KnowledgeBaseId == kb.Id && !x.IsDeleted).First()
            ?? throw new InvalidOperationException("Knowledge document does not exist.");
        var config = settings.Get();
        KnowledgeCompilerSettings.Validate(config);
        lock (_sync)
        {
            var active = db.Queryable<AiKnowledgeJob>().Where(x => x.KnowledgeBaseId == kb.Id && x.DocumentId == documentId &&
                x.JobType == "wiki_compile" && (x.Status == "queued" || x.Status == "processing")).First();
            if (active is not null) return Map(active);
            var job = new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = documentId, JobType = "wiki_compile", Message = "等待提炼" };
            job.Id = db.Insertable(job).ExecuteReturnBigIdentity();
            if (!_queue.Writer.TryWrite(new Work(kb, document, config, job.Id)))
            {
                Finish(job.Id, "error", "提炼队列已满，请稍后重试。");
                throw new InvalidOperationException("Compilation queue is full; retry later.");
            }
            return Map(job);
        }
    }

    public KnowledgeCompilationJobDto? Latest(string name, long documentId)
    {
        var kb = FindBase(name);
        var job = db.Queryable<AiKnowledgeJob>().Where(x => x.KnowledgeBaseId == kb.Id && x.DocumentId == documentId && x.JobType == "wiki_compile")
            .OrderByDescending(x => x.Id).First();
        return job is null ? null : Map(job);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Startup schema initialization runs before hosted services. Interrupted work is explicit and retryable.
        db.Updateable<AiKnowledgeJob>().SetColumns(x => new AiKnowledgeJob {
            Status = "error", Message = "服务重启中断了提炼，请重新提炼。", FinishedAt = DateTime.UtcNow
        }).Where(x => x.JobType == "wiki_compile" && (x.Status == "queued" || x.Status == "processing")).ExecuteCommand();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                db.Updateable<AiKnowledgeJob>().SetColumns(x => new AiKnowledgeJob {
                    Status = "processing", Progress = 10, Message = "解析原文并提炼知识", StartedAt = DateTime.UtcNow
                }).Where(x => x.Id == work.JobId).ExecuteCommand();
                if (!db.Queryable<AiKnowledgeBase>().Any(x => x.Id == work.Base.Id && !x.IsDeleted) ||
                    !db.Queryable<AiKnowledgeDocument>().Any(x => x.Id == work.Document.Id && !x.IsDeleted))
                    throw new InvalidOperationException("The source was deleted before compilation.");
                await ingestion.ProcessAsync(work.Base, work.Document, new KnowledgeProcessRequest {
                    Generator = work.Config.Generator, ModelId = work.Config.ModelId, ReasoningEffort = work.Config.ReasoningEffort
                }, stoppingToken, work.Config);
                Finish(work.JobId, "success", "提炼完成，可在知识标签中核对草稿。");
            }
            catch (Exception ex)
            {
                // Do not log model responses or source contents.
                logger.LogWarning("Knowledge compilation {JobId} failed ({ErrorType}).", work.JobId, ex.GetType().Name);
                Finish(work.JobId, "error", ex is OperationCanceledException ? "提炼超时或服务停止，请重新提炼。" : "提炼失败，请检查模型配置、文件格式或拆分过长资料后重试。");
            }
        }
    }

    private void Finish(long id, string status, string message) => db.Updateable<AiKnowledgeJob>()
        .SetColumns(x => new AiKnowledgeJob { Status = status, Progress = status == "success" ? 100 : 0, Message = message, FinishedAt = DateTime.UtcNow })
        .Where(x => x.Id == id).ExecuteCommand();

    private AiKnowledgeBase FindBase(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return db.Queryable<AiKnowledgeBase>().Where(x => x.Name == normalized && !x.IsDeleted).First()
            ?? throw new InvalidOperationException("Knowledge base does not exist.");
    }
    private static KnowledgeCompilationJobDto Map(AiKnowledgeJob job) => new() {
        Id = job.Id, DocumentId = job.DocumentId, Status = job.Status, Progress = job.Progress, Message = job.Message
    };
    private sealed record Work(AiKnowledgeBase Base, AiKnowledgeDocument Document, KnowledgeCompilerSettingsDto Config, long JobId);
}
