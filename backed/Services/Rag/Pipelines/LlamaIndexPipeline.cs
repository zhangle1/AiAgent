using AiAgent.Backend.Services.PythonWorkers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Rag;

/// <summary>
/// LlamaIndex pipeline，通过 Python worker 执行索引构建和检索。
/// </summary>
public sealed class LlamaIndexPipeline : IRagPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<LlamaIndexPipeline> _logger;
    private readonly IPythonWorkerHost _pythonWorkerHost;

    /// <summary>
    /// 初始化 LlamaIndex Pipeline，读取 Python 运行时、工作脚本和超时配置。
    /// </summary>
    public LlamaIndexPipeline(ILogger<LlamaIndexPipeline> logger, IPythonWorkerHost pythonWorkerHost)
    {
        _logger = logger;
        _pythonWorkerHost = pythonWorkerHost;
    }

    /// <summary>
    /// Provider 标识。
    /// </summary>
    public string Provider => "llamaindex";

    /// <summary>
    /// 调用 worker 检查 Python 依赖和运行环境。
    /// </summary>
    public async Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var workerPath = ResolveWorkerPath();
            var response = await InvokeWorkerAsync(new { command = "preflight" }, null, cancellationToken);
            var result = ParseOperation(response, "preflight");
            result.Details["worker_path"] = workerPath;
            result.Details["python_path"] = ResolvePythonPath();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LlamaIndex preflight failed.");
            return new RagOperationResult
            {
                Ok = false,
                Provider = Provider,
                Action = "preflight",
                ErrorCode = "environment_not_ready",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 首次构建 LlamaIndex 索引。
    /// </summary>
    public async Task<RagOperationResult> InitializeAsync(RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default)
    {
        var response = await InvokeWorkerAsync(BuildPayload("initialize", request), progressHandler, cancellationToken);
        return ParseOperation(response, "initialize");
    }

    /// <summary>
    /// 向已有 LlamaIndex 索引添加文档。
    /// </summary>
    public async Task<RagOperationResult> AddDocumentsAsync(RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default)
    {
        var response = await InvokeWorkerAsync(BuildPayload("add_documents", request), progressHandler, cancellationToken);
        return ParseOperation(response, "add_documents");
    }

    /// <summary>
    /// 删除旧索引目录并重新构建索引。
    /// </summary>
    public async Task<RagOperationResult> ReindexAsync(RagIndexRequest request, Func<RagProgressEvent, Task>? progressHandler = null, CancellationToken cancellationToken = default)
    {
        var response = await InvokeWorkerAsync(BuildPayload("reindex", request), progressHandler, cancellationToken);
        return ParseOperation(response, "reindex");
    }

    /// <summary>
    /// 调用 worker 加载索引并执行检索。
    /// </summary>
    public async Task<RagSearchResult> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default)
    {
        var response = await InvokeWorkerAsync(new
        {
            command = "search",
            kb_name = request.KnowledgeBaseName,
            query = request.Query,
            top_k = request.TopK,
            persist_dir = request.PersistDir,
            embedding = request.Embedding,
            retrieval = request.Retrieval
        }, null, cancellationToken);

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            var (code, message) = ExtractError(root);
            return new RagSearchResult
            {
                Ok = false,
                Provider = Provider,
                Query = request.Query,
                ErrorCode = code,
                ErrorMessage = message
            };
        }

        var result = new RagSearchResult
        {
            Ok = true,
            Provider = GetString(root, "provider") ?? Provider,
            Query = GetString(root, "query") ?? request.Query,
            Answer = GetString(root, "answer") ?? "",
            Content = GetString(root, "content") ?? ""
        };

        if (root.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array)
        {
            foreach (var citation in citations.EnumerateArray())
            {
                result.Citations.Add(new RagCitation
                {
                    Score = citation.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDouble() : null,
                    Text = GetString(citation, "text") ?? "",
                    Metadata = citation.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata.GetRawText(), JsonOptions) ?? []
                        : []
                });
            }
        }

        return result;
    }

    private static object BuildPayload(string command, RagIndexRequest request)
    {
        return new
        {
            command,
            kb_name = request.KnowledgeBaseName,
            file_paths = request.FilePaths,
            persist_dir = request.PersistDir,
            embedding = request.Embedding,
            retrieval = request.Retrieval
        };
    }

    private async Task<string> InvokeWorkerAsync(object payload, Func<RagProgressEvent, Task>? progressHandler, CancellationToken cancellationToken)
    {
        try
        {
            return await _pythonWorkerHost.InvokeAsync("Rag", payload, ToWorkerProgressHandler(progressHandler), cancellationToken);
        }
        catch (PythonWorkerException ex)
        {
            using var errorDocument = JsonDocument.Parse(ex.Stdout);
            var (code, message) = ExtractError(errorDocument.RootElement);
            throw new InvalidOperationException(message ?? code ?? $"LlamaIndex worker exited with {ex.ExitCode}.");
        }
    }

    private static Func<PythonWorkerEvent, Task>? ToWorkerProgressHandler(Func<RagProgressEvent, Task>? progressHandler)
    {
        if (progressHandler is null)
        {
            return null;
        }

        return progress => progressHandler(new RagProgressEvent
        {
            Stage = progress.Stage ?? "indexing",
            Progress = Math.Clamp(progress.Progress ?? 0, 0, 99),
            Message = progress.Message ?? "Indexing documents."
        });
    }

    private string ResolveWorkerPath()
    {
        return _pythonWorkerHost.ResolveWorkerPath("Rag");
    }

    private string ResolvePythonPath()
    {
        return _pythonWorkerHost.ResolvePythonPath("Rag");
    }

    private static RagOperationResult ParseOperation(string response, string fallbackAction)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
        var result = new RagOperationResult
        {
            Ok = ok,
            Provider = GetString(root, "provider") ?? "llamaindex",
            Action = GetString(root, "action") ?? fallbackAction,
            DocumentCount = GetInt(root, "document_count"),
            ChunkCount = GetInt(root, "chunk_count"),
            Details = ExtractDetails(root)
        };

        if (!ok)
        {
            var (code, message) = ExtractError(root);
            result.ErrorCode = code;
            result.ErrorMessage = message;
        }

        return result;
    }

    private static (string? Code, string? Message) ExtractError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return ("worker_error", root.GetRawText());
        }

        return (GetString(error, "code"), GetString(error, "message"));
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    private static Dictionary<string, object?> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object?>();
        if (root.TryGetProperty("python", out var python) && python.ValueKind == JsonValueKind.String)
        {
            details["python"] = python.GetString();
        }

        if (root.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
        {
            details["dependencies"] = JsonSerializer.Deserialize<Dictionary<string, object?>>(dependencies.GetRawText(), JsonOptions) ?? [];
        }

        return details;
    }
}