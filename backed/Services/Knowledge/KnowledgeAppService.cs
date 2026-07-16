using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Parsing;
using AiAgent.Backend.Services.Rag;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using System.Diagnostics;

namespace AiAgent.Backend.Services.Knowledge;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/knowledge")]
public sealed class KnowledgeAppService : IDynamicApiController
{
    private readonly ISqlSugarClient _db;
    private readonly IKnowledgeBaseManager _manager;
    private readonly IKnowledgeProviderConfigService _providerConfigService;
    private readonly IKnowledgeProgressHub _progressHub;
    private readonly IKnowledgeTaskRunner _taskRunner;
    private readonly IDocumentParsingService _documentParsingService;
    private readonly IRagService _ragService;
    private readonly ILogger<KnowledgeAppService> _logger;

    /// <summary>
    /// 初始化知识库动态接口服务，组合数据库、知识库管理、任务调度和 RAG 查询能力。
    /// </summary>
    public KnowledgeAppService(
        ISqlSugarClient db,
        IKnowledgeBaseManager manager,
        IKnowledgeProviderConfigService providerConfigService,
        IKnowledgeProgressHub progressHub,
        IKnowledgeTaskRunner taskRunner,
        IDocumentParsingService documentParsingService,
        IRagService ragService,
        ILogger<KnowledgeAppService> logger)
    {
        _db = db;
        _manager = manager;
        _providerConfigService = providerConfigService;
        _progressHub = progressHub;
        _taskRunner = taskRunner;
        _documentParsingService = documentParsingService;
        _ragService = ragService;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前支持或预留的 RAG provider 列表。
    /// </summary>
    [HttpGet("rag-providers")]
    public async Task<List<KnowledgeProviderDto>> GetRagProviders()
    {
        var preflight = await _ragService.PreflightAsync("llamaindex");
        return
        [
            new KnowledgeProviderDto
            {
                Id = "llamaindex",
                Name = "LlamaIndex",
                Description = "Local vector retrieval backed by LlamaIndex.",
                Configured = preflight.Ok,
                Status = preflight.Ok ? "ready" : "needs_setup",
                Modes = ["semantic"],
                DefaultMode = "semantic"
            },
            Planned("pageindex", "PageIndex", "Hosted retrieval engine with page-level citations."),
            Planned("graphrag", "GraphRAG", "Graph-enhanced retrieval reserved for a later phase."),
            Planned("lightrag", "LightRAG", "Hybrid graph and vector retrieval reserved for a later phase."),
            Planned("lightrag-server", "LightRAG Server", "External LightRAG server pointer reserved for a later phase.")
        ];
    }

    /// <summary>
    /// 检测指定 RAG provider 的运行环境。
    /// </summary>
    [HttpGet("rag-providers/{provider}/preflight")]
    public Task<RagOperationResult> CheckRagProviderEnvironment([FromRoute] string provider, CancellationToken cancellationToken)
    {
        return _ragService.PreflightAsync(provider, cancellationToken);
    }

    /// <summary>
    /// 检测文档解析 worker 的运行环境。
    /// </summary>
    [HttpGet("parsing/preflight")]
    public Task<RagOperationResult> CheckParsingEnvironment(CancellationToken cancellationToken)
    {
        return _documentParsingService.PreflightAsync(cancellationToken);
    }

    /// <summary>
    /// 获取指定 RAG provider 的检索与分块配置。
    /// </summary>
    [HttpGet("rag-providers/{provider}/config")]
    public KnowledgeProviderConfigDto GetRagProviderConfig([FromRoute] string provider)
    {
        return _providerConfigService.GetConfig(provider);
    }

    /// <summary>
    /// 保存指定 RAG provider 的检索与分块配置。
    /// </summary>
    [HttpPut("rag-providers/{provider}/config")]
    public KnowledgeProviderConfigDto UpdateRagProviderConfig([FromRoute] string provider, [FromBody] KnowledgeProviderConfigDto payload)
    {
        return _providerConfigService.SaveConfig(provider, payload);
    }

    /// <summary>
    /// 获取知识库列表。
    /// </summary>
    [HttpGet("list")]
    public List<KnowledgeBaseDto> ListKnowledgeBases()
    {
        return _manager.ListKnowledgeBases();
    }

    /// <summary>
    /// 创建知识库，并在上传文件存在时启动首次索引任务。
    /// </summary>
    [HttpPost("create")]
    public async Task<KnowledgeMutationResponse> CreateKnowledgeBase([FromForm] KnowledgeCreateRequest request, CancellationToken cancellationToken)
    {
        var kb = _manager.CreateKnowledgeBase(request.Name, request.DisplayName, request.Description, request.Provider);
        var documents = await _manager.SaveDocumentsAsync(kb, request.Files, cancellationToken);

        return new KnowledgeMutationResponse
        {
            KnowledgeBase = BuildMutationKnowledgeBase(kb, null),
            Message = documents.Count > 0 ? "Knowledge base created and files uploaded." : "Knowledge base created."
        };
    }

    /// <summary>
    /// 获取单个知识库详情。
    /// </summary>
    [HttpGet("{kbName}")]
    public KnowledgeDetailDto GetKnowledgeBase([FromRoute] string kbName)
    {
        return _manager.GetKnowledgeBase(kbName);
    }

    /// <summary>
    /// 获取知识库索引版本列表。
    /// </summary>
    [HttpGet("{kbName}/index-versions")]
    public List<KnowledgeIndexVersionDto> GetIndexVersions([FromRoute] string kbName)
    {
        var kb = FindKnowledgeBase(kbName);
        return _db.Queryable<AiKnowledgeIndexVersion>()
            .Where(x => x.KnowledgeBaseId == kb.Id)
            .OrderByDescending(x => x.VersionNo)
            .ToList()
            .Select(x => new KnowledgeIndexVersionDto
            {
                Id = x.Id,
                KnowledgeBaseId = x.KnowledgeBaseId,
                VersionNo = x.VersionNo,
                Status = x.Status,
                EngineType = x.EngineType,
                StoragePath = x.StoragePath,
                DocumentCount = x.DocumentCount,
                ChunkCount = x.ChunkCount,
                Active = kb.ActiveVersionId == x.Id,
                CreatedAt = x.CreatedAt,
                ActivatedAt = x.ActivatedAt
            })
            .ToList();
    }

    /// <summary>
    /// 读取知识库文档原始文件，供前端预览或下载。
    /// </summary>
    [HttpGet("{kbName}/documents/{documentId:long}/file")]
    public IActionResult GetDocumentFile([FromRoute] string kbName, [FromRoute] long documentId, [FromQuery] bool download = false)
    {
        var kb = FindKnowledgeBase(kbName);
        var document = _db.Queryable<AiKnowledgeDocument>()
            .Where(x => x.Id == documentId && x.KnowledgeBaseId == kb.Id && !x.IsDeleted)
            .First();
        if (document is null || string.IsNullOrWhiteSpace(document.StoragePath) || !System.IO.File.Exists(document.StoragePath))
        {
            return new NotFoundObjectResult(new { message = "Document file does not exist." });
        }

        var contentType = string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType;
        var downloadName = string.IsNullOrWhiteSpace(document.OriginalFileName) ? document.FileName : document.OriginalFileName;
        var result = new PhysicalFileResult(document.StoragePath, contentType)
        {
            EnableRangeProcessing = true
        };
        if (download)
        {
            result.FileDownloadName = downloadName;
        }

        return result;
    }

    [HttpDelete("{kbName}/documents/{documentId:long}")]
    public object DeleteDocument([FromRoute] string kbName, [FromRoute] long documentId)
    {
        var kb = FindKnowledgeBase(kbName);
        var document = _db.Queryable<AiKnowledgeDocument>()
            .Where(x => x.Id == documentId && x.KnowledgeBaseId == kb.Id && !x.IsDeleted)
            .First();
        if (document is null)
        {
            return new { ok = true };
        }

        _db.Updateable<AiKnowledgeDocument>()
            .SetColumns(x => new AiKnowledgeDocument
            {
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == document.Id)
            .ExecuteCommand();

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

        return new { ok = true };
    }

    /// <summary>
    /// 上传文件到已有知识库，并触发索引重建任务。
    /// </summary>
    /// <summary>
    /// 基于知识库全部文档重建索引。
    /// </summary>
    [HttpPost("{kbName}/upload")]
    public async Task<KnowledgeMutationResponse> UploadFiles([FromRoute(Name = "kbName")] string kbName, [FromForm(Name = "Files")] List<IFormFile>? files, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var fileCount = files?.Count ?? 0;
        var totalBytes = files?.Sum(x => x.Length) ?? 0;
        _logger.LogInformation("Knowledge upload action entered. Kb={KbName}, Files={FileCount}, Bytes={Bytes}", kbName, fileCount, totalBytes);
        var kb = FindKnowledgeBase(kbName);
        _logger.LogInformation("Knowledge upload kb resolved. Kb={KbName}, ElapsedMs={ElapsedMs}", kb.Name, stopwatch.ElapsedMilliseconds);
        var uploadedFiles = files ?? [];
        var documents = await _manager.SaveDocumentsAsync(kb, uploadedFiles, cancellationToken);
        _logger.LogInformation("Knowledge upload saved. Kb={KbName}, Documents={DocumentCount}, ElapsedMs={ElapsedMs}", kb.Name, documents.Count, stopwatch.ElapsedMilliseconds);
        return new KnowledgeMutationResponse
        {
            KnowledgeBase = BuildMutationKnowledgeBase(kb, null),
            Message = documents.Count == 0 ? "No files uploaded." : "Upload saved."
        };
    }

    [HttpPost("{kbName}/reindex")]
    public KnowledgeMutationResponse Reindex([FromRoute] string kbName)
    {
        var kb = FindKnowledgeBase(kbName);
        var documents = _db.Queryable<AiKnowledgeDocument>()
            .Where(x => x.KnowledgeBaseId == kb.Id && !x.IsDeleted)
            .ToList();
        var job = _taskRunner.StartIndexTask(kb, documents, "reindex");
        return new KnowledgeMutationResponse
        {
            KnowledgeBase = BuildMutationKnowledgeBase(kb, job, "processing"),
            TaskId = job.Id,
            Message = "Reindex started."
        };
    }

    /// <summary>
    /// 失败后重试索引任务，当前等价于重建索引。
    /// </summary>
    [HttpPost("{kbName}/retry")]
    public KnowledgeMutationResponse Retry([FromRoute] string kbName)
    {
        return Reindex(kbName);
    }

    /// <summary>
    /// 将指定知识库设为默认知识库。
    /// </summary>
    [HttpPost("{kbName}/default")]
    public KnowledgeBaseDto SetDefaultKnowledgeBase([FromRoute] string kbName)
    {
        var kb = FindKnowledgeBase(kbName);
        _db.Updateable<AiKnowledgeBase>()
            .SetColumns(x => new AiKnowledgeBase
            {
                IsDefault = false,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => !x.IsDeleted)
            .ExecuteCommand();
        _db.Updateable<AiKnowledgeBase>()
            .SetColumns(x => new AiKnowledgeBase
            {
                IsDefault = true,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == kb.Id)
            .ExecuteCommand();

        return _manager.ListKnowledgeBases().First(x => x.Id == kb.Id);
    }

    /// <summary>
    /// 获取知识库最近一次后台任务进度。
    /// </summary>
    [HttpGet("{kbName}/progress")]
    public KnowledgeJobDto? GetProgress([FromRoute] string kbName)
    {
        var kb = FindKnowledgeBase(kbName);
        return _manager.GetLatestJob(kb.Id);
    }

    [HttpGet("diagnostics")]
    public object GetDiagnostics()
    {
        return _progressHub.GetDiagnostics();
    }

    /// <summary>
    /// 在知识库当前激活索引中执行检索。
    /// </summary>
    [HttpPost("{kbName}/search")]
    public async Task<KnowledgeSearchResponse> Search([FromRoute] string kbName, [FromBody] KnowledgeSearchRequest request, CancellationToken cancellationToken)
    {
        var kb = FindKnowledgeBase(kbName);
        if (!kb.ActiveVersionId.HasValue)
        {
            throw new InvalidOperationException("Knowledge base has no active index version.");
        }

        var version = _db.Queryable<AiKnowledgeIndexVersion>()
            .Where(x => x.Id == kb.ActiveVersionId.Value)
            .First();
        if (version is null || string.IsNullOrWhiteSpace(version.StoragePath))
        {
            throw new InvalidOperationException("Active index version is missing storage path.");
        }

        var result = await _ragService.SearchAsync(kb.EngineType, new RagSearchRequest
        {
            KnowledgeBaseName = kb.Name,
            PersistDir = version.StoragePath,
            Query = request.Query,
            TopK = request.TopK
        }, cancellationToken);

        if (!result.Ok)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Knowledge search failed.");
        }

        return new KnowledgeSearchResponse
        {
            Query = result.Query,
            Provider = result.Provider,
            Answer = result.Answer,
            Content = result.Content,
            Citations = result.Citations.Select(x => new KnowledgeCitationDto
            {
                Score = x.Score,
                Text = x.Text,
                Metadata = x.Metadata
            }).ToList()
        };
    }

    /// <summary>
    /// 删除知识库及其本地索引目录。
    /// </summary>
    [HttpDelete("{kbName}")]
    public object DeleteKnowledgeBase([FromRoute] string kbName)
    {
        _manager.DeleteKnowledgeBase(kbName);
        return new { ok = true };
    }

    private AiKnowledgeBase FindKnowledgeBase(string kbName)
    {
        if (string.IsNullOrWhiteSpace(kbName))
        {
            throw new ArgumentException("Knowledge base name is required.", nameof(kbName));
        }

        var normalized = kbName.Trim().ToLowerInvariant();
        var kb = _db.Queryable<AiKnowledgeBase>()
            .Where(x => x.Name == normalized && !x.IsDeleted)
            .First();
        return kb ?? throw new InvalidOperationException($"Knowledge base '{kbName}' does not exist.");
    }

    private static KnowledgeBaseDto BuildMutationKnowledgeBase(AiKnowledgeBase kb, AiKnowledgeJob? job, string? status = null)
    {
        return new KnowledgeBaseDto
        {
            Id = kb.Id,
            Name = kb.Name,
            DisplayName = kb.DisplayName,
            Description = kb.Description,
            EngineType = kb.EngineType,
            Status = status ?? kb.Status,
            IsDefault = kb.IsDefault,
            DocumentCount = kb.DocumentCount,
            ActiveVersionId = kb.ActiveVersionId,
            CreatedAt = kb.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            LatestJob = job is null ? null : new KnowledgeJobDto
            {
                Id = job.Id,
                KnowledgeBaseId = job.KnowledgeBaseId,
                IndexVersionId = job.IndexVersionId,
                JobType = job.JobType,
                Status = job.Status,
                Progress = job.Progress,
                Message = job.Message,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                FinishedAt = job.FinishedAt
            }
        };
    }

    private static KnowledgeProviderDto Planned(string id, string name, string description)
    {
        return new KnowledgeProviderDto
        {
            Id = id,
            Name = name,
            Description = description,
            Configured = false,
            Status = "planned",
            Modes = ["semantic"],
            DefaultMode = "semantic"
        };
    }
}