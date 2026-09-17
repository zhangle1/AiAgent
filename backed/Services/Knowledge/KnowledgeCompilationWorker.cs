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
    private readonly Dictionary<long, CancellationTokenSource> _cancellations = new();
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
                x.JobType == "wiki_compile" && (x.Status == "queued" || x.Status == "processing" || x.Status == "cancelling")).First();
            if (active is not null) return Map(active);
            var job = new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = documentId, JobType = "wiki_compile", Message = "等待提炼" };
            job.Id = db.Insertable(job).ExecuteReturnBigIdentity();
            _cancellations[job.Id] = new CancellationTokenSource();
            if (!_queue.Writer.TryWrite(new Work(kb, document, config, job.Id)))
            {
                _cancellations.Remove(job.Id, out var rejected);
                rejected?.Dispose();
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
        }).Where(x => x.JobType == "wiki_compile" && (x.Status == "queued" || x.Status == "processing" || x.Status == "cancelling")).ExecuteCommand();
        return base.StartAsync(cancellationToken);
    }

    public IReadOnlyList<KnowledgeCompilationJobDto> List()
    {
        var visible = db.Queryable<AiKnowledgeBase>().Where(x => !x.IsDeleted).Select(x => x.Id).ToList();
        var active = db.Queryable<AiKnowledgeJob>().Where(x => visible.Contains(x.KnowledgeBaseId) && x.JobType == "wiki_compile" &&
            (x.Status == "queued" || x.Status == "processing" || x.Status == "cancelling")).OrderBy(x => x.Id).ToList();
        var recent = db.Queryable<AiKnowledgeJob>().Where(x => visible.Contains(x.KnowledgeBaseId) && x.JobType == "wiki_compile" &&
            x.Status != "queued" && x.Status != "processing" && x.Status != "cancelling").OrderByDescending(x => x.Id).Take(20).ToList();
        return active.Concat(recent).Select(Map).ToList();
    }

    public KnowledgeCompilationJobDto Cancel(string name, long id)
    {
        var kb = FindBase(name);
        lock (_sync)
        {
            var job = db.Queryable<AiKnowledgeJob>().Where(x => x.Id == id && x.KnowledgeBaseId == kb.Id && x.JobType == "wiki_compile").First()
                ?? throw new KeyNotFoundException("Compilation task does not exist.");
            if (job.Status is not ("queued" or "processing" or "cancelling")) return Map(job);
            if (job.Status == "queued" || !_cancellations.TryGetValue(id, out _))
                Finish(id, "cancelled", "已取消，可重新提炼。");
            else
                db.Updateable<AiKnowledgeJob>().SetColumns(x => new AiKnowledgeJob {
                    Status = "cancelling", Message = "正在停止提炼…", UpdatedAt = DateTime.UtcNow
                }).Where(x => x.Id == id).ExecuteCommand();
            if (_cancellations.TryGetValue(id, out var source)) source.Cancel();
            return Map(db.Queryable<AiKnowledgeJob>().InSingle(id));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            CancellationTokenSource? source;
            lock (_sync)
            {
                _cancellations.TryGetValue(work.JobId, out source);
                if (source is null || source.IsCancellationRequested)
                {
                    _cancellations.Remove(work.JobId);
                    source?.Dispose();
                    continue;
                }
                db.Updateable<AiKnowledgeJob>().SetColumns(x => new AiKnowledgeJob {
                    Status = "processing", Progress = 1, Message = "准备提炼", StartedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                }).Where(x => x.Id == work.JobId).ExecuteCommand();
            }
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, source.Token);
            execution.CancelAfter(TimeSpan.FromMinutes(work.Config.TimeoutMinutes));
            try
            {
                if (!db.Queryable<AiKnowledgeBase>().Any(x => x.Id == work.Base.Id && !x.IsDeleted) ||
                    !db.Queryable<AiKnowledgeDocument>().Any(x => x.Id == work.Document.Id && !x.IsDeleted))
                    throw new InvalidOperationException("The source was deleted before compilation.");
                await ingestion.ProcessAsync(work.Base, work.Document, new KnowledgeProcessRequest {
                    Generator = work.Config.Generator, ModelId = work.Config.ModelId, ReasoningEffort = work.Config.ReasoningEffort
                }, execution.Token, work.Config, (value, message) =>
                    db.Updateable<AiKnowledgeJob>().SetColumns(x => new AiKnowledgeJob {
                        Progress = value, Message = message, UpdatedAt = DateTime.UtcNow
                    }).Where(x => x.Id == work.JobId && x.Status == "processing").ExecuteCommand());
                Finish(work.JobId, "success", "提炼完成，可在知识标签中核对草稿。");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Knowledge compilation {JobId} failed ({ErrorType}).", work.JobId, ex.GetType().Name);
                var cancelled = source.IsCancellationRequested;
                Finish(work.JobId, cancelled ? "cancelled" : "error", cancelled ? "已取消，可重新提炼。" :
                    ex is OperationCanceledException ? "提炼超时或服务停止，请检查模型连接或拆分资料后重试。" :
                    "提炼失败，请检查模型配置、文件格式或拆分过长资料后重试。");
            }
            finally
            {
                lock (_sync) { _cancellations.Remove(work.JobId); source.Dispose(); }
            }
        }
    }

    private void Finish(long id, string status, string message)
    {
        // Serialize completion with Cancel's read/update so a completed job cannot return to cancelling.
        lock (_sync)
            db.Updateable<AiKnowledgeJob>()
                .SetColumns(x => new AiKnowledgeJob { Status = status, Progress = status == "success" ? 100 : x.Progress, Message = message, FinishedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow })
                .Where(x => x.Id == id).ExecuteCommand();
    }

    private AiKnowledgeBase FindBase(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return db.Queryable<AiKnowledgeBase>().Where(x => x.Name == normalized && !x.IsDeleted).First()
            ?? throw new InvalidOperationException("Knowledge base does not exist.");
    }
    private KnowledgeCompilationJobDto Map(AiKnowledgeJob job) => new() {
        Id = job.Id, DocumentId = job.DocumentId, Status = job.Status, Progress = job.Progress, Message = job.Message,
        KnowledgeBaseName = db.Queryable<AiKnowledgeBase>().InSingle(job.KnowledgeBaseId)?.Name ?? "",
        DocumentName = job.DocumentId is {} documentId ? db.Queryable<AiKnowledgeDocument>().InSingle(documentId)?.OriginalFileName : null,
        CreatedAt = job.CreatedAt, StartedAt = job.StartedAt, FinishedAt = job.FinishedAt, UpdatedAt = job.UpdatedAt
    };
    private sealed record Work(AiKnowledgeBase Base, AiKnowledgeDocument Document, KnowledgeCompilerSettingsDto Config, long JobId);
}
