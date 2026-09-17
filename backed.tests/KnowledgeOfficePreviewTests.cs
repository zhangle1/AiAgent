using System.IO;
using System.IO.Compression;
using AiAgent.Backend.Services.Knowledge;
using ClosedXML.Excel;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeOfficePreviewTests
{
    [Fact]
    public void WordPreviewPreservesParagraphsAndTablesAndEscapesMarkup()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                writer.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>&lt;script&gt;unsafe&lt;/script&gt;</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table evidence</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>""");
            var preview = KnowledgeOfficePreviewService.Render(path, ".docx", default);
            var section = Assert.Single(preview.Sections);
            Assert.Contains("&lt;script&gt;", section.Html);
            Assert.DoesNotContain("<script>", section.Html);
            Assert.Contains("<td>Table evidence</td>", section.Html);
            Assert.False(preview.Truncated);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExcelPreviewHasSheetsAndBoundsSparseRanges()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var first = workbook.AddWorksheet("First");
                first.Cell(1, 1).Value = "<img src=x>";
                first.Cell(5000, 80).Value = "Outside preview";
                workbook.AddWorksheet("Second").Cell(1, 1).Value = "Second sheet";
                workbook.SaveAs(path);
            }
            var preview = KnowledgeOfficePreviewService.Render(path, ".xlsx", default);
            Assert.Equal(2, preview.Sections.Count);
            Assert.True(preview.Truncated);
            Assert.Contains("&lt;img src=x&gt;", preview.Sections[0].Html);
            Assert.DoesNotContain("Outside preview", preview.Sections[0].Html);
            Assert.Contains("Second sheet", preview.Sections[1].Html);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => KnowledgeOfficePreviewService.Render(path, ".xlsx", cancelled.Token));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacyPreviewExplainsMissingConverter()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => KnowledgeOfficePreviewService.ConvertLegacyAsync("fixture.doc", "aiagent-missing-office-converter", default));
        Assert.Contains("LibreOffice", ex.Message);
    }
}
