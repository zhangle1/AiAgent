using AiAgent.Backend.Services.Knowledge.Core;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Parsing;
using SqlSugar;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Knowledge;

public interface IKnowledgeIngestionService
{
    Task<KnowledgeProcessingResultDto> ParseResourceAsync(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document, KnowledgeProcessRequest request, CancellationToken cancellationToken, Action<int, string>? progress = null);
    Task<KnowledgeProcessingResultDto> ProcessAsync(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document, KnowledgeProcessRequest request, CancellationToken cancellationToken, KnowledgeCompilerSettingsDto? settings = null, Action<int, string>? progress = null);
    KnowledgeDocumentContentDto GetContent(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document);
}

/// <summary>
/// Turns one immutable source file into a normalized parsed document and an independently reviewable AI artifact.
/// </summary>
public sealed class KnowledgeIngestionService : IKnowledgeIngestionService
{
    private const string PromptVersion = KnowledgeCompiler.Version;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly KnowledgeCompilerSettings _settings;
    private readonly ISqlSugarClient _db;
    private readonly IKnowledgePathService _paths;
    private readonly IDocumentConversionService _conversion;
    private readonly ILlmChatClient _llm;
    private readonly ICodexChatService _codex;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public KnowledgeIngestionService(ISqlSugarClient db, IKnowledgePathService paths, IDocumentParsingService parser, ILlmChatClient llm, ICodexChatService codex, KnowledgeCompilerSettings settings, IConfiguration? configuration = null, IHttpContextAccessor? httpContextAccessor = null, IDocumentConversionService? conversion = null)
    {
        _settings = settings;
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _paths = paths;
        _conversion = conversion ?? new DocumentConversionService(new ConfigurationBuilder()
            .AddConfiguration(configuration ?? new ConfigurationBuilder().Build())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DocumentParsing:AllowedRoots:0"] = paths.RootPath }).Build(), parser, llm);
        _llm = llm;
        _codex = codex;
    }

    public async Task<KnowledgeProcessingResultDto> ProcessAsync(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document, KnowledgeProcessRequest request, CancellationToken cancellationToken, KnowledgeCompilerSettingsDto? settings = null, Action<int, string>? progress = null)
    {
        using var scopeDb = _db.CopyNew();
        request.OwnerRoot ??= _httpContextAccessor is null ? null : KnowledgeUserScope.Resolve(scopeDb, _httpContextAccessor);
        var config = settings ?? _settings.Get();
        KnowledgeCompilerSettings.Validate(config);
        request.Parsing ??= config.Parsing;
        var callerCancellationToken = cancellationToken;
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
            using (var db = _db.CopyNew())
                UpdateDocument(db, document.Id, "parsing", null);
            progress?.Invoke(5, "正在解析原始文件");
            var parsed = await ParseAsync(knowledgeBase, document, sourcePath, request, cancellationToken, progress, 9);
            var parsedRow = new AiKnowledgeParsedDocument
            {
                KnowledgeBaseId = knowledgeBase.Id,
                DocumentId = document.Id,
                SourceHash = document.FileHash,
                Parser = parsed.Parser,
                ParserVersion = MarkdownDocumentPlanner.Version,
                ContentPath = parsed.ContentPath,
                ContentHash = KnowledgeSemanticStore.Hash(parsed.Content),
                LocatorJson = JsonSerializer.Serialize(parsed.Locators),
                CharacterCount = parsed.Content.Length,
                CreatedAt = DateTime.UtcNow
            };
            using (var db = _db.CopyNew())
                parsedRow.Id = db.Insertable(parsedRow).ExecuteReturnBigIdentity();
            if (IsPdf(sourcePath)) new KnowledgeParsedDocumentStore(_paths).ClearPdfCheckpoints(knowledgeBase, document.Id);

            using (var db = _db.CopyNew())
                UpdateDocument(db, document.Id, "generating", null);
            progress?.Invoke(15, "阶段 1/2：正在分析原文中的实体、概念与矛盾");
            var generated = await GenerateAsync(knowledgeBase, document, parsed.Content, request, config, cancellationToken, progress);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(95, "证据校验完成，正在保存知识草稿");
            var artifact = new AiKnowledgeArtifact
            {
                OwnerRoot = request.OwnerRoot,
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
            if (artifact.OwnerRoot is not null) new KnowledgeSemanticStore(_paths).MaterializeWiki(artifact.OwnerRoot, artifact.Content!);
            using (var db = _db.CopyNew())
            {
                if (!db.Queryable<AiKnowledgeDocument>().Any(x => x.Id == document.Id && !x.IsDeleted) ||
                    !db.Queryable<AiKnowledgeBase>().Any(x => x.Id == knowledgeBase.Id && !x.IsDeleted))
                    throw new InvalidOperationException("The source was deleted during compilation.");
                db.Ado.BeginTran();
                try
                {
                    artifact.Id = db.Insertable(artifact).ExecuteReturnBigIdentity();
                    UpdateDocument(db, document.Id, "processed", null);
                    db.Ado.CommitTran();
                }
                catch
                {
                    db.Ado.RollbackTran();
                    throw;
                }
            }

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
            var cancelledByCaller = ex is OperationCanceledException && callerCancellationToken.IsCancellationRequested;
            try
            {
                using var failureDb = _db.CopyNew();
                UpdateDocument(failureDb, document.Id, cancelledByCaller ? "cancelled" : "error", cancelledByCaller ? "知识整理已中断，可重试。" :
                    ex is OperationCanceledException ? "知识整理超时或服务停止，请检查模型连接或拆分资料后重试。" : $"文档解析失败：{SafeError(ex)}");
            }
            catch (Exception statusError) { ex.Data["StatusPersistenceErrorType"] = statusError.GetType().Name; }
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<KnowledgeProcessingResultDto> ParseResourceAsync(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document,
        KnowledgeProcessRequest request, CancellationToken cancellationToken, Action<int, string>? progress = null)
    {
        request.Parsing ??= _settings.Get().Parsing;
        request.Parsing.Validate();
        var sourcePath = Path.GetFullPath(document.StoragePath);
        EnsureInside(sourcePath, Path.GetFullPath(_paths.GetKnowledgeBasePath(knowledgeBase.Name)));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("原始资料不存在。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using (var db = _db.CopyNew()) UpdateDocument(db, document.Id, "parsing", null);
            progress?.Invoke(5, "正在解析原文；PDF 本地提取章节与图片，独立图片按配置转录");
            var parsed = await ParseAsync(knowledgeBase, document, sourcePath, request, cancellationToken, progress, 80);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(90, "正文解析完成，正在保存");
            using var saveDb = _db.CopyNew();
            if (!saveDb.Queryable<AiKnowledgeDocument>().Any(x => x.Id == document.Id && !x.IsDeleted) ||
                !saveDb.Queryable<AiKnowledgeBase>().Any(x => x.Id == knowledgeBase.Id && !x.IsDeleted))
                throw new InvalidOperationException("资料在解析期间已删除。");
            var row = new AiKnowledgeParsedDocument { KnowledgeBaseId = knowledgeBase.Id, DocumentId = document.Id,
                SourceHash = document.FileHash, ContentHash = KnowledgeSemanticStore.Hash(parsed.Content), Parser = parsed.Parser, ParserVersion = MarkdownDocumentPlanner.Version, ContentPath = parsed.ContentPath,
                LocatorJson = JsonSerializer.Serialize(parsed.Locators), CharacterCount = parsed.Content.Length, CreatedAt = DateTime.UtcNow };
            saveDb.Ado.BeginTran();
            try
            {
                row.Id = saveDb.Insertable(row).ExecuteReturnBigIdentity();
                UpdateDocument(saveDb, document.Id, "processed", null);
                saveDb.Ado.CommitTran();
            }
            catch { saveDb.Ado.RollbackTran(); throw; }
            if (IsPdf(sourcePath)) new KnowledgeParsedDocumentStore(_paths).ClearPdfCheckpoints(knowledgeBase, document.Id);
            return new() { DocumentId = document.Id, ParsedDocumentId = row.Id, Status = "processed", Parser = parsed.Parser };
        }
        catch (Exception ex)
        {
            try
            {
                using var db = _db.CopyNew();
                var interrupted = ex is OperationCanceledException;
                UpdateDocument(db, document.Id, interrupted ? "cancelled" : "error", interrupted
                    ? IsPdf(sourcePath) ? "PDF 解析已中断，重试时将复用已完成的页面。" : "解析已中断，可重试。"
                    : $"文档解析失败：{SafeError(ex)}");
            }
            catch (Exception stateError) { ex.Data["StatusPersistenceErrorType"] = stateError.GetType().Name; }
            throw;
        }
        finally { _gate.Release(); }
    }

    public KnowledgeDocumentContentDto GetContent(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document)
    {
        using var db = _db.CopyNew();
        var parsed = db.Queryable<AiKnowledgeParsedDocument>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.DocumentId == document.Id)
            .OrderByDescending(x => x.Id).First();
        var ownerRoot = _httpContextAccessor is null ? null : KnowledgeUserScope.Resolve(db, _httpContextAccessor);
        var artifact = db.Queryable<AiKnowledgeArtifact>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.DocumentId == document.Id && (x.OwnerRoot == null || x.OwnerRoot == ownerRoot))
            .OrderByDescending(x => x.Id).First();
        string? parsedContent = null;
        if (parsed?.ContentPath is { Length: > 0 } path && File.Exists(path))
        {
            EnsureInside(Path.GetFullPath(path), Path.GetFullPath(_paths.GetKnowledgeBasePath(knowledgeBase.Name)));
            parsedContent = File.ReadAllText(path, Encoding.UTF8);
        }
        return new KnowledgeDocumentContentDto
        {
            ParsingVersion = parsed?.ParserVersion,
            ParsedSectionCount = parsed is null ? null : new KnowledgeParsedDocumentStore(_paths).ReadManifest(knowledgeBase, parsed)?.Files.Count,
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

    private static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    private async Task<ParsedContent> ParseAsync(AiKnowledgeBase kb, AiKnowledgeDocument document, string sourcePath,
        KnowledgeProcessRequest request, CancellationToken cancellationToken, Action<int, string>? progress, int pdfProgressRange)
    {
        var options = request.Parsing ?? new(); options.Validate();
        var store = new KnowledgeParsedDocumentStore(_paths);
        var staging = store.NewStaging(kb, document.Id);
        try
        {
            var checkpointDirectory = IsPdf(sourcePath) ? store.PdfCheckpointDirectory(kb, document.Id) : null;
            var conversion = Path.Combine(staging, "conversion");
            var extracted = await ExtractAsync(new DocumentParseRequest {
                FilePath = sourcePath, OutputDir = conversion, VlmModelId = request.VlmModelId, SourceName = document.OriginalFileName,
                Parsing = options, WriteBundle = false, PdfCheckpointDirectory = checkpointDirectory,
                PdfProgress = progress is null ? null : page =>
                {
                    var message = page switch
                    {
                        _ when page.Completed == page.Total => $"已完成 {page.Total} 页，正在整理图片和正文",
                        _ when page.Completed + page.Failed == page.Total => $"已检查全部 {page.Total} 页 · 已完成 {page.Completed} 页",
                        _ => $"解析第 {page.Page}/{page.Total} 页 · 第 {page.Batch}/{page.TotalBatches} 批 · 已完成 {page.Completed} 页"
                    };
                    if (page.Reused > 0) message += $" · 复用 {page.Reused} 页";
                    if (page.Failed > 0) message += $" · {page.Failed} 页待重试";
                    progress(5 + pdfProgressRange * page.Completed / page.Total, message);
                }
            }, cancellationToken);
            if (string.IsNullOrWhiteSpace(extracted.Content)) throw new InvalidOperationException(IsPdf(sourcePath)
                ? "PDF 未提取到可读正文。当前 PDF 解析依赖文本层；扫描件请先完成 OCR，或上传图片并选择 VLM。" : "解析器未返回可读正文。");
            var plan = new MarkdownDocumentPlanner().Plan(extracted.Content, document.OriginalFileName, options, cancellationToken);
            var published = await store.PublishAsync(staging, extracted.Content, plan, options, cancellationToken, extracted.Assets, conversion);
            extracted.Locators["layout_version"] = MarkdownDocumentPlanner.Version;
            extracted.Locators["section_count"] = published.Layout.Files.Count;
            extracted.Locators["layout_manifest"] = "layout.json";
            return new(extracted.Content, published.ContentPath, extracted.Parser, extracted.Locators);
        }
        catch (Exception ex)
        {
            try { store.DiscardStaging(staging); }
            catch (Exception cleanupError) { ex.Data["StagingCleanupErrorType"] = cleanupError.GetType().Name; }
            throw;
        }
    }

    private async Task<ExtractedContent> ExtractAsync(DocumentParseRequest request, CancellationToken cancellationToken)
    {
        var result = await _conversion.ConvertAsync(request, cancellationToken);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.MarkdownPath))
            throw new InvalidOperationException(result.ErrorMessage ?? "Document conversion failed.");
        EnsureInside(Path.GetFullPath(result.MarkdownPath), Path.GetFullPath(request.OutputDir));
        var content = await File.ReadAllTextAsync(result.MarkdownPath, cancellationToken);
        var locators = result.Details;
        locators["warnings"] = result.Warnings;
        if (result.PageCount > 0) locators["page_count"] = result.PageCount;
        return new(content, result.Engine, locators, result.Assets);
    }

    private async Task<GeneratedContent> GenerateAsync(AiKnowledgeBase kb, AiKnowledgeDocument document, string parsedContent, KnowledgeProcessRequest request, KnowledgeCompilerSettingsDto config, CancellationToken cancellationToken, Action<int, string>? progress)
    {
        var workspace = Path.Combine(_paths.GetKnowledgeBasePath(kb.Name), "compiler-runtime");
        Directory.CreateDirectory(workspace);
        var model = new KnowledgeModelAdapter(_llm, _codex, request, workspace);
        string wikiContext;
        using (var contextDb = _db.CopyNew())
        {
            wikiContext = string.Join("\n", new KnowledgeWorkspaceService(contextDb).ListPages(kb.Name, request.OwnerRoot)
                .Where(page => page.DocumentId != document.Id).Take(40)
                .Select(page => $"{page.Title}（{page.ReviewStatus}）"));
        }
        var currentProgress = 15;
        var compilation = await new KnowAgent.KnowAgent(model, config.MaxSteps, message => progress?.Invoke(currentProgress, message)).CompileAsync(parsedContent,
            report: step => {
                var analyzing = step.Action == "analyze_source";
                var stage = analyzing ? "阶段 1/2：分析原文" : "阶段 2/2：生成并校验知识页";
                var result = step.Result.StartsWith("Validation", StringComparison.Ordinal) ? "校验未通过，正在修正" :
                    step.Result == "started" ? "正在处理，已完成" : "已完成";
                currentProgress = (analyzing ? 15 : 45) + (analyzing ? 30 : 45) * step.CoveredParts / step.TotalParts;
                progress?.Invoke(currentProgress,
                    $"{stage}；{result} {step.CoveredParts}/{step.TotalParts} 段（模型调用 {KnowAgent.ContextBudget.CallsLabel(step.Number, config.MaxSteps)}）");
                return Task.CompletedTask;
            }, ct: cancellationToken, wikiContext: wikiContext);
        var content = new StringBuilder();
        foreach (var page in compilation.Pages)
        {
            content.AppendLine($"# {page.Title}\n\n{page.Markdown}\n\n## 来源证据");
            foreach (var evidence in page.Evidence)
                content.AppendLine($"- 来源文档 {document.Id} · 文本分段 {evidence.Part}:\n\n> {evidence.Quote.Replace("\n", "\n> ")}\n");
        }
        return new GeneratedContent(content.ToString(), compilation.Provider, compilation.Model, compilation);
    }

    private static void EnsureInside(string path, string root)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Knowledge file is outside the allowed knowledge base directory.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("知识文件路径不能包含符号链接。");
    }

    private static void UpdateDocument(ISqlSugarClient db, long id, string status, string? error)
    {
        db.Updateable<AiKnowledgeDocument>().SetColumns(x => new AiKnowledgeDocument { Status = status, ErrorMessage = error, UpdatedAt = DateTime.UtcNow }).Where(x => x.Id == id).ExecuteCommand();
    }

    private static string SafeError(Exception exception)
    {
        var message = exception.GetBaseException().Message.Trim();
        return message.Length <= 500 ? message : message[..500] + "…";
    }

    private sealed record ParsedContent(string Content, string ContentPath, string Parser, Dictionary<string, object?> Locators);
    private sealed record ExtractedContent(string Content, string Parser, Dictionary<string, object?> Locators, List<DocumentParseAsset> Assets);
    private sealed record GeneratedContent(string Content, string? Provider, string? Model, Compilation Compilation);
}
