using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Parsing;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task PublishingParsedImageDoesNotRejectAnAssetAboveTheOldSizeLimit()
    {
        using var db = Database();
        using var files = new SemanticFiles();
        var kb = CreateBase(db);
        var store = new KnowledgeParsedDocumentStore(files.Paths);
        var staging = store.NewStaging(kb, 1);
        var conversion = Path.Combine(staging, "conversion");
        const string relative = "assets/figure.png";
        var source = Path.Combine(conversion, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        using (var png = new FileStream(source, FileMode.CreateNew))
        {
            png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            png.SetLength(33 * 1024 * 1024);
        }
        string hash;
        using (var png = File.OpenRead(source))
            hash = System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));
        var content = $"# Figure\n\n![Figure]({relative})";
        var options = new MarkdownParsingOptions();
        var plan = new MarkdownDocumentPlanner().Plan(content, "book.pdf", options);
        var result = await store.PublishAsync(staging, content, plan, options, default,
            [new DocumentParseAsset { Path = relative, Sha256 = hash, Page = 1, Kind = "image" }], conversion);

        Assert.Equal(hash, Assert.Single(result.Layout.Assets!).Hash);
        Assert.Equal(33 * 1024 * 1024L, new FileInfo(Path.Combine(Path.GetDirectoryName(result.ContentPath)!, relative)).Length);
    }

    [Fact]
    public async Task KnowledgePdfRetryPublishesOneCompleteVersionAndCleansCheckpoints()
    {
        using var db = Database();
        using var files = new SemanticFiles();
        using var cancellation = new CancellationTokenSource();
        var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/book.pdf");
        var pdf = Path.ChangeExtension(doc.StoragePath, ".pdf");
        File.Move(doc.StoragePath, pdf);
        File.WriteAllBytes(pdf, CSharpPdfParsingTests.LayoutPdf());
        doc.StoragePath = pdf;
        doc.Extension = ".pdf";
        doc.OriginalFileName = "book.pdf";
        doc.FileHash = files.Paths.ComputeFileHash(pdf);
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentParsing:AllowedRoots:0"] = files.Paths.RootPath
        }).Build();
        var parser = new DocumentParsingService(config, NullLogger<DocumentParsingService>.Instance);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, parser, new Llm(), new Codex(), new(db));
        var store = new KnowledgeParsedDocumentStore(files.Paths);
        var directory = store.PdfCheckpointDirectory(kb, doc.Id);
        var original = await ingestion.ParseResourceAsync(kb, doc, new(), default);
        Assert.False(Directory.Exists(directory));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ingestion.ParseResourceAsync(kb, doc, new(), cancellation.Token,
            (_, message) => { if (message.StartsWith("已完成 2 页")) cancellation.Cancel(); }));
        var existingVersion = Assert.Single(db.Queryable<AiKnowledgeParsedDocument>().Where(r => r.DocumentId == doc.Id).ToList());
        Assert.Equal(original.ParsedDocumentId, existingVersion.Id);
        Assert.NotEmpty(store.ReadManifest(kb, existingVersion)!.Assets!);
        Assert.Equal(2, Directory.GetFiles(directory, "page-*.json", SearchOption.AllDirectories).Length);

        var progress = new List<string>();
        var retry = await ingestion.ParseResourceAsync(kb, doc, new(), default, (_, message) => progress.Add(message));
        Assert.Contains(progress, message => message.Contains("复用 2 页"));
        Assert.NotEqual(original.ParsedDocumentId, retry.ParsedDocumentId);
        var published = db.Queryable<AiKnowledgeParsedDocument>().InSingle(retry.ParsedDocumentId);
        var layout = store.ReadManifest(kb, published)!;
        Assert.All(layout.Assets!, asset => Assert.True(File.Exists(store.AssetPath(kb, published, asset))));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task GbkTextRawPreviewAndPublishedMarkdownUseSameDecoder()
    {
        using var db = Database(); using var files = new SemanticFiles(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/chinese.txt");
        doc.Extension = ".txt"; doc.OriginalFileName = "chinese.txt";
        const string source = "# 原始标题\n\n中文正文，保留编码与内容。";
        var bytes = System.Text.Encoding.GetEncoding(54936).GetBytes(source);
        File.WriteAllBytes(doc.StoragePath, bytes); doc.FileHash = files.Paths.ComputeFileHash(doc.StoragePath); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); var api = new Llm(); var cli = new Codex();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, null!, settings, api, cli, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), semantic);
        var read = await resources.ReadAsync(doc.ResourceUri!, default);
        Assert.Equal(source, read.SourceText); Assert.Equal(source, read.ParsedContent); Assert.NotNull(read.ParsedIndex);
        Assert.Equal(bytes, File.ReadAllBytes(doc.StoragePath));
    }

    [Fact]
    public async Task PublishedIndexesNavigateOwnedChaptersAndHashFailureDoesNotHideFullBody()
    {
        using var db = Database(); using var files = new SemanticFiles(); db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db); var doc = files.Document(kb, "viking://resources/manual.txt");
        var source = "# 第一章\n\n父级说明\n\n## Linux 配置\n\n" + new string('字', 200) + "\n\n## Windows 配置\n\n" + new string('文', 200);
        File.WriteAllText(doc.StoragePath, source); doc.FileHash = files.Paths.ComputeFileHash(doc.StoragePath); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db); var api = new Llm(); var cli = new Codex();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, api, cli, settings);
        var result = await ingestion.ParseResourceAsync(kb, doc, new() { Parsing = new() { MaxSectionChars = 140, MaxSectionTokens = 80, MinSectionTokens = 10 } }, default);
        var row = db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId);
        using var worker = new KnowledgeCompilationWorker(db, settings, ingestion, NullLogger<KnowledgeCompilationWorker>.Instance);
        var semantic = new KnowledgeResourceSemanticService(db, ingestion, files.Paths, new(files.Paths), api, cli);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, worker, settings, api, cli, NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), semantic);
        var store = new KnowledgeParsedDocumentStore(files.Paths); var layout = store.ReadManifest(kb, row)!;
        Assert.True(layout.Navigation!.Count >= 2);
        var chapterDirectory = Assert.Single(resources.Tree(), node => node.Kind == "directory" && node.Name == "第一章" && node.SourceUri == doc.ResourceUri);
        Assert.Equal(layout.Files.Where(entry => entry.Path.StartsWith("第一章/")).Min(entry => entry.Order), chapterDirectory.SectionOrder);
        var index = (await resources.ReadAsync(doc.ResourceUri!, default)).ParsedIndex!;
        Assert.True(index.IsNavigation); Assert.False(index.CanDelete);
        foreach (var node in resources.Tree().Where(n => n.IsNavigation))
        {
            var read = await resources.ReadAsync(node.Uri, default);
            Assert.DoesNotContain("](./", read.SourceText);
            foreach (Match link in Regex.Matches(read.SourceText!, @"\]\((viking://[^)]+)\)"))
                Assert.Equal(doc.ResourceUri, (await resources.ReadAsync(link.Groups[1].Value, default)).Node.SourceUri);
            Assert.Throws<ArgumentException>(() => resources.Parse(node.Uri));
        }
        var path = resources.File(index.Uri, row.Id).Path;
        Assert.Contains("](./", File.ReadAllText(path));
        File.WriteAllText(path, "tampered index");
        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.ReadAsync(index.Uri, default));
        Assert.Equal(source, ingestion.GetContent(kb, doc).ParsedContent);
        Assert.Equal(source, File.ReadAllText(doc.StoragePath)); Assert.Empty(api.Inputs);
    }

    [Fact]
    public async Task PreviousLayoutVersionRemainsReadableWithoutNewIndexMetadata()
    {
        using var db = Database(); using var files = new SemanticFiles(); var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/legacy.md"); doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), new(db));
        var result = await ingestion.ParseResourceAsync(kb, doc, new(), default);
        var row = db.Queryable<AiKnowledgeParsedDocument>().InSingle(result.ParsedDocumentId);
        var store = new KnowledgeParsedDocumentStore(files.Paths); var layout = store.ReadManifest(kb, row)!;
        var previous = layout with { Version = "markdown-layout-1", Navigation = null };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(row.ContentPath!)!, "layout.json"), JsonSerializer.Serialize(previous));
        row.ParserVersion = "markdown-layout-1";
        var read = store.ReadManifest(kb, row)!;
        Assert.Null(read.Navigation); Assert.Equal(layout.Files, read.Files);
        Assert.Contains("每周三发布", File.ReadAllText(store.SectionPath(kb, row, read.Files[0])));
    }
}
