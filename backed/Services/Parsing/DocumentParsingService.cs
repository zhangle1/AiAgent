using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Rag;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AiAgent.Backend.Services.Parsing;

public interface IDocumentParsingService
{
    Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default);
    Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>本地 C# PDF 解析入口；不启动 Python，不调用模型。</summary>
public sealed class DocumentParsingService(IConfiguration configuration, ILogger<DocumentParsingService> logger) : IDocumentParsingService
{
    // PDFium 内部也有全局锁；入口限流同时约束文字布局与位图的内存占用。
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal const string Engine = "pdfpig-pdfium-layout";

    public async Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 实际加载两套引擎与原生库，避免仅检查 DLL 存在就宣称环境可用。
                var builder = new PdfDocumentBuilder();
                var page = builder.AddPage(72, 72);
                page.AddText("Ready", 10, new PdfPoint(5, 35), builder.AddStandard14Font(Standard14Font.Helvetica));
                var bytes = builder.Build();
                using var document = PdfDocument.Open(bytes);
                if (document.GetPage(1).Letters.Count == 0) throw new InvalidDataException("PDF 文字引擎自检失败。");
                using var stream = new MemoryStream(bytes, writable: false);
                using var bitmap = CSharpPdfLayoutConverter.RenderPage(stream, 0, 72, 72);
                cancellationToken.ThrowIfCancellationRequested();
                return new RagOperationResult { Ok = true, Provider = "document-parser", Action = "preflight",
                    Details = new() { ["engine"] = Engine, ["python_required"] = false, ["native_renderer_ready"] = bitmap.Width == 72 } };
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "C# PDF parsing preflight failed.");
            return new RagOperationResult { Ok = false, Provider = "document-parser", Action = "preflight",
                ErrorCode = "environment_not_ready", ErrorMessage = "C# PDF 引擎未就绪，请检查发布目录中的 PDFium / SkiaSharp 原生库。" };
        }
        finally { Gate.Release(); }
    }

    public async Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
    {
        var idleLimit = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("DocumentParsing:TimeoutSeconds", 300), 5, 3600));
        using var timeout = new CancellationTokenSource(idleLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var entered = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var source = AllowedPath(request.FilePath);
            var output = AllowedPath(request.OutputDir);
            var checkpoints = request.PdfCheckpointDirectory is null ? null : AllowedPath(request.PdfCheckpointDirectory);
            if (!Path.GetExtension(source).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("仅支持 PDF 文件。");
            if (!File.Exists(source)) throw new FileNotFoundException("PDF 原文件不存在。");
            if (new FileInfo(source).Length > 256L * 1024 * 1024) throw new InvalidDataException("PDF 超过 256 MiB 解析上限。");
            await Gate.WaitAsync(linked.Token);
            entered = true;
            Action<PdfParseProgress>? progress = request.PdfProgress;
            if (checkpoints is not null)
            {
                progress = state =>
                {
                    timeout.CancelAfter(idleLimit);
                    request.PdfProgress?.Invoke(state);
                };
            }
            // 等待原生调用及写入真正退出再返回，不能让超时后的后台线程继续写 staging。
            return await Task.Run(() => CSharpPdfLayoutConverter.Convert(source, output, request.WriteImages, linked.Token, checkpoints, progress), linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Failure("parse_timeout", request.PdfCheckpointDirectory is null ? "PDF 解析超时，未发布本次结果。"
                : "PDF 解析超时。已完成的页面已保留，重试时将从未完成的页面继续。");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("path_not_allowed", "PDF 输入或输出不在允许的存储目录内，或路径包含符号链接。请检查 DocumentParsing:AllowedRoots。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "C# PDF parsing failed.");
            return Failure("parse_failed", ex is InvalidDataException ? ex.Message : "C# PDF 解析失败，请检查 PDF 是否损坏、加密，以及 PDFium / SkiaSharp 原生库是否完整。");
        }
        finally { if (entered) Gate.Release(); }
    }

    private static DocumentParseResult Failure(string code, string message) => new()
    { Ok = false, Engine = Engine, Action = "parse_pdf", ErrorCode = code, ErrorMessage = message };

    private string AllowedPath(string path) => DocumentConversionPaths.Validate(configuration, path);
}
