using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Services.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using AiAgent.Backend.Services.CodeRepository;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Entities.CodeRepository;
using Microsoft.AspNetCore.Http;

namespace AiAgent.Backend.Tests;

public sealed partial class DocumentConversionTests
{
    [Theory]
    [InlineData(".md", "# 原始标题\n\n保留正文", "# 原始标题")]
    [InlineData(".txt", "保留第一段\n\n第二段", "第二段")]
    [InlineData(".json", "{\"large\":9007199254740993,\"duplicate\":1,\"duplicate\":2}", "9007199254740993")]
    [InlineData(".csv", "name,value\n\"A,B\",\"a\"\"b\"\n\"line\nbreak\",", "line<br>break")]
    [InlineData(".tsv", "first\t\tthird\nx\t\ty", "first |  | third")]
    [InlineData(".html", "<title>Title</title><h1>Heading</h1><p>hello &amp; world</p><script>SECRET</script>", "# Heading")]
    [InlineData(".rtf", "{\\rtf1\\ansi Hello \\b bold\\b0\\par Next paragraph}", "bold")]
    public async Task ConvertsFormatsWithoutPythonAndDoesNotChangeSource(string extension, string source, string expected)
    {
        using var files = new Files();
        var result = await files.Convert(extension, Encoding.UTF8.GetBytes(source));
        Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.Contains(expected, markdown); Assert.DoesNotContain("SECRET", markdown);
        Assert.Equal(source, File.ReadAllText(files.Source(extension)));
        Assert.Equal(markdown, File.ReadAllText(result.TextPath!));
        Assert.Equal(false, result.Details["python_required"]);
        if (extension == ".csv") { Assert.Contains("| name | value |", markdown); Assert.Contains("A,B", markdown); Assert.Contains("a\"b", markdown); }
        if (extension is ".md" or ".json" or ".txt") Assert.Equal(source, markdown);
    }

    [Fact]
    public async Task WordExportsReferencedImagesInOrderAndIgnoresUnusedMedia()
    {
        using var files = new Files(); var original = Word();
        var result = await files.Convert(".docx", original);
        Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.Contains("# Word heading", markdown);
        var asset = Assert.Single(result.Assets);
        Assert.True(markdown.IndexOf("Before", StringComparison.Ordinal) < markdown.IndexOf(asset.Path, StringComparison.Ordinal));
        Assert.True(markdown.IndexOf(asset.Path, StringComparison.Ordinal) < markdown.IndexOf("After", StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(Path.Combine(files.Output, asset.Path));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), asset.Sha256);
        using var bitmap = SKBitmap.Decode(bytes); Assert.Equal(32, bitmap.Width);
        Assert.Equal(original, File.ReadAllBytes(files.Source(".docx")));
    }

    [Fact]
    public async Task SlidesKeepPresentationOrderTablesNotesAndImages()
    {
        using var files = new Files();
        var result = await files.Convert(".pptx", Package(new() {
            ["ppt/presentation.xml"] = Encoding.UTF8.GetBytes("""<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:sldIdLst><p:sldId r:id="b"/><p:sldId r:id="a"/></p:sldIdLst></p:presentation>"""),
            ["ppt/_rels/presentation.xml.rels"] = Encoding.UTF8.GetBytes("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="a" Target="slides/slide2.xml"/><Relationship Id="b" Target="slides/slide10.xml"/></Relationships>"""),
            ["ppt/slides/slide10.xml"] = Encoding.UTF8.GetBytes("""<p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>First in order</a:t></a:r></a:p></p:txBody></p:sp><p:pic><p:blipFill><a:blip r:embed="image"/></p:blipFill></p:pic><p:graphicFrame><a:tbl><a:tr><a:tc><a:p><a:r><a:t>Table cell</a:t></a:r></a:p></a:tc></a:tr></a:tbl></p:graphicFrame></p:spTree></p:cSld></p:sld>"""),
            ["ppt/slides/slide2.xml"] = Encoding.UTF8.GetBytes("""<p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Second in order</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>"""),
            ["ppt/slides/_rels/slide10.xml.rels"] = Encoding.UTF8.GetBytes("""<Relationships><Relationship Id="image" Target="../media/pic.png" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"/><Relationship Id="notes" Target="../notesSlides/note.xml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide"/></Relationships>"""),
            ["ppt/notesSlides/note.xml"] = Encoding.UTF8.GetBytes("""<p:notes xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><p:sp><p:nvSpPr><p:nvPr><p:ph type="body"/></p:nvPr></p:nvSpPr><p:txBody><a:p><a:r><a:t>Speaker note</a:t></a:r></a:p></p:txBody></p:sp></p:notes>"""),
            ["ppt/media/pic.png"] = Png()
        }));
        Assert.True(result.Ok, result.ErrorMessage); var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.True(markdown.IndexOf("First in order", StringComparison.Ordinal) < markdown.IndexOf("Second in order", StringComparison.Ordinal));
        Assert.Contains("Table cell", markdown); Assert.Contains("Speaker note", markdown);
        Assert.Contains(Assert.Single(result.Assets).Path, markdown);
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".epub")]
    [InlineData(".pdf")]
    [InlineData(".rtf")]
    public async Task ProjectImportUsesSharedConverterAndPersistsOriginalWithoutTemporaryImageLinks(string extension)
    {
        using var files = new Files(); var projectRoot = Path.Combine(files.Root, "project"); Directory.CreateDirectory(projectRoot);
        using var db = new SqlSugar.SqlSugarScope(new SqlSugar.ConnectionConfig {
            DbType = SqlSugar.DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = SqlSugar.InitKeyType.Attribute,
            ConfigureExternalServices = new SqlSugar.ConfigureExternalServices { EntityService = (_, column) => { if (column.IsIdentity) column.DataType = "INTEGER"; if (column.DataType == "nvarchar(max)") column.DataType = "text"; } }
        });
        db.CodeFirst.InitTables<AiCodeProject, AiCodeRepository, AiProjectMarkdownDocument>();
        db.Insertable(new AiCodeProject { Id = 1, Name = "fixture", RootPath = projectRoot }).ExecuteCommand();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["DataPath"] = Path.Combine(files.Root, "data"), ["CodeRepository:AllowedRoots:0"] = projectRoot
        }).Build();
        var manager = new CodeRepositoryManager(db, config, NullLogger<CodeRepositoryManager>.Instance, new DocumentParsingService(config, NullLogger<DocumentParsingService>.Instance));
        var bytes = extension == ".pdf" ? CSharpPdfParsingTests.LayoutPdf() : extension == ".epub" ? Epub() : extension == ".rtf" ? Encoding.ASCII.GetBytes("{\\rtf1 RTF body}") : Word();
        using var input = new MemoryStream(bytes);
        var file = new FormFile(input, 0, bytes.Length, "file", "fixture" + extension) { Headers = new HeaderDictionary() };
        var imported = await manager.ImportProjectDocumentsAsync(1, new AuthenticatedUser("owner", "fixture"), null, null, [file]);
        var item = Assert.Single(imported.Documents); Assert.Equal("imported", Assert.Single(imported.Items).Status);
        var preview = await manager.PreviewProjectDocumentAsync(1, item.RepositoryName, item.Path);
        Assert.DoesNotContain("assets/", preview.Content);
        Assert.Contains(extension == ".pdf" ? "Manufacturing" : extension == ".epub" ? "Second" : extension == ".rtf" ? "RTF body" : "Word heading", preview.Content);
        Assert.Equal(bytes, File.ReadAllBytes(manager.GetProjectDocumentFile(1, item.RepositoryName, item.Path).FilePath));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(files.Root, "data", "knowledge_bases", "conversion-workspaces", "projects")));
    }

    [Fact]
    public async Task WordTextOnlyDoesNotProduceDanglingImageLinks()
    {
        using var files = new Files(); var result = await files.Convert(".docx", Word(), false);
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Empty(result.Assets); Assert.DoesNotContain("assets/", File.ReadAllText(result.MarkdownPath!));
        Assert.Contains("Before", File.ReadAllText(result.MarkdownPath!));
    }

    [Fact]
    public async Task EpubFollowsSpineAndExportsPackageRelativeImages()
    {
        using var files = new Files(); var result = await files.Convert(".epub", Epub());
        Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.True(markdown.IndexOf("Second", StringComparison.Ordinal) < markdown.IndexOf("First", StringComparison.Ordinal));
        Assert.Contains(Assert.Single(result.Assets).Path, markdown);
    }

    [Fact]
    public async Task HtmlStripsActiveContentAndNeverReadsExternalOrLocalImages()
    {
        using var files = new Files();
        var result = await files.Convert(".html", Encoding.UTF8.GetBytes("<h1>Heading</h1><img src='https://invalid.invalid/private.png'><img src='file:///private.png'><a href='javascript:alert(1)'>link</a><iframe>BAD</iframe>"));
        Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.Contains("# Heading", markdown); Assert.Contains("link", markdown);
        Assert.DoesNotContain("javascript:", markdown); Assert.DoesNotContain("file:", markdown); Assert.DoesNotContain("BAD", markdown);
        Assert.Empty(result.Assets); Assert.NotEmpty(result.Warnings);
    }

    [Theory]
    [InlineData(".csv", "a,b\n\"unclosed")]
    [InlineData(".rtf", "plain text")]
    [InlineData(".docx", "not a zip")]
    [InlineData(".txt", "%PDF-1.7\nbinary PDF")]
    public async Task MalformedInputsFailWithoutPublishingMarkdown(string extension, string source)
    {
        using var files = new Files(); var result = await files.Convert(extension, Encoding.UTF8.GetBytes(source));
        Assert.False(result.Ok); Assert.False(File.Exists(Path.Combine(files.Output, "document.md")));
    }

    [Fact]
    public async Task PackageXmlCannotReadExternalEntities()
    {
        using var files = new Files(); var secret = Path.Combine(files.Root, "synthetic-secret.txt"); File.WriteAllText(secret, "SYNTHETIC_SECRET");
        var xml = $"<!DOCTYPE document [<!ENTITY external SYSTEM '{new Uri(secret).AbsoluteUri}'>]><document>&external;</document>";
        var result = await files.Convert(".docx", Package(new() { ["word/document.xml"] = Encoding.UTF8.GetBytes(xml) }));
        Assert.False(result.Ok); Assert.False(File.Exists(Path.Combine(files.Output, "document.md")));
    }

    [Fact]
    public async Task SparseWorkbookCannotExpandBillionsOfEmptyCells()
    {
        using var files = new Files(); using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet("Sparse"); sheet.Cell(1, 1).Value = "start"; sheet.Cell(1048576, 16384).Value = "end";
        using var bytes = new MemoryStream(); workbook.SaveAs(bytes);
        var result = await files.Convert(".xlsx", bytes.ToArray());
        Assert.False(result.Ok); Assert.Contains("200 万", result.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(files.Output, "document.md")));
    }

    [Fact]
    public async Task RejectsPackageTraversalAndOutOfRootOutput()
    {
        using var files = new Files();
        var result = await files.Convert(".epub", Package(new() { ["../outside.txt"] = Encoding.UTF8.GetBytes("bad") }));
        Assert.False(result.Ok);
        File.WriteAllText(files.Source(".md"), "# Test");
        result = await files.Service.ConvertAsync(new() { FilePath = files.Source(".md"), OutputDir = files.Root + "-sibling" });
        Assert.Equal("path_not_allowed", result.ErrorCode); Assert.False(Directory.Exists(files.Root + "-sibling"));
    }

    [Fact]
    public async Task CancelledConversionWritesNothingAndPdfKeepsItsEngineAndAssets()
    {
        using var files = new Files(); File.WriteAllText(files.Source(".md"), "# Heading");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.Service.ConvertAsync(new() { FilePath = files.Source(".md"), OutputDir = files.Output }, cancellation.Token));
        Assert.False(Directory.Exists(files.Output));
        var pdf = await files.Convert(".pdf", CSharpPdfParsingTests.LayoutPdf());
        Assert.True(pdf.Ok, pdf.ErrorMessage); Assert.Equal("pdfpig-pdfium-layout", pdf.Engine); Assert.Equal(3, pdf.Assets.Count);
        Assert.Contains("# Manufacturing", File.ReadAllText(pdf.MarkdownPath!));
    }

    internal static byte[] Word() => Package(new() {
        ["word/document.xml"] = Encoding.UTF8.GetBytes("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Word heading</w:t></w:r></w:p><w:p><w:r><w:t>Before</w:t><w:drawing><a:blip r:embed="img1"/></w:drawing><w:t>After</w:t></w:r></w:p></w:body></w:document>"""),
        ["word/_rels/document.xml.rels"] = Encoding.UTF8.GetBytes("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="img1" Target="media/image.png" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"/></Relationships>"""),
        ["word/media/image.png"] = Png(), ["word/media/unused.png"] = Png()
    });

    internal static byte[] Epub() => Package(new() {
        ["mimetype"] = Encoding.ASCII.GetBytes("application/epub+zip"),
        ["META-INF/container.xml"] = Encoding.UTF8.GetBytes("""<container><rootfiles><rootfile full-path="OEBPS/book.opf"/></rootfiles></container>"""),
        ["OEBPS/book.opf"] = Encoding.UTF8.GetBytes("""<package><manifest><item id="a" href="chapters/a.xhtml" media-type="application/xhtml+xml"/><item id="b" href="chapters/b.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="b"/><itemref idref="a"/></spine></package>"""),
        ["OEBPS/chapters/a.xhtml"] = Encoding.UTF8.GetBytes("<h1>First</h1><p>A text</p>"),
        ["OEBPS/chapters/b.xhtml"] = Encoding.UTF8.GetBytes("<h1>Second</h1><p>B text</p><img src='../images/pic.png'>"),
        ["OEBPS/images/pic.png"] = Png()
    });

    internal static byte[] Package(Dictionary<string, byte[]> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var entry in entries) { using var target = archive.CreateEntry(entry.Key).Open(); target.Write(entry.Value); }
        return buffer.ToArray();
    }
    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(32, 24); bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }

    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "document-conversion-tests-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Root, "out");
        internal DocumentConversionService Service { get; }
        internal string Source(string extension) => Path.Combine(Root, "source" + extension);
        internal Files()
        {
            Directory.CreateDirectory(Root);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DocumentParsing:AllowedRoots:0"] = Root }).Build();
            Service = new(config, new DocumentParsingService(config, NullLogger<DocumentParsingService>.Instance));
        }
        internal Task<DocumentParseResult> Convert(string extension, byte[] bytes, bool images = true)
        {
            File.WriteAllBytes(Source(extension), bytes);
            return Service.ConvertAsync(new() { FilePath = Source(extension), OutputDir = Output, WriteImages = images });
        }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("document-conversion-tests-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
}
