using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.PythonWorkers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IImageOcrService
{
    Task<List<ChatImageOcrResult>> ExtractAsync(IEnumerable<string> imagePaths, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken);
    Task<ImageOcrDiagnosticDto> DiagnoseAsync(string? imagePath, CancellationToken cancellationToken);
}

/// <summary>
/// Extracts text from server-owned chat images for third-party Codex profiles.
/// OCR is best-effort: a worker failure never prevents the text chat request from continuing.
/// </summary>
public sealed class ImageOcrService : IImageOcrService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IImageOcrPolicyService _policyService;
    private readonly IPythonWorkerHost _pythonWorkers;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageOcrService> _logger;
    private readonly SemaphoreSlim _concurrency = new(1, 1);
    private readonly ConcurrentDictionary<string, Task<ChatImageOcrResult?>> _inflight = new(StringComparer.Ordinal);

    public ImageOcrService(IImageOcrPolicyService policyService, IPythonWorkerHost pythonWorkers, IConfiguration configuration, ILogger<ImageOcrService> logger)
    {
        _policyService = policyService;
        _pythonWorkers = pythonWorkers;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<ChatImageOcrResult>> ExtractAsync(IEnumerable<string> imagePaths, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var policy = _policyService.GetPolicy();
        if (!policy.Enabled || !policy.AutoProcessImages) return [];
        var results = new List<ChatImageOcrResult>();
        foreach (var imagePath in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(imagePath);
            if (!file.Exists || file.Length <= 0 || file.Length > policy.MaxImageBytes)
            {
                await EmitAsync(onEvent, "failed", "图片未进入 OCR：文件不可用或超过管理员配置的大小限制。", null, cancellationToken);
                continue;
            }

            var attachmentId = Path.GetFileNameWithoutExtension(file.Name);
            if (attachmentId.Length != 32 || attachmentId.Any(character => !Uri.IsHexDigit(character))) attachmentId = "image";
            await EmitAsync(onEvent, "queued", "图片文字识别已排队。", attachmentId, cancellationToken);
            var cacheKey = await BuildCacheKeyAsync(file.FullName, policy.Language, cancellationToken);
            var task = _inflight.GetOrAdd(cacheKey, _ => ExtractOneAsync(file, attachmentId, cacheKey, policy, onEvent, cancellationToken));
            try
            {
                var result = await task;
                if (result != null && !string.IsNullOrWhiteSpace(result.Text)) results.Add(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Image OCR failed. AttachmentId={AttachmentId}", attachmentId);
                await EmitAsync(onEvent, "failed", "图片文字识别失败，已继续发送原始聊天请求。", attachmentId, cancellationToken);
            }
            finally
            {
                if (task.IsCompleted) _inflight.TryRemove(cacheKey, out _);
            }
        }
        return results;
    }

    /// <summary>
    /// Verifies the configured worker without exposing server-side paths to the browser.
    /// An optional server-owned image can be used to exercise the complete OCR pipeline.
    /// </summary>
    public async Task<ImageOcrDiagnosticDto> DiagnoseAsync(string? imagePath, CancellationToken cancellationToken)
    {
        var diagnostic = new ImageOcrDiagnosticDto();
        try
        {
            var pythonPath = _pythonWorkers.ResolvePythonPath("Ocr");
            diagnostic.PythonConfigured = string.Equals(pythonPath, "python", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pythonPath, "py", StringComparison.OrdinalIgnoreCase)
                || File.Exists(pythonPath);
            if (!diagnostic.PythonConfigured)
            {
                throw new FileNotFoundException("PaddleOCR Python environment was not found. Run PythonWorkers/ocr/install.ps1 from the backend directory.", pythonPath);
            }
            _ = _pythonWorkers.ResolveWorkerPath("Ocr");
            diagnostic.WorkerConfigured = true;

            var healthOutput = await _pythonWorkers.InvokeAsync("Ocr", new { command = "health" }, cancellationToken);
            using (var document = JsonDocument.Parse(healthOutput))
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                {
                    diagnostic.Error = root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                        ? message.GetString()
                        : "PaddleOCR health check failed.";
                    return diagnostic;
                }
                diagnostic.PaddleVersion = ReadString(root, "paddle_version");
                diagnostic.PaddleOcrVersion = ReadString(root, "paddleocr_version");
            }

            diagnostic.Ready = true;
            if (string.IsNullOrWhiteSpace(imagePath)) return diagnostic;

            var file = new FileInfo(imagePath);
            if (!file.Exists || file.Length <= 0) throw new InvalidOperationException("The diagnostic image is no longer available.");
            _pythonWorkers.EnsureAllowedPath(file.FullName);
            var policy = _policyService.GetPolicy();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds));
            var output = await _pythonWorkers.InvokeAsync("Ocr", new { command = "ocr_image", image_path = file.FullName, language = policy.Language }, timeout.Token);
            diagnostic.Result = ParseResult(output, Path.GetFileNameWithoutExtension(file.Name), policy);
            return diagnostic;
        }
        catch (PythonWorkerException exception)
        {
            _logger.LogWarning(exception, "Image OCR diagnostic worker failed. Stdout={Stdout}; Stderr={Stderr}", exception.Stdout, exception.Stderr);
            diagnostic.Error = GetWorkerErrorMessage(exception);
            return diagnostic;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Image OCR diagnostic failed.");
            diagnostic.Error = exception.Message;
            return diagnostic;
        }
    }

    private async Task<ChatImageOcrResult?> ExtractOneAsync(FileInfo file, string attachmentId, string cacheKey, ImageOcrPolicyDto policy, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken)
    {
        var cached = await ReadCacheAsync(cacheKey, attachmentId, cancellationToken);
        if (cached != null)
        {
            cached.FromCache = true;
            await EmitAsync(onEvent, "completed", "已使用缓存的图片文字识别结果。", attachmentId, cancellationToken, true);
            return cached;
        }

        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            cached = await ReadCacheAsync(cacheKey, attachmentId, cancellationToken);
            if (cached != null)
            {
                cached.FromCache = true;
                await EmitAsync(onEvent, "completed", "已使用缓存的图片文字识别结果。", attachmentId, cancellationToken, true);
                return cached;
            }

            _pythonWorkers.EnsureAllowedPath(file.FullName);
            await EmitAsync(onEvent, "recognizing", "正在识别图片中的文字。", attachmentId, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds));
            var output = await _pythonWorkers.InvokeAsync("Ocr", new { command = "ocr_image", image_path = file.FullName, language = policy.Language }, async progress =>
            {
                var message = string.IsNullOrWhiteSpace(progress.Message) ? "正在识别图片中的文字。" : progress.Message;
                await EmitAsync(onEvent, "recognizing", message, attachmentId, cancellationToken);
            }, timeout.Token);
            var result = ParseResult(output, attachmentId, policy);
            await WriteCacheAsync(cacheKey, result, cancellationToken);
            await EmitAsync(onEvent, "completed", result.Truncated ? "图片文字已识别，内容已按安全上限截断。" : "图片文字已识别并附加到第三方模型上下文。", attachmentId, cancellationToken);
            return result;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static ChatImageOcrResult ParseResult(string output, string attachmentId, ImageOcrPolicyDto policy)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var message = root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var value) ? value.GetString() : "OCR worker returned an invalid result.";
            throw new InvalidOperationException(message);
        }
        var text = ReadString(root, "text").Trim();
        var truncated = text.Length > policy.MaxPromptCharacters;
        if (truncated) text = text[..policy.MaxPromptCharacters];
        return new ChatImageOcrResult
        {
            AttachmentId = attachmentId,
            Engine = ReadString(root, "engine") is { Length: > 0 } engine ? engine : "paddleocr",
            Language = policy.Language,
            Text = text,
            Confidence = ReadDouble(root, "confidence"),
            ElapsedMs = ReadLong(root, "elapsed_ms"),
            Truncated = truncated
        };
    }

    private static string GetWorkerErrorMessage(PythonWorkerException exception)
    {
        try
        {
            using var document = JsonDocument.Parse(exception.Stdout);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? exception.Message;
            }
        }
        catch (JsonException) { }

        var stderr = exception.Stderr.Trim();
        return string.IsNullOrWhiteSpace(stderr) ? exception.Message : $"{exception.Message} {stderr[..Math.Min(stderr.Length, 800)]}";
    }

    private string CachePath(string cacheKey)
    {
        var configured = _configuration["ImageOcr:CachePath"];
        var root = string.IsNullOrWhiteSpace(configured) ? Path.Combine("data", "image_ocr_cache") : configured.Trim();
        return Path.Combine(Path.GetFullPath(root), $"{cacheKey}.json");
    }

    private async Task<ChatImageOcrResult?> ReadCacheAsync(string cacheKey, string attachmentId, CancellationToken cancellationToken)
    {
        var path = CachePath(cacheKey);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var cached = await JsonSerializer.DeserializeAsync<ChatImageOcrResult>(stream, JsonOptions, cancellationToken);
            if (cached == null || string.IsNullOrWhiteSpace(cached.Text)) return null;
            cached.AttachmentId = attachmentId;
            return cached;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private async Task WriteCacheAsync(string cacheKey, ChatImageOcrResult result, CancellationToken cancellationToken)
    {
        var path = CachePath(cacheKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.writing";
        try
        {
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { }
        }
    }

    private static async Task<string> BuildCacheKeyAsync(string path, string language, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return $"paddleocr-v1-{language}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static async Task EmitAsync(AgentStreamEventHandler? onEvent, string status, string content, string? attachmentId, CancellationToken cancellationToken, bool cached = false)
    {
        if (onEvent == null) return;
        await onEvent(new AgentStreamEvent
        {
            Type = "tool",
            Content = content,
            Metadata = new Dictionary<string, object?>
            {
                ["agent"] = "image_ocr",
                ["ocr_status"] = status,
                ["attachment_id"] = attachmentId,
                ["cached"] = cached
            }
        }, cancellationToken);
    }

    private static string ReadString(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static double? ReadDouble(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    private static long ReadLong(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
}
