using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Services.Parsing;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;

namespace AiAgent.Backend.Tests;

public sealed class CSharpPdfParsingTests
{
    [Fact]
    public async Task PreflightLoadsManagedAndNativeEnginesWithoutPython()
    {
        using var fixture = new Files();
        var result = await fixture.Parser.PreflightAsync();
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(false, result.Details["python_required"]);
        Assert.Equal(true, result.Details["native_renderer_ready"]);
        Assert.DoesNotContain("python_path", result.Details.Keys);
    }

    [Fact]
    public async Task RealPdfExtractsBookmarksTablesRasterVectorAndScannedPageWithoutChangingSource()
    {
        using var fixture = new Files();
        var original = LayoutPdf();
        File.WriteAllBytes(fixture.Source, original);
        var result = await fixture.Parse();
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal("pdfpig-pdfium-layout", result.Engine);
        Assert.Equal(2, result.PageCount);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.Contains("# Manufacturing", markdown);
        Assert.Contains("## Details", markdown);
        Assert.Contains("Body text is extracted directly", markdown);
        Assert.Contains("| Name | Value |", markdown);
        Assert.Contains("| A | 42 |", markdown);
        Assert.Contains("尚未 OCR", markdown);
        Assert.Contains(result.Warnings, x => x.Contains("1 页"));
        Assert.Contains(result.Assets, x => x.Kind == "image" && x.Page == 1);
        Assert.Contains(result.Assets, x => x.Kind == "vector" && x.Page == 1);
        Assert.Contains(result.Assets, x => x.Kind == "page" && x.Page == 2);
        foreach (var asset in result.Assets)
        {
            Assert.StartsWith("assets/", asset.Path);
            Assert.Contains($"]({asset.Path})", markdown);
            var bytes = File.ReadAllBytes(Path.Combine(fixture.Output, asset.Path));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), asset.Sha256);
            using var image = SKBitmap.Decode(bytes);
            Assert.InRange(image.Width, 1, 4096);
            Assert.InRange(image.Height, 1, 4096);
            Assert.True((long)image.Width * image.Height <= 4_000_000);
        }
        Assert.Equal(original, File.ReadAllBytes(fixture.Source));
        Assert.Contains("Body text is extracted directly", File.ReadAllText(result.TextPath!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedPdfResumesFromSavedPagesAndRepairsMissingImages(bool removeImage)
    {
        using var fixture = new Files();
        using var cancellation = new CancellationTokenSource();
        const int total = 23;
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 1; i <= total; i++)
        {
            var page = builder.AddPage(200, 240);
            page.AddText($"Synthetic page {i}", 10, new PdfPoint(20, 200), font);
            if (i is 1 or total) page.AddPng(RedPng(), new PdfRectangle(20, 20, 120, 120));
        }
        File.WriteAllBytes(fixture.Source, builder.Build());
        var checkpoints = Path.Combine(fixture.Root, "checkpoints");
        var before = new List<PdfParseProgress>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Parser.ParsePdfAsync(new()
        {
            FilePath = fixture.Source, OutputDir = fixture.Output, PdfCheckpointDirectory = checkpoints,
            PdfProgress = state =>
            {
                before.Add(state);
                if (state.Page == 21) cancellation.Cancel();
            }
        }, cancellation.Token));
        Assert.Equal(20, before[^1].Completed);
        Assert.Equal(2, before[^1].Batch);
        Assert.Equal(20, Directory.GetFiles(checkpoints, "page-*.json", SearchOption.AllDirectories).Length);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "document.md")));
        if (removeImage) File.Delete(Assert.Single(Directory.GetFiles(checkpoints, "*.png", SearchOption.AllDirectories)));

        var after = new List<PdfParseProgress>();
        var resumed = await fixture.Parser.ParsePdfAsync(new()
        {
            FilePath = fixture.Source, OutputDir = Path.Combine(fixture.Root, "resumed"),
            PdfCheckpointDirectory = checkpoints, PdfProgress = after.Add
        });
        Assert.True(resumed.Ok, resumed.ErrorMessage);
        Assert.Equal(total, after[^1].Completed);
        Assert.Equal(removeImage ? 19 : 20, after[^1].Reused);
        Assert.Contains(after, state => state.Page == 21 && state.Completed == 20);
        var markdown = File.ReadAllText(resumed.MarkdownPath!);
        Assert.Equal(Enumerable.Range(1, total).Select(i => $"<!-- page: {i} -->"),
            markdown.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("<!-- page:")));
        var image = Assert.Single(resumed.Assets);
        Assert.Contains($"![第 1 页图 1]({image.Path})", markdown);
        Assert.Contains($"![第 {total} 页图 1]({image.Path})", markdown);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "resumed", image.Path)));
    }

    [Fact]
    public async Task FailedPageLeavesSuccessfulPagesOnBothSidesAvailableForRetry()
    {
        using var fixture = new Files();
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(200, 240).AddText("Page one", 10, new PdfPoint(20, 200), font);
        var badPage = builder.AddPage(200, 240);
        for (var i = 0; i < 20001; i++) badPage.DrawLine(new PdfPoint(10, 10), new PdfPoint(40, 60));
        builder.AddPage(200, 240).AddText("Page three", 10, new PdfPoint(20, 200), font);
        File.WriteAllBytes(fixture.Source, builder.Build());
        var checkpoints = Path.Combine(fixture.Root, "checkpoints");
        var result = await fixture.Parser.ParsePdfAsync(new()
        {
            FilePath = fixture.Source, OutputDir = fixture.Output, PdfCheckpointDirectory = checkpoints
        });
        Assert.False(result.Ok);
        Assert.Contains("已保留 2/3 页", result.ErrorMessage);
        Assert.Contains("第 2 页未完成", result.ErrorMessage);
        Assert.Equal(new[] { "page-0001.json", "page-0003.json" },
            Directory.GetFiles(checkpoints, "page-*.json", SearchOption.AllDirectories).Select(Path.GetFileName).Order());
        Assert.False(File.Exists(Path.Combine(fixture.Output, "document.md")));

        var after = new List<PdfParseProgress>();
        result = await fixture.Parser.ParsePdfAsync(new()
        {
            FilePath = fixture.Source, OutputDir = Path.Combine(fixture.Root, "retry"),
            PdfCheckpointDirectory = checkpoints, PdfProgress = after.Add
        });
        Assert.False(result.Ok);
        Assert.Equal(2, after[^1].Reused);
        Assert.Equal(1, after[^1].Failed);
    }

    [Fact]
    public async Task FontSizeFallbackAndTextOnlyAttachmentModeNeedNoRendererOutput()
    {
        using var fixture = new Files();
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(400, 500);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Large heading", 24, new PdfPoint(20, 450), font);
        page.AddText("The regular body contains enough words to identify the body font.", 10, new PdfPoint(20, 400), font);
        page.AddPng(RedPng(), new PdfRectangle(30, 100, 130, 200));
        File.WriteAllBytes(fixture.Source, builder.Build());
        var result = await fixture.Parse(false);
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Contains("# Large heading", File.ReadAllText(result.MarkdownPath!));
        Assert.Contains("regular body", File.ReadAllText(result.TextPath!));
        Assert.Empty(result.Assets);
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "assets")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScanUsesOneWholeTitleAndExcludesPageFurnitureFromHeadings(bool imageBacked)
    {
        using var fixture = new Files();
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var chapter = builder.AddPage(400, 600);
        chapter.AddPng(RedPng(), new PdfRectangle(0, 0, 400, 600));
        if (!imageBacked) chapter.SetTextRenderingMode(TextRenderingMode.Neither);
        chapter.AddText("12", 21, new PdfPoint(350, 580), font);
        chapter.AddText("The role of", 31, new PdfPoint(34, 512), font);
        chapter.AddText("the warehouse", 32, new PdfPoint(34, 475), font);
        chapter.AddText("Warehouses consolidate goods to meet demand and improve service.", 10, new PdfPoint(36, 429), font);
        chapter.AddText("Warehousing operations include storage, picking and receiving.", 10, new PdfPoint(36, 409), font);
        chapter.AddText("FIGURE 1.1", 19, new PdfPoint(44, 285), font);
        var following = builder.AddPage(400, 600);
        following.AddPng(RedPng(), new PdfRectangle(0, 0, 400, 600));
        if (!imageBacked) following.SetTextRenderingMode(TextRenderingMode.Neither);
        following.AddText("The role of the warehouse", 17, new PdfPoint(34, 577), font);
        following.AddText("standardization reduces costs;", 22, new PdfPoint(36, 526), font);
        following.AddText("The next page continues the chapter body.", 10, new PdfPoint(36, 490), font);
        following.AddText("# OCR markup is ordinary printed text;", 10, new PdfPoint(36, 469), font);
        File.WriteAllBytes(fixture.Source, builder.Build());
        var result = await fixture.Parse(imageBacked);
        Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        var headings = markdown.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith('#'));
        Assert.Equal(new[] { "# The role of the warehouse" }, headings);
        Assert.Contains("Warehouses consolidate goods", markdown);
        Assert.Contains("The next page continues the chapter body", markdown);
        Assert.Contains(@"\# OCR markup is ordinary printed text;", markdown);
        Assert.Contains("FIGURE 1.1", markdown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task CropBoxAndRotationExportActualColoredRegion(int rotation)
    {
        using var fixture = new Files();
        File.WriteAllBytes(fixture.Source, RawPdf($"/MediaBox [0 0 600 800] /CropBox [50 100 450 600] /Rotate {rotation}",
            "BT /F1 12 Tf 80 520 Td (Synthetic label) Tj ET\n1 0 0 rg 100 200 120 60 re f\n"));
        var result = await fixture.Parse();
        Assert.True(result.Ok, result.ErrorMessage);
        var asset = Assert.Single(result.Assets, a => a.Kind == "vector");
        using var image = SKBitmap.Decode(Path.Combine(fixture.Output, asset.Path));
        var center = image.GetPixel(image.Width / 2, image.Height / 2);
        Assert.True(center.Red > 240 && center.Green < 20 && center.Blue < 20, $"Incorrect crop center {center}");
        if (rotation % 180 == 0) Assert.True(image.Width > image.Height * 1.7);
        else Assert.True(image.Height > image.Width * 1.7);
    }

    [Fact]
    public async Task ExternalKnowledgeRootWorksAndSiblingInputAndOutputAreRejected()
    {
        using var fixture = new Files();
        var knowledge = Path.Combine(fixture.Root, "external", "storage", "knowledge_bases");
        Directory.CreateDirectory(knowledge);
        var source = Path.Combine(knowledge, "book.pdf");
        File.WriteAllBytes(source, LayoutPdf());
        var parser = fixture.CreateParser(new Dictionary<string, string?> {
            ["DocumentParsing:AllowedRoots:0"] = Path.Combine(fixture.Root, "other"),
            ["KnowledgeStorage:RootPath"] = Path.Combine(fixture.Root, "external")
        });
        var valid = await parser.ParsePdfAsync(new() { FilePath = source, OutputDir = Path.Combine(knowledge, "parsed") });
        Assert.True(valid.Ok, valid.ErrorMessage);
        var rejected = await parser.ParsePdfAsync(new() { FilePath = source, OutputDir = knowledge + "-sibling" });
        Assert.Equal("path_not_allowed", rejected.ErrorCode);
        Assert.False(Directory.Exists(knowledge + "-sibling"));
        File.WriteAllBytes(fixture.Source, LayoutPdf());
        rejected = await parser.ParsePdfAsync(new() { FilePath = fixture.Source, OutputDir = Path.Combine(knowledge, "next") });
        Assert.Equal("path_not_allowed", rejected.ErrorCode);
    }

    [Fact]
    public async Task LargePageIsRenderedWithinPixelBudget()
    {
        using var fixture = new Files();
        File.WriteAllBytes(fixture.Source, RawPdf("/MediaBox [0 0 20000 30000]", "1 0 0 rg 0 0 20000 30000 re f\n"));
        var result = await fixture.Parse();
        Assert.True(result.Ok, result.ErrorMessage);
        var asset = Assert.Single(result.Assets);
        using var image = SKBitmap.Decode(Path.Combine(fixture.Output, asset.Path));
        Assert.InRange(image.Width, 1, 4096);
        Assert.InRange(image.Height, 1, 4096);
        Assert.InRange((long)image.Width * image.Height, 1, 4_000_000);
        Assert.True(image.GetPixel(image.Width / 2, image.Height / 2).Red > 240);
    }

    [Fact]
    public async Task PageCountLimitRejectsBeforeCreatingOutput()
    {
        using var fixture = new Files();
        var builder = new PdfDocumentBuilder();
        for (var index = 0; index < 2001; index++) builder.AddPage(72, 72);
        File.WriteAllBytes(fixture.Source, builder.Build());
        var result = await fixture.Parse();
        Assert.False(result.Ok);
        Assert.Contains("2000", result.ErrorMessage);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task LegacyAllowedRootsStillAuthorizeCSharpParsing()
    {
        using var fixture = new Files();
        File.WriteAllBytes(fixture.Source, LayoutPdf());
        var parser = fixture.CreateParser(new Dictionary<string, string?> { ["PythonWorkers:AllowedRoots:0"] = fixture.Root });
        var result = await parser.ParsePdfAsync(new() { FilePath = fixture.Source, OutputDir = fixture.Output, WriteImages = false });
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Contains("Body text", File.ReadAllText(result.TextPath!));
    }

    [Fact]
    public async Task RejectsInvalidPdfAndNeverOverwritesExistingResult()
    {
        using var fixture = new Files();
        File.WriteAllText(fixture.Source, "invalid PDF");
        var result = await fixture.Parse();
        Assert.False(result.Ok);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "document.md")));
        File.WriteAllBytes(fixture.Source, LayoutPdf());
        Directory.CreateDirectory(fixture.Output);
        var existing = Path.Combine(fixture.Output, "document.md");
        File.WriteAllText(existing, "previous published bytes");
        result = await fixture.Parse();
        Assert.False(result.Ok);
        Assert.Equal("previous published bytes", File.ReadAllText(existing));
    }

    [Fact]
    public async Task ChatUploadExtractsPdfWithRealCSharpService()
    {
        using var fixture = new Files();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ChatAttachments:RootPath"] = fixture.Root, ["DocumentParsing:AllowedRoots:0"] = fixture.Root,
            ["PythonWorkers:Parsing:PythonPath"] = Path.Combine(fixture.Root, "missing-python.exe")
        }).Build();
        var parser = new DocumentParsingService(config, NullLogger<DocumentParsingService>.Instance);
        var attachments = new ChatFileAttachmentService(config, parser, NullLogger<ChatFileAttachmentService>.Instance);
        var user = new AuthenticatedUser("pdf-owner", "fixture");
        using var stream = new MemoryStream(LayoutPdf());
        var file = new FormFile(stream, 0, stream.Length, "file", "fixture.pdf") { Headers = new HeaderDictionary() };
        var upload = await attachments.SaveAsync(user, file, default);
        var resolved = await attachments.ResolveLocalAttachmentsAsync(user, null, [upload.Id], default);
        var context = await attachments.ExtractContextAsync(resolved, default);
        Assert.Contains("Body text is extracted directly", context);
        Assert.Contains("42", context);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Root, "files"), "*.png", SearchOption.AllDirectories));
        await Assert.ThrowsAsync<InvalidOperationException>(() => attachments.ResolveLocalAttachmentsAsync(new AuthenticatedUser("other", "other"), null, [upload.Id], default));
    }

    [Fact]
    public async Task CancellationPropagatesWithoutWritingOutputs()
    {
        using var fixture = new Files();
        File.WriteAllBytes(fixture.Source, LayoutPdf());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Parse(cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(fixture.Output));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Parser.PreflightAsync(cancellation.Token));
    }

    internal static byte[] LayoutPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(400, 600);
        page.AddText("Manufacturing", 22, new PdfPoint(20, 560), font);
        page.AddText("Body text is extracted directly and remains complete in Markdown.", 10, new PdfPoint(20, 525), font);
        page.AddText("Details", 16, new PdfPoint(20, 480), font);
        foreach (var x in new[] { 20, 150, 280 }) page.DrawLine(new PdfPoint(x, 340), new PdfPoint(x, 420));
        foreach (var y in new[] { 340, 380, 420 }) page.DrawLine(new PdfPoint(20, y), new PdfPoint(280, y));
        page.AddText("Name", 10, new PdfPoint(30, 400), font);
        page.AddText("Value", 10, new PdfPoint(160, 400), font);
        page.AddText("A", 10, new PdfPoint(30, 360), font);
        page.AddText("42", 10, new PdfPoint(160, 360), font);
        page.AddPng(RedPng(), new PdfRectangle(20, 180, 120, 260));
        page.DrawRectangle(new PdfPoint(200, 180), 80, 70);
        page.DrawLine(new PdfPoint(200, 180), new PdfPoint(280, 250));
        var scan = builder.AddPage(400, 600);
        scan.AddPng(RedPng(), new PdfRectangle(0, 0, 400, 600));
        builder.Bookmarks = new Bookmarks(new BookmarkNode[] {
            new DocumentBookmarkNode("Manufacturing", 0, new ExplicitDestination(1, ExplicitDestinationType.FitPage, ExplicitDestinationCoordinates.Empty), new BookmarkNode[] {
                new DocumentBookmarkNode("Details", 1, new ExplicitDestination(1, ExplicitDestinationType.FitPage, ExplicitDestinationCoordinates.Empty), Array.Empty<BookmarkNode>())
            })
        });
        return builder.Build();
    }

    private static byte[] RedPng()
    {
        using var bitmap = new SKBitmap(32, 24);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static byte[] RawPdf(string pageOptions, string content)
    {
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R {pageOptions} /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream"
        };
        var output = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(output.Length); output.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = output.Length;
        output.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) output.Append($"{offset:D10} 00000 n \n");
        output.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(output.ToString());
    }

    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "csharp-pdf-tests-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source.pdf");
        public string Output => Path.Combine(Root, "output");
        public DocumentParsingService Parser { get; }
        public Files() { Directory.CreateDirectory(Root); Parser = CreateParser(); }
        public DocumentParsingService CreateParser(Dictionary<string, string?>? overrides = null)
        {
            var values = overrides ?? new Dictionary<string, string?> { ["DocumentParsing:AllowedRoots:0"] = Root };
            values["PythonWorkers:Parsing:PythonPath"] = Path.Combine(Root, "python-does-not-exist.exe");
            return new DocumentParsingService(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), NullLogger<DocumentParsingService>.Instance);
        }
        public Task<DocumentParseResult> Parse(bool writeImages = true, CancellationToken cancellationToken = default) =>
            Parser.ParsePdfAsync(new() { FilePath = Source, OutputDir = Output, WriteImages = writeImages }, cancellationToken);
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("csharp-pdf-tests-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
