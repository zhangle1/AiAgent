using AiAgent.Backend.Services.PythonWorkers;
using AiAgent.Backend.Services.Rag;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>
/// 文档解析服务。
/// </summary>
public interface IDocumentParsingService
{
    /// <summary>
    /// 检测文档解析 worker 环境。
    /// </summary>
    Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析 PDF 文档。
    /// </summary>
    Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 Python worker 的文档解析服务。
/// </summary>
public sealed class DocumentParsingService : IDocumentParsingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IPythonWorkerHost _pythonWorkerHost;
    private readonly ILogger<DocumentParsingService> _logger;

    /// <summary>
    /// 初始化文档解析服务。
    /// </summary>
    public DocumentParsingService(IPythonWorkerHost pythonWorkerHost, ILogger<DocumentParsingService> logger)
    {
        _pythonWorkerHost = pythonWorkerHost;
        _logger = logger;
    }

    /// <summary>
    /// 检测文档解析 worker 环境。
    /// </summary>
    public async Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _pythonWorkerHost.InvokeAsync("Parsing", new { command = "preflight" }, cancellationToken);
            var result = ParsePreflight(response);
            result.Details["worker_path"] = _pythonWorkerHost.ResolveWorkerPath("Parsing");
            result.Details["python_path"] = _pythonWorkerHost.ResolvePythonPath("Parsing");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document parsing preflight failed.");
            return new RagOperationResult
            {
                Ok = false,
                Provider = "document-parser",
                Action = "preflight",
                ErrorCode = "environment_not_ready",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 解析 PDF 文档。
    /// </summary>
    public async Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _pythonWorkerHost.EnsureAllowedPath(request.FilePath);
            _pythonWorkerHost.EnsureAllowedPath(request.OutputDir);

            var response = await _pythonWorkerHost.InvokeAsync("Parsing", new
            {
                command = "parse_pdf",
                file_path = request.FilePath,
                output_dir = request.OutputDir,
                options = new
                {
                    engine = request.Engine,
                    write_images = request.WriteImages
                }
            }, cancellationToken);

            return ParseResult(response);
        }
        catch (PythonWorkerException ex)
        {
            return ParseResult(ex.Stdout);
        }
        catch (Exception ex)
        {
            return new DocumentParseResult
            {
                Ok = false,
                Provider = "document-parser",
                Action = "parse_pdf",
                ErrorCode = "worker_error",
                ErrorMessage = ex.Message
            };
        }
    }

    private static RagOperationResult ParsePreflight(string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
        var result = new RagOperationResult
        {
            Ok = ok,
            Provider = GetString(root, "provider") ?? "document-parser",
            Action = GetString(root, "action") ?? "preflight",
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

    private static DocumentParseResult ParseResult(string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
        var result = new DocumentParseResult
        {
            Ok = ok,
            Provider = GetString(root, "provider") ?? "document-parser",
            Action = GetString(root, "action") ?? "parse_pdf",
            Engine = GetString(root, "engine") ?? "",
            MarkdownPath = GetString(root, "markdown_path"),
            TextPath = GetString(root, "text_path"),
            PageCount = GetInt(root, "page_count"),
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

    private static Dictionary<string, object?> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object?>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is "ok" or "provider" or "action" or "error")
            {
                continue;
            }

            details[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText(), JsonOptions);
        }

        return details;
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
}