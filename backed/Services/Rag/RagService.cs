using AiAgent.Backend.Services.Settings;

namespace AiAgent.Backend.Services.Rag;

/// <summary>
/// RAG 统一入口，屏蔽不同 provider 的具体实现差异。
/// </summary>
public interface IRagService
{
    /// <summary>
    /// 检查指定 provider 是否可用。
    /// </summary>
    Task<RagOperationResult> PreflightAsync(string? provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 首次构建知识库索引。
    /// </summary>
    Task<RagOperationResult> InitializeAsync(string? provider, RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 增量添加文档。
    /// </summary>
    Task<RagOperationResult> AddDocumentsAsync(string? provider, RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重建索引。
    /// </summary>
    Task<RagOperationResult> ReindexAsync(string? provider, RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行检索。
    /// </summary>
    Task<RagSearchResult> SearchAsync(string? provider, RagSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前激活的 Embedding 配置快照。
    /// </summary>
    RagEmbeddingOptions GetActiveEmbeddingOptions();
}

/// <summary>
/// RAG 统一入口实现。
/// </summary>
public sealed class RagService : IRagService
{
    private const string RedactedSecret = "********";
    private readonly IRagPipelineFactory _pipelineFactory;
    private readonly IModelCatalogService _catalogService;

    /// <summary>
    /// 初始化 RAG 门面服务，统一选择 Pipeline 并注入当前 Embedding 配置。
    /// </summary>
    public RagService(IRagPipelineFactory pipelineFactory, IModelCatalogService catalogService)
    {
        _pipelineFactory = pipelineFactory;
        _catalogService = catalogService;
    }

    /// <summary>
    /// 检查指定 provider 是否可用。
    /// </summary>
    public Task<RagOperationResult> PreflightAsync(string? provider, CancellationToken cancellationToken = default)
    {
        return _pipelineFactory.GetPipeline(provider).PreflightAsync(cancellationToken);
    }

    /// <summary>
    /// 首次构建知识库索引，并注入当前激活 Embedding 配置。
    /// </summary>
    public Task<RagOperationResult> InitializeAsync(string? provider, RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default)
    {
        request.Embedding = GetActiveEmbeddingOptions();
        return _pipelineFactory.GetPipeline(provider).InitializeAsync(request, progressHandler, cancellationToken);
    }

    /// <summary>
    /// 增量添加文档，并注入当前激活 Embedding 配置。
    /// </summary>
    public Task<RagOperationResult> AddDocumentsAsync(string? provider, RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default)
    {
        request.Embedding = GetActiveEmbeddingOptions();
        return _pipelineFactory.GetPipeline(provider).AddDocumentsAsync(request, progressHandler, cancellationToken);
    }

    /// <summary>
    /// 重建索引，并注入当前激活 Embedding 配置。
    /// </summary>
    public Task<RagOperationResult> ReindexAsync(string? provider, RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default)
    {
        request.Embedding = GetActiveEmbeddingOptions();
        return _pipelineFactory.GetPipeline(provider).ReindexAsync(request, progressHandler, cancellationToken);
    }

    /// <summary>
    /// 执行检索，并注入当前激活 Embedding 配置。
    /// </summary>
    public Task<RagSearchResult> SearchAsync(string? provider, RagSearchRequest request, CancellationToken cancellationToken = default)
    {
        request.Embedding = GetActiveEmbeddingOptions();
        return _pipelineFactory.GetPipeline(provider).SearchAsync(request, cancellationToken);
    }

    /// <summary>
    /// 从模型设置 catalog 中读取当前激活 Embedding 配置。
    /// </summary>
    public RagEmbeddingOptions GetActiveEmbeddingOptions()
    {
        var catalog = _catalogService.Load(redactSecrets: false);
        var service = catalog.Services.Embedding;
        var profile = service.Profiles.FirstOrDefault(x => x.Id == service.ActiveProfileId)
            ?? service.Profiles.FirstOrDefault();
        var model = profile?.Models.FirstOrDefault(x => x.Id == service.ActiveModelId)
            ?? profile?.Models.FirstOrDefault();

        return new RagEmbeddingOptions
        {
            Provider = NormalizeProvider(profile?.Binding ?? profile?.Provider),
            BaseUrl = profile?.BaseUrl,
            ApiKey = profile?.ApiKey == RedactedSecret ? null : profile?.ApiKey,
            Model = model?.Model ?? "",
            Dimension = model?.Dimension
        };
    }

    private static string NormalizeProvider(string? provider)
    {
        return (provider ?? "").Trim().Replace("-", "_").ToLowerInvariant();
    }
}