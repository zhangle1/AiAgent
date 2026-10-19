using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Parsing;
using AiAgent.Backend.Services.Rag;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task ParsingPublishesCompleteLayoutAndExposesReadonlySectionsToResourcesAndTools()
    {
        using var db = Database(); using var files = new SemanticFiles(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/docs/manual.md");
        var source = "# 概述\n\n" + new string('中', 2300) + "\n\n# 安装\n\n## Linux\n\n" + new string('文', 2400) + "\n\n## Windows\n\n" + new string('档', 2300);
        File.WriteAllText(doc.StoragePath, source); doc.FileHash = files.Paths.ComputeFileHash(doc.StoragePath); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); var api = new Llm(); var cli = new Codex();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        var result = await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var parsed = db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId);
        var store = new KnowledgeParsedDocumentStore(files.Paths); var layout = store.ReadManifest(kb, parsed)!;
        Assert.Equal(source, File.ReadAllText(parsed.ContentPath!)); Assert.True(layout.Files.Count >= 3);
        Assert.Equal(MarkdownDocumentPlanner.Version, ingestion.GetContent(kb, doc).ParsingVersion);
        Assert.Equal(layout.Files.Count, ingestion.GetContent(kb, doc).ParsedSectionCount);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(parsed.ContentPath!)!)!, ".staging-*"));
        Assert.Equal(source, string.Concat(layout.Files.OrderBy(f => f.Order).Select(entry => File.ReadAllText(store.SectionPath(kb, parsed, entry))[entry.RepeatedHeaderCharacters..])));
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, api, cli, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), semantic);
        var tree = resources.Tree(); var sections = tree.Where(n => n.Kind == "file" && KnowledgeResourceService.IsParsedUri(n.Uri)).ToList();
        Assert.Equal(layout.Files.Count, sections.Count); Assert.Single(resources.Processing());
        var read = await resources.ReadAsync(doc.ResourceUri!, default); Assert.Equal(layout.Files.Count, read.ParsedOutputs.Count); Assert.Empty(read.CompiledOutputs);
        foreach (var section in sections)
        {
            var part = await resources.ReadAsync(section.Uri, default);
            Assert.NotEmpty(part.ParsedContent!); Assert.Equal("not_applicable", part.SemanticStatus); Assert.Equal(doc.ResourceUri, part.Node.SourceUri);
            Assert.Equal(part.ParsedContent, File.ReadAllText(resources.File(section.Uri).Path));
            Assert.Throws<ArgumentException>(() => resources.Parse(section.Uri));
        }
        var tools = new AiAgent.Backend.Services.Knowledge.KnowAgent.KnowledgeAgentTools(resources);
        var toolText = await tools.ExecuteAsync("viking://resources/", new() { Command = "read", TargetUri = sections[0].Uri, Limit = 6000 }, default);
        Assert.Contains("content", toolText);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.CreateDirectoryAsync(new() { ParentUri = sections[0].ParentUri!, Name = "manual" }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.CreateDirectoryAsync(new() { ParentUri = "viking://resources/", Name = "_parsed" }, default));
        Assert.Equal(source, File.ReadAllText(doc.StoragePath)); Assert.Equal(99, db.Queryable<AiKnowledgeBase>().InSingle(kb.Id).ActiveVersionId);

        // Reparse publishes a replacement layout, without leaving old section names in the tree.
        var next = await ingestion.ParseResourceAsync(kb, doc, new() { Parsing = new() { SplitContent = false } }, default);
        Assert.NotEqual(result.ParsedDocumentId, next.ParsedDocumentId);
        Assert.Single(resources.Tree(), n => n.Kind == "file" && KnowledgeResourceService.IsParsedUri(n.Uri));
        Assert.Equal(source, ingestion.GetContent(kb, doc).ParsedContent);
    }

    [Fact]
    public async Task PdfConversionUsesSamePlannerAndCancelledReparseKeepsPublishedBody()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/source.pdf");
        var pdf = Path.ChangeExtension(doc.StoragePath, ".pdf"); File.Move(doc.StoragePath, pdf); doc.StoragePath = pdf; doc.OriginalFileName = "source.pdf";
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var parser = new FixtureDocumentParser();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, parser, new Llm(), new Codex(), new(db));
        var result = await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var parsed = db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId);
        var layout = new KnowledgeParsedDocumentStore(files.Paths).ReadManifest(kb, parsed)!;
        Assert.True(layout.Files.Count > 1); Assert.Contains(".staging-", parser.OutputDirectory);
        Assert.Contains("END_OF_PDF", ingestion.GetContent(kb, doc).ParsedContent);
        Assert.Contains("page_count", parsed.LocatorJson);
        parser.Cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ingestion.ParseResourceAsync(kb, doc, new(), default));
        Assert.Single(db.Queryable<AiKnowledgeParsedDocument>().ToList()); Assert.Equal(parsed.ContentPath, db.Queryable<AiKnowledgeParsedDocument>().First().ContentPath);
        Assert.Contains("END_OF_PDF", ingestion.GetContent(kb, doc).ParsedContent);
        var documentRoot = Path.GetDirectoryName(Path.GetDirectoryName(parsed.ContentPath!)!)!;
        Assert.Empty(Directory.GetDirectories(documentRoot, ".staging-*"));
    }

    [Fact]
    public async Task ImageTranscriptionUsesSameLayoutWithoutSummarizingOrFixedOutputCap()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/scan.png");
        var path = Path.ChangeExtension(doc.StoragePath, ".png"); File.WriteAllBytes(path, new byte[] { 137, 80, 78, 71 });
        doc.StoragePath = path; doc.OriginalFileName = "scan.png"; doc.ContentType = "image/png"; doc.FileHash = files.Paths.ComputeFileHash(path); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var api = new Llm(); var transcript = "# 图像转录\n\n" + new string('字', 6000); api.Replies.Enqueue(transcript);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, new Codex(), new(db));
        var result = await ingestion.ParseResourceAsync(kb, doc, new() { VlmModelId = "vision" }, default);
        Assert.Single(api.Inputs); Assert.Equal(0, api.MaxOutputTokens); Assert.Equal("vision", api.ModelId);
        Assert.Contains("Do not summarize or omit content", api.Inputs[0][0].Content);
        Assert.Equal(transcript, ingestion.GetContent(kb, doc).ParsedContent);
        Assert.True(ingestion.GetContent(kb, doc).ParsedSectionCount > 1);
        Assert.Contains("derived_from_image", db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId).LocatorJson);
    }

    [Fact]
    public async Task CancelledPublishDoesNotExposePartialGenerationOrReplacePreviousOne()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity(); var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), new(db));
        var result = await ingestion.ParseResourceAsync(kb, doc, new(), default); var row = db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId);
        var store = new KnowledgeParsedDocumentStore(files.Paths); var original = store.ReadManifest(kb, row)!;
        var staging = store.NewStaging(kb, doc.Id); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try
        {
            var content = "replacement";
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PublishAsync(staging, content, new MarkdownDocumentPlanner().Plan(content, "manual.md"), new(), cancel.Token));
        }
        finally { store.DiscardStaging(staging); }
        Assert.Equal(original.ContentHash, store.ReadManifest(kb, row)!.ContentHash);
        Assert.Single(db.Queryable<AiKnowledgeParsedDocument>().ToList()); Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
    }

    [Fact]
    public async Task DeepChineseHeadingsHaveReadableUrisWithoutBreakingWholeTree()
    {
        using var db = Database(); using var files = new SemanticFiles(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/long.md");
        var source = string.Join("\n\n", Enumerable.Range(1, 6).Select(level => new string('#', level) + " " + new string('章', 48))) + "\n\n" + new string('中', 10000);
        File.WriteAllText(doc.StoragePath, source); doc.FileHash = files.Paths.ComputeFileHash(doc.StoragePath); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); var api = new Llm(); var cli = new Codex(); var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, api, cli, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), semantic);
        var section = resources.Tree().Last(n => n.Kind == "file" && KnowledgeResourceService.IsParsedUri(n.Uri));
        Assert.True(section.Uri.Length > 1000); Assert.NotEmpty((await resources.ReadAsync(section.Uri, default)).ParsedContent!);
        Assert.Equal(source, ingestion.GetContent(kb, doc).ParsedContent);
    }

    [Fact]
    public async Task HtmlKeepsHeadingsTablesAndCodeAsSeparateMarkdownBlocks()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/sample.html");
        var path = Path.ChangeExtension(doc.StoragePath, ".html");
        File.WriteAllText(path, "<html><style>IGNORE_STYLE</style><h1>安装</h1><p>正文 &amp; 内容</p><pre><code># 假标题\nprint(&quot;hello&quot;)</code></pre><table><tr><th>列</th><th>值</th></tr><tr><td>A|B</td><td>一<br>二</td></tr></table><script>IGNORE_SCRIPT</script><h2>后续</h2><p>结束</p></html>");
        doc.StoragePath = path; doc.OriginalFileName = "sample.html"; doc.FileHash = files.Paths.ComputeFileHash(path); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), new(db));
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var text = ingestion.GetContent(kb, doc).ParsedContent!;
        Assert.Contains("# 安装", text); Assert.Contains("正文 & 内容", text); Assert.Contains("```\n# 假标题", text);
        Assert.Contains("| 列 | 值 |", text); Assert.Contains(@"A\|B", text); Assert.Contains("一<br>二", text);
        Assert.DoesNotContain("IGNORE_STYLE", text); Assert.DoesNotContain("IGNORE_SCRIPT", text);
        Assert.DoesNotContain("\u0001parsed-block-", text);
    }

    [Fact]
    public async Task MissingOrCorruptLayoutNeverHidesFullBodyAndCorruptSectionCannotBeDownloaded()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/manual.md");
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity(); var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), new(db));
        var result = await ingestion.ParseResourceAsync(kb, doc, new(), default); var row = db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId);
        var store = new KnowledgeParsedDocumentStore(files.Paths); var layout = store.ReadManifest(kb, row)!;
        var path = store.SectionPath(kb, row, layout.Files[0]); File.WriteAllText(path, "tampered");
        Assert.Throws<InvalidOperationException>(() => store.SectionPath(kb, row, layout.Files[0]));
        var manifestPath = Path.Combine(Path.GetDirectoryName(row.ContentPath!)!, "layout.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(layout with { Files = null! }));
        Assert.Null(store.ReadManifest(kb, row));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(row.ContentPath!)!, "layout.json"), "{broken");
        Assert.Null(store.ReadManifest(kb, row)); Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
        row.ParserVersion = "1"; db.Updateable(row).ExecuteCommand();
        Assert.Null(ingestion.GetContent(kb, doc).ParsedSectionCount); Assert.Contains("每周三发布", ingestion.GetContent(kb, doc).ParsedContent);
    }

    [Fact]
    public async Task PersonalParsedSectionsAreInvisibleToOtherUsers()
    {
        using var db = Database(); using var files = new SemanticFiles(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://user/bob/personal/manual.md"); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); var api = new Llm(); var cli = new Codex();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings); await ingestion.ParseResourceAsync(kb, doc, new(), default);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, api, cli, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), semantic);
        Assert.DoesNotContain(resources.Tree(), n => n.DocumentId == doc.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resources.ReadAsync("viking://user/bob/personal/_parsed/manual-" + doc.Id + "/manual.md", default));
    }

    [Fact]
    public void ParsingOptionsAreValidatedAndSavedAsImmutableTaskSnapshot()
    {
        using var db = Database(); var kb = CreateBase(db);
        var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "snapshot.md" }; doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db);
        using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var config = new KnowledgeCompilerSettingsDto { Parsing = new() { SplitContent = false, MaxSectionChars = 9999 } };
        var job = worker.Enqueue(kb.Name, doc.Id, configuration: config);
        config.Parsing.MaxSectionChars = 22; config.Parsing.SplitContent = true;
        var snapshot = JsonSerializer.Deserialize<KnowledgeCompilerSettingsDto>(db.Queryable<AiKnowledgeJob>().InSingle(job.Id).ConfigurationJson!)!;
        Assert.False(snapshot.Parsing.SplitContent); Assert.Equal(9999, snapshot.Parsing.MaxSectionChars);
        Assert.Throws<ArgumentException>(() => KnowledgeCompilerSettings.Validate(new() { Parsing = new() { MaxSectionTokens = 0 } }));
        Assert.Throws<ArgumentException>(() => KnowledgeCompilerSettings.Validate(new() { Parsing = new() { MinSectionTokens = 2049 } }));
    }

    private sealed class FixtureDocumentParser : IDocumentParsingService
    {
        public bool Cancel; public string? OutputDirectory;
        public Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
        {
            OutputDirectory = request.OutputDir; Directory.CreateDirectory(request.OutputDir);
            var path = Path.Combine(request.OutputDir, "pdf.md");
            await File.WriteAllTextAsync(path, "# PDF 标题\n\n" + new string('字', 6000) + "\n\nEND_OF_PDF", cancellationToken);
            if (Cancel) throw new OperationCanceledException();
            return new() { Ok = true, MarkdownPath = path, Engine = "fixture-pdf", PageCount = 12 };
        }
    }
}

public sealed class OfficeMarkdownConverterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiagent-office-parser-" + Guid.NewGuid().ToString("N"));
    public OfficeMarkdownConverterTests() => Directory.CreateDirectory(_root);
    [Fact]
    public void WordKeepsHeadingsParagraphsAndTablesWithoutLeakingStyles()
    {
        var file = Zip("sample.docx", new() {
            ["word/document.xml"] = """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="CustomTitle"/></w:pPr><w:r><w:t>安装</w:t></w:r></w:p><w:p><w:r><w:t>第一段</w:t><w:br/><w:t>第二行</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>字段</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>值</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>A|B</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>数据</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>""",
            ["word/styles.xml"] = """<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:styleId="CustomTitle"><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>STYLE_ONLY</w:t></w:r></w:style></w:styles>""",
        });
        var text = OfficeMarkdownConverter.ReadOpenXml(file, ".docx", default);
        Assert.Contains("## 安装", text); Assert.Contains("第一段\n第二行", text); Assert.Contains("| 字段 | 值 |", text); Assert.Contains(@"A\|B", text); Assert.DoesNotContain("STYLE_ONLY", text);
    }
    [Fact]
    public void WorkbookPreservesEmptyColumnsAndSheetNames()
    {
        var path = Path.Combine(_root, "sheets.xlsx");
        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("项目资料"); sheet.Cell(3, 3).Value = "A"; sheet.Cell(3, 5).Value = "C";
            sheet.Cell(4, 3).Value = "下一行"; sheet.Cell(4, 5).Value = "多行\n内容"; workbook.SaveAs(path);
        }
        var result = OfficeMarkdownConverter.ReadWorkbook(path, default);
        Assert.Contains("# 项目资料", result.Content); Assert.Contains("| A |  | C |", result.Content); Assert.Contains("多行<br>内容", result.Content);
        Assert.Equal(new[] { "项目资料" }, Assert.IsType<List<string>>(result.Locators["sheets"]));
    }
    [Fact]
    public void PptUsesPresentationOrderInsteadOfLexicalZipOrder()
    {
        const string prefix = """<p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>""";
        const string suffix = "</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>";
        var file = Zip("sample.pptx", new() {
            ["ppt/presentation.xml"] = """<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:sldIdLst><p:sldId r:id="b"/><p:sldId r:id="a"/></p:sldIdLst></p:presentation>""",
            ["ppt/_rels/presentation.xml.rels"] = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="a" Target="slides/slide1.xml"/><Relationship Id="b" Target="slides/slide10.xml"/></Relationships>""",
            ["ppt/slides/slide1.xml"] = prefix + "SECOND" + suffix, ["ppt/slides/slide10.xml"] = prefix + "FIRST" + suffix,
        });
        var text = OfficeMarkdownConverter.ReadOpenXml(file, ".pptx", default);
        Assert.True(text.IndexOf("FIRST", StringComparison.Ordinal) < text.IndexOf("SECOND", StringComparison.Ordinal));
        Assert.Contains("# 幻灯片 2", text);
    }
    private string Zip(string name, Dictionary<string, string> entries)
    {
        var path = Path.Combine(_root, name); using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var writer = new StreamWriter(archive.CreateEntry(entry.Key).Open()); writer.Write(entry.Value); }
        return path;
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(_root);
        if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("aiagent-office-parser-", StringComparison.Ordinal)) Directory.Delete(full, true);
    }
}
