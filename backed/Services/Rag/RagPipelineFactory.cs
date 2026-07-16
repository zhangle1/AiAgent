namespace AiAgent.Backend.Services.Rag;

/// <summary>
/// RAG pipeline 工厂，用于按 provider 选择具体实现。
/// </summary>
public interface IRagPipelineFactory
{
    /// <summary>
    /// 根据 provider 获取 pipeline。
    /// </summary>
    IRagPipeline GetPipeline(string? provider);
}

/// <summary>
/// RAG pipeline 工厂实现，当前默认落到 LlamaIndex。
/// </summary>
public sealed class RagPipelineFactory : IRagPipelineFactory
{
    private readonly LlamaIndexPipeline _llamaIndexPipeline;

    /// <summary>
    /// 初始化 RAG Pipeline 工厂，并注册当前支持的 LlamaIndex 实现。
    /// </summary>
    public RagPipelineFactory(LlamaIndexPipeline llamaIndexPipeline)
    {
        _llamaIndexPipeline = llamaIndexPipeline;
    }

    /// <summary>
    /// 根据 provider 获取 pipeline，未实现的 provider 会抛出明确异常。
    /// </summary>
    public IRagPipeline GetPipeline(string? provider)
    {
        var normalized = NormalizeProvider(provider);
        return normalized switch
        {
            "llamaindex" or "local_vector" or "" => _llamaIndexPipeline,
            _ => throw new InvalidOperationException($"RAG provider '{provider}' is reserved but not implemented yet.")
        };
    }

    /// <summary>
    /// 规范化 provider 名称，兼容 local_vector 等历史标识。
    /// </summary>
    public static string NormalizeProvider(string? provider)
    {
        var value = (provider ?? "").Trim().Replace("-", "_").ToLowerInvariant();
        return value switch
        {
            "localvector" => "local_vector",
            "local_vector" => "llamaindex",
            _ => value
        };
    }
}