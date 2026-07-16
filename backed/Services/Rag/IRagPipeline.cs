namespace AiAgent.Backend.Services.Rag;

/// <summary>
/// 单个 RAG provider 的统一 pipeline 接口。
/// </summary>
public interface IRagPipeline
{
    /// <summary>
    /// Provider 标识。
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// 检查 provider 运行环境是否可用。
    /// </summary>
    Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 首次构建知识库索引。
    /// </summary>
    Task<RagOperationResult> InitializeAsync(RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 增量添加文档到索引。
    /// </summary>
    Task<RagOperationResult> AddDocumentsAsync(RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于全部文档重建索引。
    /// </summary>
    Task<RagOperationResult> ReindexAsync(RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在已构建索引中执行检索。
    /// </summary>
    Task<RagSearchResult> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);
}