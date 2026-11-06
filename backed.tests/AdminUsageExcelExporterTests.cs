using System.IO;
using AiAgent.Backend.Dtos.Admin;
using AiAgent.Backend.Services.Admin;
using ClosedXML.Excel;

namespace AiAgent.Backend.Tests;

public sealed class AdminUsageExcelExporterTests
{
    [Fact]
    public void WorkbookPreservesDatesNumbersTextAndSeparateDataSets()
    {
        var report = new AdminUsageReportDto
        {
            From = new DateTime(2026, 11, 1), To = new DateTime(2026, 11, 4),
            TurnCount = 3, TotalTokens = 120, Metrics = new() { SessionCount = 1, ActiveDays = 2, DailyTurns = 1 },
            Buckets = [new() { Label = "11-01", TurnCount = 3, TotalTokens = 120 }, new() { Label = "11-02" }],
            Users = [new() { UserId = "001", Username = "=1+1", TurnCount = 3, PromptTokens = 80, CompletionTokens = 40, TotalTokens = 120 }],
            UserProjects = [new() { UserId = "001", Username = "=1+1", ProjectId = null, ProjectName = "未关联", TurnCount = 1, TotalTokens = 40 },
                new() { UserId = "002", ProjectName = "未关联", TurnCount = 2, TotalTokens = 80 }]
        };
        using var stream = new MemoryStream(AdminUsageExcelExporter.Export(report, "001", 0));
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(new[] { "统计概览", "时间趋势", "用户汇总", "项目对比", "用户×项目明细" }, workbook.Worksheets.Select(x => x.Name));
        Assert.Equal("2026-11-03", workbook.Worksheet(1).Cell(3, 2).GetString());
        Assert.Equal("001", workbook.Worksheet(1).Cell(5, 2).GetString());
        Assert.Equal("0", workbook.Worksheet(1).Cell(6, 2).GetString());
        Assert.Equal(1, workbook.Worksheet(1).Cell(7, 2).GetDouble());
        Assert.Equal(0, workbook.Worksheet(2).Cell(3, 3).GetDouble());
        Assert.Equal("001", workbook.Worksheet(3).Cell(2, 1).GetString());
        Assert.Equal("=1+1", workbook.Worksheet(3).Cell(2, 2).GetString());
        Assert.False(workbook.Worksheet(3).Cell(2, 2).HasFormula);
        Assert.Equal(XLDataType.Number, workbook.Worksheet(3).Cell(2, 6).DataType);
        Assert.Equal(3, workbook.Worksheet(4).Cell(2, 3).GetDouble());
        Assert.Equal(120, workbook.Worksheet(4).Cell(2, 4).GetDouble());
        Assert.Equal(3, workbook.Worksheet(5).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void EmptyReportStillExportsAllHeaders()
    {
        using var stream = new MemoryStream(AdminUsageExcelExporter.Export(new() { From = new(2026, 1, 1), To = new(2026, 1, 2) }, null, null));
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(5, workbook.Worksheets.Count);
        foreach (var sheet in workbook.Worksheets.Skip(1))
            Assert.Equal(1, sheet.LastRowUsed()!.RowNumber());
    }
}
