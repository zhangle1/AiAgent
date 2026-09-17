using AiAgent.Backend.Services.Knowledge.Core;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Parsing;
using ClosedXML.Excel;
using SqlSugar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AiAgent.Backend.Services.Knowledge;

public interface IKnowledgeIngestionService
{
    Task<KnowledgeProcessingResultDto> ProcessAsync(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document, KnowledgeProcessRequest request, CancellationToken cancellationToken, KnowledgeCompilerSettingsDto? settings = null, Action<int, string>? progress = null);
    KnowledgeDocumentContentDto GetContent(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document);
}

/// <summary>
/// Turns one immutable source file into a normalized parsed document and an independently reviewable AI artifact.
/// </summary>
public sealed class KnowledgeIngestionService : IKnowledgeIngestionService
{
    private const string PromptVersion = KnowledgeCompiler.Version;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".html", ".htm",
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", ".sql", ".sh", ".ps1", ".css"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly KnowledgeCompilerSettings _settings;
    private readonly ISqlSugarClient _db;
    private readonly IKnowledgePathService _paths;
    private readonly IDocumentParsingService _parser;
    private readonly ILlmChatClient _llm;
    private readonly ICodexChatService _codex;
    private readonly IConfiguration? _configuration;

    public KnowledgeIngestionService(ISqlSugarClient db, IKnowledgePathService paths, IDocumentParsingService parser, ILlmChatClient llm, ICodexChatService codex, KnowledgeCompilerSettings settings, IConfiguration? configuration = null)
    {
        _settings = settings;
        _configuration = configuration;
        _db = db;
        _paths = paths;
        _parser = parser;
        _llm = llm;
        _codex = codex;
    }

    public async Task<KnowledgeProcessingResultDto> ProcessAsync(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document, KnowledgeProcessRequest request, CancellationToken cancellationToken, KnowledgeCompilerSettingsDto? settings = null, Action<int, string>? progress = null)
    {
        var config = settings ?? _settings.Get();
        KnowledgeCompilerSettings.Validate(config);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes));
        cancellationToken = timeout.Token;
        var sourcePath = Path.GetFullPath(document.StoragePath);
        var kbPath = Path.GetFullPath(_paths.GetKnowledgeBasePath(knowledgeBase.Name));
        EnsureInside(sourcePath, kbPath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Knowledge source file does not exist.");

        progress?.Invoke(2, "等待文档处理器");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            UpdateDocument(document.Id, "parsing", null);
            progress?.Invoke(5, "正在解析原始文件");
            var parsed = await ParseAsync(knowledgeBase, document, sourcePath, cancellationToken);
            var parsedRow = new AiKnowledgeParsedDocument
            {
                KnowledgeBaseId = knowledgeBase.Id,
                DocumentId = document.Id,
                SourceHash = document.FileHash,
                Parser = parsed.Parser,
                ParserVersion = "1",
                ContentPath = parsed.ContentPath,
                LocatorJson = JsonSerializer.Serialize(parsed.Locators),
                CharacterCount = parsed.Content.Length,
                CreatedAt = DateTime.UtcNow
            };
            parsedRow.Id = _db.Insertable(parsedRow).ExecuteReturnBigIdentity();

            UpdateDocument(document.Id, "generating", null);
            progress?.Invoke(15, "解析完成，等待模型第 1 步响应");
            var generated = await GenerateAsync(knowledgeBase, document, parsed.Content, request, config, cancellationToken, progress);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_db.Queryable<AiKnowledgeDocument>().Any(x => x.Id == document.Id && !x.IsDeleted) ||
                !_db.Queryable<AiKnowledgeBase>().Any(x => x.Id == knowledgeBase.Id && !x.IsDeleted))
                throw new InvalidOperationException("The source was deleted during compilation.");
            progress?.Invoke(95, "证据校验完成，正在保存知识草稿");
            var artifact = new AiKnowledgeArtifact
            {
                KnowledgeBaseId = knowledgeBase.Id,
                DocumentId = document.Id,
                ParsedDocumentId = parsedRow.Id,
                ArtifactType = "knowledge_page",
                GeneratorType = request.Generator,
                Provider = generated.Provider,
                Model = generated.Model,
                PromptVersion = PromptVersion,
                Content = generated.Content,
                ReviewStatus = "draft",
                EvidenceJson = JsonSerializer.Serialize(new { source_document_id = document.Id, parsed_document_id = parsedRow.Id, source_hash = document.FileHash, pages = generated.Compilation.Pages, steps = generated.Compilation.Steps }),
                CreatedAt = DateTime.UtcNow
            };
            artifact.Id = _db.Insertable(artifact).ExecuteReturnBigIdentity();
            UpdateDocument(document.Id, "processed", null);

            return new KnowledgeProcessingResultDto
            {
                DocumentId = document.Id,
                ParsedDocumentId = parsedRow.Id,
                ArtifactId = artifact.Id,
                Status = "processed",
                Generator = request.Generator,
                Parser = parsed.Parser
            };
        }
        catch (Exception ex)
        {
            UpdateDocument(document.Id, ex is OperationCanceledException ? "cancelled" : "error", ex is OperationCanceledException ? "提炼已中断，可重试。" : "提炼失败，请检查文件或模型配置。");
            throw;
        }
        finally { _gate.Release(); }
    }

    public KnowledgeDocumentContentDto GetContent(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document)
    {
        var parsed = _db.Queryable<AiKnowledgeParsedDocument>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.DocumentId == document.Id)
            .OrderByDescending(x => x.Id).First();
        var artifact = _db.Queryable<AiKnowledgeArtifact>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.DocumentId == document.Id)
            .OrderByDescending(x => x.Id).First();
        string? parsedContent = null;
        if (parsed?.ContentPath is { Length: > 0 } path && File.Exists(path))
        {
            EnsureInside(Path.GetFullPath(path), Path.GetFullPath(_paths.GetKnowledgeBasePath(knowledgeBase.Name)));
            parsedContent = File.ReadAllText(path, Encoding.UTF8);
        }
        return new KnowledgeDocumentContentDto
        {
            DocumentId = document.Id,
            OriginalFileName = document.OriginalFileName,
            Status = document.Status,
            ParsedDocumentId = parsed?.Id,
            ParsedContent = parsedContent,
            Parser = parsed?.Parser,
            ArtifactId = artifact?.Id,
            ArtifactContent = artifact?.Content,
            Generator = artifact?.GeneratorType,
            Provider = artifact?.Provider,
            Model = artifact?.Model,
            ReviewStatus = artifact?.ReviewStatus
        };
    }

    private async Task<ParsedContent> ParseAsync(AiKnowledgeBase kb, AiKnowledgeDocument document, string sourcePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        using var converted = extension is ".doc" or ".xls"
            ? await KnowledgeOfficePreviewService.ConvertLegacyAsync(sourcePath, _configuration?["Knowledge:LibreOfficePath"], cancellationToken) : null;
        if (converted is not null) { sourcePath = converted.Path; extension = Path.GetExtension(sourcePath); }
        var outputDir = Path.Combine(_paths.GetKnowledgeBasePath(kb.Name), "parsed", document.Id.ToString());
        Directory.CreateDirectory(outputDir);
        string content;
        string parser;
        Dictionary<string, object?> locators = new();

        if (extension == ".pdf")
        {
            var result = await _parser.ParsePdfAsync(new DocumentParseRequest { FilePath = sourcePath, OutputDir = outputDir }, cancellationToken);
            if (!result.Ok || string.IsNullOrWhiteSpace(result.MarkdownPath)) throw new InvalidOperationException(result.ErrorMessage ?? "PDF parsing failed.");
            EnsureInside(Path.GetFullPath(result.MarkdownPath), Path.GetFullPath(outputDir));
            content = await File.ReadAllTextAsync(result.MarkdownPath, cancellationToken);
            parser = result.Engine;
            locators["page_count"] = result.PageCount;
        }
        else if (extension == ".xlsx")
        {
            (content, locators) = ParseWorkbook(sourcePath);
            parser = "closedxml";
        }
        else if (extension is ".docx" or ".pptx")
        {
            content = ParseOpenXml(sourcePath, extension);
            parser = "openxml";
        }
        else if (TextExtensions.Contains(extension))
        {
            content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
            if (extension is ".html" or ".htm") content = HtmlToText(content);
            parser = "text";
        }
        else
        {
            throw new NotSupportedException($"Document type '{extension}' is not supported for knowledge compilation.");
        }

        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("The parser returned empty content.");
        var target = Path.Combine(outputDir, $"content-{Guid.NewGuid():N}.md");
        await AtomicWriteAsync(target, content, cancellationToken);
        return new ParsedContent(content, target, parser, locators);
    }

    private async Task<GeneratedContent> GenerateAsync(AiKnowledgeBase kb, AiKnowledgeDocument document, string parsedContent, KnowledgeProcessRequest request, KnowledgeCompilerSettingsDto config, CancellationToken cancellationToken, Action<int, string>? progress)
    {
        var workspace = Path.Combine(_paths.GetKnowledgeBasePath(kb.Name), "compiler-runtime");
        Directory.CreateDirectory(workspace);
        var model = new KnowledgeModelAdapter(_llm, _codex, request, workspace);
        var compilation = await new KnowledgeCompiler().CompileAsync(parsedContent, model, config.MaxSteps,
            progress: step => {
                var action = step.Action switch { "read_source" => "读取原文", "propose_page" => "生成知识页", "finish" => "完成校验", _ => "解析模型响应" };
                var result = step.Result.StartsWith("Validation", StringComparison.Ordinal) ? "校验未通过，正在修正" : action;
                progress?.Invoke(15 + (step.TotalParts > 0 ? 75 * step.CoveredParts / step.TotalParts : 75),
                    $"第 {step.Number}/{config.MaxSteps} 步：{result}；等待下一步模型响应");
                return Task.CompletedTask;
            }, cancellationToken: cancellationToken);
        var content = new StringBuilder();
        foreach (var page in compilation.Pages)
        {
            content.AppendLine($"# {page.Title}\n\n{page.Markdown}\n\n## 来源证据");
            foreach (var evidence in page.Evidence)
                content.AppendLine($"- 来源文档 {document.Id} · 文本分段 {evidence.Part}:\n\n> {evidence.Quote.Replace("\n", "\n> ")}\n");
        }
        return new GeneratedContent(content.ToString(), compilation.Provider, compilation.Model, compilation);
    }

    private static (string Content, Dictionary<string, object?> Locators) ParseWorkbook(string path)
    {
        using var workbook = new XLWorkbook(path);
        var output = new StringBuilder();
        var sheets = new List<string>();
        foreach (var sheet in workbook.Worksheets)
        {
            sheets.Add(sheet.Name);
            output.AppendLine($"# Sheet: {sheet.Name}");
            var range = sheet.RangeUsed();
            if (range is null) continue;
            foreach (var row in range.Rows())
                output.AppendLine($"- Row {row.RowNumber()}: {string.Join(" | ", row.Cells().Select(x => x.GetFormattedString()))}");
        }
        return (output.ToString(), new Dictionary<string, object?> { ["sheets"] = sheets });
    }

    private static string ParseOpenXml(string path, string extension)
    {
        using var archive = ZipFile.OpenRead(path);
        var prefix = extension == ".docx" ? "word/" : "ppt/slides/";
        var entries = archive.Entries.Where(x => x.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.FullName);
        var output = new StringBuilder();
        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            var xml = XDocument.Load(stream);
            var text = string.Join(" ", xml.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (text.Length > 0) output.AppendLine($"# {entry.FullName}\n\n{text}\n");
        }
        return output.ToString();
    }

    private static string HtmlToText(string html) => Regex.Replace(Regex.Replace(html, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase), "<[^>]+>", " ");

    private static async Task AtomicWriteAsync(string target, string content, CancellationToken cancellationToken)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, target, true);
    }

    private static void EnsureInside(string path, string root)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Knowledge file is outside the allowed knowledge base directory.");
    }

    private void UpdateDocument(long id, string status, string? error)
    {
        _db.Updateable<AiKnowledgeDocument>().SetColumns(x => new AiKnowledgeDocument { Status = status, ErrorMessage = error, UpdatedAt = DateTime.UtcNow }).Where(x => x.Id == id).ExecuteCommand();
    }

    private sealed record ParsedContent(string Content, string ContentPath, string Parser, Dictionary<string, object?> Locators);
    private sealed record GeneratedContent(string Content, string? Provider, string? Model, Compilation Compilation);
}
