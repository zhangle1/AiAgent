using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiAgent.Backend.Services.Parsing;

namespace AiAgent.Backend.Tests;

public sealed partial class DocumentConversionTests
{
    private sealed record BundleFixture(string Version, string ContentHash, List<MarkdownBundleSection> Files, List<MarkdownBundleNavigation> Navigation);

    [Theory]
    [InlineData(".txt")]
    [InlineData(".docx")]
    [InlineData(".pdf")]
    public async Task PdfWordAndTextWriteCompleteHierarchicalBundlesWithWorkingLocalLinks(string extension)
    {
        using var files = new Files();
        var text = "# Introduction\n\n" + new string('a', 110) + "\n\n# Installation\n\nOverview\n\n## Linux\n\n" + new string('b', 120) + "\n\n## Windows\n\n" + new string('c', 120);
        var word = Word();
        var bytes = extension == ".pdf" ? CSharpPdfParsingTests.LayoutPdf() : extension == ".docx" ? word : Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(files.Source(extension), bytes);
        var result = await files.Service.ConvertAsync(new() { FilePath = files.Source(extension), SourceName = "原始手册" + extension, OutputDir = files.Output,
            Parsing = new() { MaxSectionChars = 140, MaxSectionTokens = 80, MinSectionTokens = 10 } });
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.True(File.Exists(result.IndexPath)); Assert.True(File.Exists(result.LayoutPath));
        var layout = JsonSerializer.Deserialize<BundleFixture>(File.ReadAllText(result.LayoutPath!))!;
        Assert.Equal(MarkdownDocumentPlanner.Version, layout.Version);
        Assert.Equal(result.SectionCount, layout.Files.Count);
        Assert.Equal(MarkdownBundleWriter.Hash(File.ReadAllText(result.MarkdownPath!)), layout.ContentHash);
        var next = 0;
        foreach (var entry in layout.Files)
        {
            Assert.Equal(next, entry.SourceStart); next = entry.SourceEnd;
            var target = Path.Combine(files.Output, "sections", entry.Path);
            var section = File.ReadAllText(target); Assert.Equal(entry.Hash, MarkdownBundleWriter.Hash(section));
            foreach (Match match in Regex.Matches(section, @"!\[[^\]]*\]\(([^)]+\.png)\)"))
            {
                var image = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, match.Groups[1].Value));
                Assert.StartsWith(Path.Combine(files.Output, "assets"), image);
                Assert.True(File.Exists(image));
            }
        }
        Assert.Equal(File.ReadAllText(result.MarkdownPath!).Length, next);
        foreach (var index in layout.Navigation)
        {
            var target = Path.Combine(files.Output, "sections", index.Path);
            var content = File.ReadAllText(target); Assert.Equal(index.Hash, MarkdownBundleWriter.Hash(content));
            foreach (Match link in Regex.Matches(content, @"\[[^\]]*\]\(([^)]+)\)"))
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, Uri.UnescapeDataString(link.Groups[1].Value)))));
        }
        if (extension == ".txt")
        {
            Assert.True(layout.Files.Count >= 3); Assert.True(layout.Navigation.Count > 1);
            Assert.Contains(layout.Files, f => f.Path.StartsWith("Installation/", StringComparison.Ordinal));
            Assert.Equal(text, string.Concat(layout.Files.Select(f => File.ReadAllText(Path.Combine(files.Output, "sections", f.Path))[f.RepeatedHeaderCharacters..])));
        }
        Assert.Equal(bytes, File.ReadAllBytes(files.Source(extension)));
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("gb18030")]
    public async Task TextDecodesChineseEncodingsWithoutReplacingSourceOrInventingHeadings(string encodingName)
    {
        using var files = new Files(); Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(encodingName);
        const string source = "第一章 原始标题\r\n\r\n中文内容，保持原文。\r\n# 已有 Markdown 标题\r\n第二段。";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray();
        var result = await files.Convert(".txt", bytes);
        Assert.True(result.Ok, result.ErrorMessage); Assert.Equal(source, File.ReadAllText(result.MarkdownPath!));
        Assert.Equal(bytes, File.ReadAllBytes(files.Source(".txt")));
        if (encodingName == "gb18030") Assert.Contains(result.Warnings, w => w.Contains("GB18030"));
        Assert.Equal(encoding.WebName, result.Details["source_encoding"]);
    }

    [Fact]
    public async Task WordRetainsStylesNumberedListsSafeLinksAndContentControlTables()
    {
        using var files = new Files();
        var source = Package(new() {
            ["word/document.xml"] = Encoding.UTF8.GetBytes("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p><w:pPr><w:pStyle w:val="CustomTitle"/></w:pPr><w:r><w:t>Styled heading</w:t></w:r></w:p><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r><w:r><w:t> and </w:t></w:r><w:r><w:rPr><w:i/><w:strike/></w:rPr><w:t>Italic strike</w:t></w:r><w:hyperlink r:id="safe"><w:r><w:t>Official</w:t></w:r></w:hyperlink><w:hyperlink r:id="unsafe"><w:r><w:t>Unsafe label</w:t></w:r></w:hyperlink></w:p><w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="9"/></w:numPr></w:pPr><w:r><w:t>Ordered item</w:t></w:r></w:p><w:sdt><w:sdtContent><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table data</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:sdtContent></w:sdt><w:p><w:r><w:t># literal text</w:t><w:footnoteReference w:id="1"/></w:r></w:p></w:body></w:document>"""),
            ["word/styles.xml"] = Encoding.UTF8.GetBytes("""<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:styleId="CustomTitle"><w:basedOn w:val="Heading2"/></w:style></w:styles>"""),
            ["word/numbering.xml"] = Encoding.UTF8.GetBytes("""<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:abstractNum w:abstractNumId="2"><w:lvl w:ilvl="1"><w:numFmt w:val="decimal"/></w:lvl></w:abstractNum><w:num w:numId="9"><w:abstractNumId w:val="2"/></w:num></w:numbering>"""),
            ["word/footnotes.xml"] = Encoding.UTF8.GetBytes("""<w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:footnote w:id="1"><w:p><w:r><w:t>Referenced note</w:t></w:r></w:p></w:footnote></w:footnotes>"""),
            ["word/_rels/document.xml.rels"] = Encoding.UTF8.GetBytes("""<Relationships><Relationship Id="safe" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" TargetMode="External" Target="https://example.invalid/docs(a)"/><Relationship Id="unsafe" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" TargetMode="External" Target="javascript:alert(1)"/></Relationships>""")
        });
        var result = await files.Convert(".docx", source); Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.Contains("## Styled heading", markdown); Assert.Contains("**Bold**", markdown); Assert.Contains("~~*Italic strike*~~", markdown);
        Assert.Contains("[Official](https://example.invalid/docs%28a%29)", markdown); Assert.DoesNotContain("javascript:", markdown);
        Assert.Contains("    1. Ordered item", markdown); Assert.Contains("| Table data |", markdown);
        Assert.Contains("\\# literal text[^footnotes-1]", markdown); Assert.Contains("[^footnotes-1]: Referenced note", markdown);
    }

    [Fact]
    public async Task PdfUsesSampledBodyFontForCoverAndDeduplicatesRepeatedCropsAcrossPages()
    {
        using var files = new Files();
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var cover = builder.AddPage(400, 600);
        cover.AddText("Cover heading", 28, new UglyToad.PdfPig.Core.PdfPoint(20, 500), font);
        for (var i = 0; i < 2; i++)
        {
            var page = builder.AddPage(400, 600);
            page.AddText("Repeated body text establishes the normal font size used throughout this document.", 10, new UglyToad.PdfPig.Core.PdfPoint(20, 500), font);
            page.AddPng(Png(), new UglyToad.PdfPig.Core.PdfRectangle(20, 100, 100, 160));
        }
        var result = await files.Convert(".pdf", builder.Build()); Assert.True(result.Ok, result.ErrorMessage);
        var markdown = File.ReadAllText(result.MarkdownPath!);
        Assert.Contains("# Cover heading", markdown);
        Assert.Single(result.Assets); Assert.Equal(1, result.Details["images_deduplicated"]);
        Assert.Equal(2, Regex.Matches(markdown, Regex.Escape(result.Assets[0].Path)).Count);
    }

    [Fact]
    public async Task TextOnlyModeAvoidsUnusedBundlesAndBadEncodingDoesNotPublishSuccess()
    {
        using var files = new Files(); File.WriteAllText(files.Source(".txt"), "# 正文\n\n原文");
        var result = await files.Service.ConvertAsync(new() { FilePath = files.Source(".txt"), OutputDir = files.Output, WriteBundle = false });
        Assert.True(result.Ok, result.ErrorMessage); Assert.Null(result.LayoutPath); Assert.False(Directory.Exists(Path.Combine(files.Output, "sections")));
        using var bad = new Files();
        var failure = await bad.Convert(".txt", new byte[] { 0xff, 0xff, 0x00, 0x01 });
        Assert.False(failure.Ok); Assert.False(File.Exists(Path.Combine(bad.Output, "document.md")));
    }
}
