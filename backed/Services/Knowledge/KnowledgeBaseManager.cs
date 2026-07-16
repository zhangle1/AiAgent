using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;
using System.Diagnostics;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>
/// 知识库数据库与目录管理服务。
/// </summary>
public interface IKnowledgeBaseManager
{
    /// <summary>
    /// 查询知识库列表。
    /// </summary>
    List<KnowledgeBaseDto> ListKnowledgeBases();

    /// <summary>
    /// 查询知识库详情。
    /// </summary>
    KnowledgeDetailDto GetKnowledgeBase(string name);

    /// <summary>
    /// 创建知识库基础记录和目录。
    /// </summary>
    AiKnowledgeBase CreateKnowledgeBase(string name, string? displayName, string? description, string? provider);

    /// <summary>
    /// 保存上传文件并写入文档记录。
    /// </summary>
    Task<List<AiKnowledgeDocument>> SaveDocumentsAsync(AiKnowledgeBase kb, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建新的索引版本记录。
    /// </summary>
    AiKnowledgeIndexVersion CreateIndexVersion(AiKnowledgeBase kb, string provider, string storagePath, string? chunkConfigJson = null);

    /// <summary>
    /// 创建后台任务记录。
    /// </summary>
    AiKnowledgeJob CreateJob(AiKnowledgeBase kb, string jobType, long? indexVersionId = null);

    /// <summary>
    /// 获取知识库最近一次后台任务。
    /// </summary>
    KnowledgeJobDto? GetLatestJob(long knowledgeBaseId);

    /// <summary>
    /// 标记任务开始执行。
    /// </summary>
    void MarkJobRunning(long jobId, string message);

    void MarkJobProgress(long jobId, int progress, string message);

    /// <summary>
    /// 标记任务成功完成。
    /// </summary>
    void MarkJobSuccess(long jobId, string message);

    /// <summary>
    /// 标记任务失败。
    /// </summary>
    void MarkJobError(long jobId, string message);

    /// <summary>
    /// 激活索引版本并更新知识库状态。
    /// </summary>
    void ActivateVersion(long knowledgeBaseId, long versionId, int documentCount, int chunkCount);

    /// <summary>
    /// 更新知识库状态。
    /// </summary>
    void UpdateKnowledgeBaseStatus(long knowledgeBaseId, string status, string? errorMessage = null);

    /// <summary>
    /// 删除知识库记录和本地目录。
    /// </summary>
    void DeleteKnowledgeBase(string name);
}

/// <summary>
/// 知识库数据库与文件目录管理实现。
/// </summary>
public sealed class KnowledgeBaseManager : IKnowledgeBaseManager
{
    private readonly ISqlSugarClient _db;
    private readonly IKnowledgePathService _paths;
    private readonly ILogger<KnowledgeBaseManager> _logger;

    /// <summary>
    /// 初始化知识库领域管理器，负责知识库、文档、版本和任务的数据库读写。
    /// </summary>
    public KnowledgeBaseManager(ISqlSugarClient db, IKnowledgePathService paths, ILogger<KnowledgeBaseManager> logger)
    {
        _db = db;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// 查询知识库列表，并附带最近任务信息。
    /// </summary>
    public List<KnowledgeBaseDto> ListKnowledgeBases()
    {
        var rows = _db.Queryable<AiKnowledgeBase>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// 查询知识库详情和文档列表。
    /// </summary>
    public KnowledgeDetailDto GetKnowledgeBase(string name)
    {
        var kb = FindKnowledgeBase(name);
        var dto = new KnowledgeDetailDto
        {
            Id = kb.Id,
            Name = kb.Name,
            DisplayName = kb.DisplayName,
            Description = kb.Description,
            EngineType = kb.EngineType,
            Status = kb.Status,
            IsDefault = kb.IsDefault,
            DocumentCount = kb.DocumentCount,
            ActiveVersionId = kb.ActiveVersionId,
            CreatedAt = kb.CreatedAt,
            UpdatedAt = kb.UpdatedAt,
            LatestJob = GetLatestJob(kb.Id),
            Documents = _db.Queryable<AiKnowledgeDocument>()
                .Where(x => x.KnowledgeBaseId == kb.Id && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(ToDocumentDto)
                .ToList()
        };

        return dto;
    }

    /// <summary>
    /// 创建知识库基础记录和 raw 目录。
    /// </summary>
    public AiKnowledgeBase CreateKnowledgeBase(string name, string? displayName, string? description, string? provider)
    {
        var normalizedName = _paths.NormalizeName(name);
        var exists = _db.Queryable<AiKnowledgeBase>().Any(x => x.Name == normalizedName && !x.IsDeleted);
        if (exists)
        {
            throw new InvalidOperationException($"Knowledge base '{normalizedName}' already exists.");
        }

        Directory.CreateDirectory(_paths.GetRawPath(normalizedName));
        var kb = new AiKnowledgeBase
        {
            Name = normalizedName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim(),
            Description = description?.Trim(),
            EngineType = NormalizeProvider(provider),
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        kb.Id = _db.Insertable(kb).ExecuteReturnBigIdentity();
        return kb;
    }

    /// <summary>
    /// 保存上传文件，生成文档记录并更新文档数量。
    /// </summary>
    public async Task<List<AiKnowledgeDocument>> SaveDocumentsAsync(AiKnowledgeBase kb, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new List<AiKnowledgeDocument>();
        foreach (var file in files.Where(x => x.Length > 0))
        {
            var fileStopwatch = Stopwatch.StartNew();
            var saved = await _paths.SaveFileAsync(kb.Name, file, cancellationToken);
            _logger.LogInformation("Knowledge document file persisted. Kb={KbName}, File={FileName}, ElapsedMs={ElapsedMs}", kb.Name, file.FileName, fileStopwatch.ElapsedMilliseconds);
            var document = new AiKnowledgeDocument
            {
                KnowledgeBaseId = kb.Id,
                FileName = Path.GetFileName(saved.StoragePath),
                OriginalFileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                Extension = Path.GetExtension(file.FileName),
                FileSize = saved.FileSize,
                FileHash = saved.FileHash,
                StoragePath = saved.StoragePath,
                Status = "uploaded",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            document.Id = _db.Insertable(document).ExecuteReturnBigIdentity();
            _logger.LogInformation("Knowledge document row inserted. Kb={KbName}, File={FileName}, DocumentId={DocumentId}, ElapsedMs={ElapsedMs}", kb.Name, file.FileName, document.Id, fileStopwatch.ElapsedMilliseconds);
            result.Add(document);
        }

        if (result.Count > 0)
        {
            var documentCount = _db.Queryable<AiKnowledgeDocument>()
                .Count(x => x.KnowledgeBaseId == kb.Id && !x.IsDeleted);
            _db.Updateable<AiKnowledgeBase>()
                .SetColumns(x => new AiKnowledgeBase
                {
                    DocumentCount = documentCount,
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == kb.Id)
                .ExecuteCommand();
            kb.DocumentCount = documentCount;
        }

        _logger.LogInformation("Knowledge documents saved. Kb={KbName}, FileCount={FileCount}, SavedCount={SavedCount}, ElapsedMs={ElapsedMs}", kb.Name, files.Count, result.Count, stopwatch.ElapsedMilliseconds);
        return result;
    }

    /// <summary>
    /// 创建索引版本记录，并计算 provider 对应的持久化目录。
    /// </summary>
    public AiKnowledgeIndexVersion CreateIndexVersion(AiKnowledgeBase kb, string provider, string storagePath, string? chunkConfigJson = null)
    {
        var nextVersion = _db.Queryable<AiKnowledgeIndexVersion>()
            .Where(x => x.KnowledgeBaseId == kb.Id)
            .Max(x => x.VersionNo) + 1;
        var versionNo = nextVersion <= 0 ? 1 : nextVersion;
        var version = new AiKnowledgeIndexVersion
        {
            KnowledgeBaseId = kb.Id,
            VersionNo = versionNo,
            Status = "building",
            EngineType = NormalizeProvider(provider),
            ChunkConfigJson = chunkConfigJson,
            StoragePath = _paths.GetVersionPath(kb.Name, versionNo, NormalizeProvider(provider)),
            CreatedAt = DateTime.UtcNow
        };
        version.Id = _db.Insertable(version).ExecuteReturnBigIdentity();
        return version;
    }

    /// <summary>
    /// 创建后台索引任务记录。
    /// </summary>
    public AiKnowledgeJob CreateJob(AiKnowledgeBase kb, string jobType, long? indexVersionId = null)
    {
        var job = new AiKnowledgeJob
        {
            KnowledgeBaseId = kb.Id,
            IndexVersionId = indexVersionId,
            JobType = jobType,
            Status = "queued",
            Progress = 0,
            Message = "Queued",
            CreatedAt = DateTime.UtcNow
        };
        job.Id = _db.Insertable(job).ExecuteReturnBigIdentity();
        return job;
    }

    /// <summary>
    /// 获取最近任务，供列表和进度接口使用。
    /// </summary>
    public KnowledgeJobDto? GetLatestJob(long knowledgeBaseId)
    {
        var job = _db.Queryable<AiKnowledgeJob>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id)
            .First();
        return job is null ? null : ToJobDto(job);
    }

    /// <summary>
    /// 将任务状态更新为处理中。
    /// </summary>
    public void MarkJobRunning(long jobId, string message)
    {
        _db.Updateable<AiKnowledgeJob>()
            .SetColumns(x => new AiKnowledgeJob
            {
                Status = "processing",
                Progress = 10,
                Message = message,
                StartedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == jobId)
            .ExecuteCommand();
    }

    public void MarkJobProgress(long jobId, int progress, string message)
    {
        _db.Updateable<AiKnowledgeJob>()
            .SetColumns(x => new AiKnowledgeJob
            {
                Status = "processing",
                Progress = Math.Clamp(progress, 0, 99),
                Message = message
            })
            .Where(x => x.Id == jobId)
            .ExecuteCommand();
    }

    /// <summary>
    /// 将任务状态更新为成功。
    /// </summary>
    public void MarkJobSuccess(long jobId, string message)
    {
        _db.Updateable<AiKnowledgeJob>()
            .SetColumns(x => new AiKnowledgeJob
            {
                Status = "success",
                Progress = 100,
                Message = message,
                FinishedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == jobId)
            .ExecuteCommand();
    }

    /// <summary>
    /// 将任务状态更新为失败。
    /// </summary>
    public void MarkJobError(long jobId, string message)
    {
        _db.Updateable<AiKnowledgeJob>()
            .SetColumns(x => new AiKnowledgeJob
            {
                Status = "error",
                Progress = 100,
                ErrorMessage = message,
                Message = "Failed",
                FinishedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == jobId)
            .ExecuteCommand();
    }

    /// <summary>
    /// 激活索引版本，并把知识库状态置为 ready。
    /// </summary>
    public void ActivateVersion(long knowledgeBaseId, long versionId, int documentCount, int chunkCount)
    {
        _db.Updateable<AiKnowledgeIndexVersion>()
            .SetColumns(x => new AiKnowledgeIndexVersion
            {
                Status = "ready",
                DocumentCount = documentCount,
                ChunkCount = chunkCount,
                ActivatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == versionId)
            .ExecuteCommand();

        _db.Updateable<AiKnowledgeBase>()
            .SetColumns(x => new AiKnowledgeBase
            {
                ActiveVersionId = versionId,
                Status = "ready",
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == knowledgeBaseId)
            .ExecuteCommand();
    }

    /// <summary>
    /// 更新知识库生命周期状态。
    /// </summary>
    public void UpdateKnowledgeBaseStatus(long knowledgeBaseId, string status, string? errorMessage = null)
    {
        _db.Updateable<AiKnowledgeBase>()
            .SetColumns(x => new AiKnowledgeBase
            {
                Status = status,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == knowledgeBaseId)
            .ExecuteCommand();
    }

    /// <summary>
    /// 软删除知识库和文档，并删除本地目录。
    /// </summary>
    public void DeleteKnowledgeBase(string name)
    {
        var kb = FindKnowledgeBase(name);
        _db.Updateable<AiKnowledgeBase>()
            .SetColumns(x => new AiKnowledgeBase
            {
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == kb.Id)
            .ExecuteCommand();

        _db.Updateable<AiKnowledgeDocument>()
            .SetColumns(x => new AiKnowledgeDocument { IsDeleted = true, DeletedAt = DateTime.UtcNow })
            .Where(x => x.KnowledgeBaseId == kb.Id)
            .ExecuteCommand();

        var path = _paths.GetKnowledgeBasePath(kb.Name);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private AiKnowledgeBase FindKnowledgeBase(string name)
    {
        var normalizedName = _paths.NormalizeName(name);
        var kb = _db.Queryable<AiKnowledgeBase>()
            .Where(x => x.Name == normalizedName && !x.IsDeleted)
            .First();
        return kb ?? throw new InvalidOperationException($"Knowledge base '{normalizedName}' does not exist.");
    }

    private KnowledgeBaseDto ToDto(AiKnowledgeBase kb)
    {
        return new KnowledgeBaseDto
        {
            Id = kb.Id,
            Name = kb.Name,
            DisplayName = kb.DisplayName,
            Description = kb.Description,
            EngineType = kb.EngineType,
            Status = kb.Status,
            IsDefault = kb.IsDefault,
            DocumentCount = kb.DocumentCount,
            ActiveVersionId = kb.ActiveVersionId,
            CreatedAt = kb.CreatedAt,
            UpdatedAt = kb.UpdatedAt,
            LatestJob = GetLatestJob(kb.Id)
        };
    }

    private static KnowledgeDocumentDto ToDocumentDto(AiKnowledgeDocument document)
    {
        return new KnowledgeDocumentDto
        {
            Id = document.Id,
            FileName = document.FileName,
            OriginalFileName = document.OriginalFileName,
            FileSize = document.FileSize,
            ContentType = document.ContentType,
            Extension = document.Extension,
            FileHash = document.FileHash,
            Status = document.Status,
            CreatedAt = document.CreatedAt
        };
    }

    private static KnowledgeJobDto ToJobDto(AiKnowledgeJob job)
    {
        return new KnowledgeJobDto
        {
            Id = job.Id,
            KnowledgeBaseId = job.KnowledgeBaseId,
            IndexVersionId = job.IndexVersionId,
            JobType = job.JobType,
            Status = job.Status,
            Progress = job.Progress,
            Message = job.Message,
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt
        };
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = (provider ?? "llamaindex").Trim().Replace("-", "_").ToLowerInvariant();
        return value is "local_vector" or "localvector" ? "llamaindex" : value;
    }
}