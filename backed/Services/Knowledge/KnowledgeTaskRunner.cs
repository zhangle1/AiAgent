using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Rag;
using System.Text.Json;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>
/// 知识库后台索引任务启动器。
/// </summary>
public interface IKnowledgeTaskRunner
{
    /// <summary>
    /// 创建任务记录并在后台执行索引动作。
    /// </summary>
    AiKnowledgeJob StartIndexTask(AiKnowledgeBase kb, IReadOnlyList<AiKnowledgeDocument> documents, string action);
}

/// <summary>
/// 知识库后台索引任务启动器实现。
/// </summary>
public sealed class KnowledgeTaskRunner : IKnowledgeTaskRunner
{
    private readonly IKnowledgeBaseManager _manager;
    private readonly IKnowledgeProviderConfigService _providerConfigService;
    private readonly IKnowledgePathService _paths;
    private readonly IKnowledgeProgressHub _progressHub;
    private readonly IRagService _ragService;
    private readonly IKnowledgeIndexMaterializer _materializer;
    private readonly ILogger<KnowledgeTaskRunner> _logger;

    /// <summary>
    /// 初始化知识库后台任务调度器，用于异步执行索引初始化、增量追加和重建。
    /// </summary>
    public KnowledgeTaskRunner(
        IKnowledgeBaseManager manager,
        IKnowledgeProviderConfigService providerConfigService,
        IKnowledgePathService paths,
        IKnowledgeProgressHub progressHub,
        IRagService ragService,
        IKnowledgeIndexMaterializer materializer,
        ILogger<KnowledgeTaskRunner> logger)
    {
        _manager = manager;
        _providerConfigService = providerConfigService;
        _paths = paths;
        _progressHub = progressHub;
        _ragService = ragService;
        _materializer = materializer;
        _logger = logger;
    }

    /// <summary>
    /// 启动初始化、上传后重建或手动重建索引任务。
    /// </summary>
    public AiKnowledgeJob StartIndexTask(AiKnowledgeBase kb, IReadOnlyList<AiKnowledgeDocument> documents, string action)
    {
        var retrieval = _providerConfigService.GetRetrievalOptions(kb.EngineType);
        var version = _manager.CreateIndexVersion(kb, kb.EngineType, "", JsonSerializer.Serialize(retrieval));
        var storagePath = version.StoragePath ?? _paths.GetVersionPath(kb.Name, version.VersionNo, kb.EngineType);
        var job = _manager.CreateJob(kb, action, version.Id);
        _manager.UpdateKnowledgeBaseStatus(kb.Id, action == "reindex" ? "processing" : "initializing");
        _ = PublishProgressAsync(kb.Name, kb.Id, "queued");

        _ = Task.Run(async () =>
        {
            try
            {
                _manager.MarkJobRunning(job.Id, "Indexing documents.");
                await PublishProgressAsync(kb.Name, kb.Id, "started");
                var request = new RagIndexRequest
                {
                    KnowledgeBaseName = kb.Name,
                    PersistDir = storagePath,
                    FilePaths = documents.Select(x => x.StoragePath).ToList(),
                    Retrieval = retrieval
                };

                _manager.MarkJobProgress(job.Id, 35, $"Preparing {documents.Count} documents.");
                await PublishProgressAsync(kb.Name, kb.Id, "preparing");
                var result = action switch
                {
                    "reindex" => await _ragService.ReindexAsync(kb.EngineType, request, progress => OnRagProgressAsync(kb.Name, kb.Id, job.Id, progress)),
                    "upload" => await _ragService.ReindexAsync(kb.EngineType, request, progress => OnRagProgressAsync(kb.Name, kb.Id, job.Id, progress)),
                    _ => await _ragService.InitializeAsync(kb.EngineType, request, progress => OnRagProgressAsync(kb.Name, kb.Id, job.Id, progress))
                };
                _manager.MarkJobProgress(job.Id, 85, "Activating index version.");
                await PublishProgressAsync(kb.Name, kb.Id, "activating");

                if (!result.Ok)
                {
                    var message = result.ErrorMessage ?? "RAG pipeline failed.";
                    _manager.MarkJobError(job.Id, message);
                    _manager.UpdateKnowledgeBaseStatus(kb.Id, "error", message);
                    await PublishProgressAsync(kb.Name, kb.Id, "error");
                    return;
                }

                _manager.MarkJobProgress(job.Id, 92, "Importing structured chunks.");
                await PublishProgressAsync(kb.Name, kb.Id, "materializing");
                var materializedChunkCount = _materializer.ImportChunks(kb, version, documents);
                var chunkCount = materializedChunkCount > 0 ? materializedChunkCount : result.ChunkCount;

                _manager.ActivateVersion(kb.Id, version.Id, result.DocumentCount, chunkCount);
                _manager.MarkJobSuccess(job.Id, "Indexing completed.");
                await PublishProgressAsync(kb.Name, kb.Id, "success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge indexing task failed. Kb={KbName}, JobId={JobId}", kb.Name, job.Id);
                _manager.MarkJobError(job.Id, ex.Message);
                _manager.UpdateKnowledgeBaseStatus(kb.Id, "error", ex.Message);
                await PublishProgressAsync(kb.Name, kb.Id, "error");
            }
        });

        return job;
    }

    private async Task OnRagProgressAsync(string kbName, long knowledgeBaseId, long jobId, RagProgressEvent progress)
    {
        var value = Math.Clamp(progress.Progress, 0, 99);
        var message = string.IsNullOrWhiteSpace(progress.Message)
            ? $"Indexing documents ({progress.Stage})."
            : progress.Message;
        _manager.MarkJobProgress(jobId, value, message);
        await PublishProgressAsync(kbName, knowledgeBaseId, progress.Stage);
    }

    private async Task PublishProgressAsync(string kbName, long knowledgeBaseId, string eventType)
    {
        var normalizedName = kbName.Trim().ToLowerInvariant();
        var job = _manager.GetLatestJob(knowledgeBaseId);
        await _progressHub.PublishAsync(normalizedName, job, eventType);
    }
}