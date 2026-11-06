using AiAgent.Backend.Dtos.Admin;
using ClosedXML.Excel;

namespace AiAgent.Backend.Services.Admin;

public static class AdminUsageExcelExporter
{
    public static byte[] Export(AdminUsageReportDto report, string? userId, long? projectId)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet Sheet(string name, params string[] headers)
        {
            var sheet = workbook.Worksheets.Add(name);
            Row(sheet, 1, headers.Select(x => (XLCellValue)x).ToArray());
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("DBEAFE");
            sheet.SheetView.FreezeRows(1);
            return sheet;
        }
        var summary = Sheet("统计概览", "指标", "数值");
        Row(summary, 2, "开始日期（UTC）", report.From.ToString("yyyy-MM-dd"));
        Row(summary, 3, "结束日期（UTC，含）", report.To.AddDays(-1).ToString("yyyy-MM-dd"));
        Row(summary, 4, "聚合维度", report.Period switch { "week" => "按周", "month" => "按月", "year" => "按年", _ => "按日" });
        Row(summary, 5, "用户筛选", userId ?? "全部用户");
        Row(summary, 6, "项目筛选", projectId?.ToString() ?? "全部项目");
        Row(summary, 7, "活跃会话数", report.Metrics.SessionCount);
        Row(summary, 8, "完成轮次", report.TurnCount);
        Row(summary, 9, "活跃天数", report.Metrics.ActiveDays);
        Row(summary, 10, "日均轮次", report.Metrics.DailyTurns);
        Row(summary, 11, "累计 Token", report.TotalTokens);
        Row(summary, 12, "参与用户数", report.Users.Count);
        Row(summary, 13, "统计口径", "仅统计已记录用量的成功轮次；日均含零活动日；项目按会话当前归属；Token 含估算值。跨时间桶会话数不可相加作为全期去重数。");
        var trend = Sheet("时间趋势", "周期", "活跃会话数", "完成轮次", "活跃天数", "日均轮次", "Token");
        var index = 2;
        foreach (var bucket in report.Buckets)
            Row(trend, index++, bucket.Label, bucket.Metrics.SessionCount, bucket.TurnCount, bucket.Metrics.ActiveDays, bucket.Metrics.DailyTurns, bucket.TotalTokens);
        var users = Sheet("用户汇总", "用户ID", "用户", "完成轮次", "输入 Token", "输出 Token", "累计 Token");
        index = 2;
        foreach (var user in report.Users)
            Row(users, index++, user.UserId, user.Username, user.TurnCount, user.PromptTokens, user.CompletionTokens, user.TotalTokens);
        var projects = Sheet("项目对比", "项目ID", "项目", "完成轮次", "Token");
        index = 2;
        foreach (var group in report.UserProjects.GroupBy(x => x.ProjectId))
            Row(projects, index++, group.Key?.ToString() ?? "0", group.First().ProjectName, group.Sum(x => x.TurnCount), group.Sum(x => x.TotalTokens));
        var detail = Sheet("用户×项目明细", "用户ID", "用户", "项目ID", "项目", "活跃会话数", "完成轮次", "活跃天数", "日均轮次", "Token");
        index = 2;
        foreach (var item in report.UserProjects)
            Row(detail, index++, item.UserId, item.Username, item.ProjectId?.ToString() ?? "0", item.ProjectName, item.Metrics.SessionCount, item.TurnCount, item.Metrics.ActiveDays, item.Metrics.DailyTurns, item.TotalTokens);
        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Columns().Width = 20;
            sheet.RangeUsed()!.SetAutoFilter();
        }
        summary.Column(2).Width = 60;
        summary.Cell(13, 2).Style.Alignment.WrapText = true;
        summary.Row(13).Height = 60;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Row(IXLWorksheet sheet, int row, params XLCellValue[] values)
    {
        for (var column = 0; column < values.Length; column++)
            sheet.Cell(row, column + 1).Value = values[column];
    }
}
