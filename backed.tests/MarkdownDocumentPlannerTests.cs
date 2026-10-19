using System;
using System.Linq;
using System.Threading;
using AiAgent.Backend.Services.Parsing;

namespace AiAgent.Backend.Tests;

public sealed class MarkdownDocumentPlannerTests
{
    private static readonly MarkdownDocumentPlanner Planner = new();
    private static MarkdownParsingOptions Small => new() { MaxSectionChars = 140, MaxSectionTokens = 80, MinSectionTokens = 10 };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SmallDocumentAndNoSplitPreserveWholeSource(bool split)
    {
        var source = "---\r\ntitle: example\r\n---\r\n# 内容\r\n\r\n中文 😀 原文。\r\n";
        var options = new MarkdownParsingOptions { SplitContent = split };
        if (!split) { options.MaxSectionChars = 2; options.MaxSectionTokens = 1; options.MinSectionTokens = 0; }
        var plan = Planner.Plan(source, "技术报告.md", options);
        Assert.Equal("技术报告.md", Assert.Single(plan.Sections).RelativePath);
        Assert.Equal(source, plan.Sections[0].Content); Coverage(source, plan);
    }

    [Fact]
    public void HeadingsAreHierarchicalAndSkippedLevelsRemainChildren()
    {
        var source = "# 概述\n\n" + new string('a', 90) + "\n\n# 安装\n\n父级介绍\n\n### Linux\n\n" + new string('b', 100) + "\n\n### Windows\n\n" + new string('c', 100);
        var plan = Planner.Plan(source, "技术报告.md", Small);
        Assert.Contains(plan.Sections, p => p.RelativePath == "概述.md");
        Assert.Contains(plan.Sections, p => p.RelativePath == "安装/Linux.md" && p.Content.Contains("# 安装"));
        Assert.Contains(plan.Sections, p => p.RelativePath == "安装/Windows.md");
        Coverage(source, plan);
    }

    [Fact]
    public void CodeCommentsEscapedHeadingsAndFrontmatterDoNotBecomePaths()
    {
        var source = "---\n# frontmatter\n---\n# 真实\n\n" + new string('x', 80) + "\n\n````md\n# backtick\n```\n# stillCode\n````\n\n~~~\n# tilde\n~~~\n\n<!--\n# comment\n-->\n\n    # indented\n\n\t# tabbed\n\n\\# escaped\n\n" + new string('z', 150);
        var plan = Planner.Plan(source, "sample.md", Small);
        Assert.All(plan.Sections, section => Assert.DoesNotContain(new[] { "frontmatter", "backtick", "stillCode", "tilde", "comment", "indented", "tabbed", "escaped" }, name => section.RelativePath.Contains(name)));
        Coverage(source, plan);
    }

    [Fact]
    public void AdjacentSmallSectionsMergeButRespectCharacterBudget()
    {
        var source = "# 简介\n\n一。\n\n# 使用\n\n二。\n\n# 详情\n\n" + new string('字', 180);
        var options = new MarkdownParsingOptions { MaxSectionChars = 90, MaxSectionTokens = 2048, MinSectionTokens = 512 };
        var plan = Planner.Plan(source, "报告.md", options);
        Assert.Contains(plan.Sections, p => p.Content.Contains("# 简介") && p.Content.Contains("# 使用"));
        Assert.All(plan.Sections, p => Assert.InRange(p.Content.Length, 1, options.MaxSectionChars));
        Coverage(source, plan);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("dense-cjk")]
    [InlineData("emoji")]
    public void ParagraphFallbackPreservesEveryCharacterAndNamesUseDocument(string kind)
    {
        var unit = kind == "dense-cjk" ? "中文测试" : kind == "emoji" ? "😀🚀" : "abcd";
        var source = string.Concat(Enumerable.Repeat(unit, 2200)) + "\r\n\r\nTHE_END  ";
        var plan = Planner.Plan(source, "技术报告.md");
        Assert.True(plan.Sections.Count > 1);
        Assert.All(plan.Sections, part => {
            Assert.StartsWith("技术报告_", part.RelativePath); Assert.DoesNotContain("part-", part.RelativePath);
            Assert.InRange(part.Content.Length, 1, 6000); Assert.InRange(MarkdownDocumentPlanner.EstimateTokens(part.Content), 0, 2048);
            Assert.False(char.IsLowSurrogate(part.Content[0])); Assert.False(char.IsHighSurrogate(part.Content[^1]));
        });
        Coverage(source, plan);
    }

    [Fact]
    public void TablesRepeatHeadersKeepRowsAndAllowOneOversizedRow()
    {
        var rows = Enumerable.Range(0, 18).Select(i => $"| 行{i} | {new string('测', i == 9 ? 180 : 20)} |\n").ToArray();
        var source = "# 数据\n\n| 名称 | 内容 |\n| --- | --- |\n" + string.Concat(rows);
        var plan = Planner.Plan(source, "表格.md", Small);
        Assert.True(plan.Sections.Count > 2);
        Assert.All(rows, row => Assert.Single(plan.Sections, p => p.Content.Contains(row)));
        Assert.Contains(plan.Sections, p => p.RepeatedHeader.Length > 0 && p.Content.StartsWith("| 名称 | 内容 |\n| --- | --- |\n"));
        Assert.Single(plan.Sections, p => p.Content.Length > Small.MaxSectionChars);
        Coverage(source, plan);
    }

    [Fact]
    public void UnsafeAndDuplicateTitlesGetUniquePortableNames()
    {
        var source = string.Join("\n\n", new[] { "CON", "../路径:<坏>|?%", "同名", "同名", "SAME", "same", new string('长', 220) }.Select(title => "# " + title + "\n\n" + new string('x', 80)));
        var plan = Planner.Plan(source, "source.md", Small);
        Assert.Equal(plan.Sections.Count, plan.Sections.Select(p => p.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(plan.Sections, p => Assert.DoesNotContain("..", p.RelativePath.Split('/')));
        Assert.All(plan.Sections, p => Assert.DoesNotContain(':', p.RelativePath));
        Coverage(source, plan);
    }

    [Fact]
    public void TinyPreambleJoinsHierarchicalSectionWithoutLosingSourceOrder()
    {
        var source = "文档说明。\n\n# 安装\n\n## Linux\n\n" + new string('字', 100) + "\n\n## Windows\n\n" + new string('文', 100);
        var plan = Planner.Plan(source, "报告.md", Small);
        Assert.All(plan.Sections, p => Assert.StartsWith("安装/", p.RelativePath));
        Assert.Contains("文档说明。", plan.Sections[0].Content); Coverage(source, plan);
    }

    [Fact]
    public void InitialHorizontalRuleWithoutClosingFrontmatterDoesNotHideHeadings()
    {
        var source = "---\n\n# 第一章\n\n" + new string('字', 90) + "\n\n# 第二章\n\n" + new string('文', 90);
        var plan = Planner.Plan(source, "report.md", Small);
        Assert.Contains(plan.Sections, p => p.RelativePath.Contains("第二章", StringComparison.Ordinal));
        Coverage(source, plan);
    }

    [Fact]
    public void LayoutIsDeterministicAndCanBeCancelled()
    {
        var source = string.Concat(Enumerable.Repeat("# 标题\n\n" + new string('字', 80) + "\n\n", 40));
        Assert.Equal(Planner.Plan(source, "doc.md", Small).Sections, Planner.Plan(source, "doc.md", Small).Sections);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Planner.Plan(source, "doc.md", Small, cancel.Token));
    }

    private static void Coverage(string source, MarkdownDocumentPlan plan)
    {
        var next = 0;
        foreach (var part in plan.Sections)
        {
            Assert.Equal(next, part.SourceStart); Assert.True(part.SourceEnd > next);
            Assert.Equal(part.RepeatedHeader + source[part.SourceStart..part.SourceEnd], part.Content); next = part.SourceEnd;
        }
        Assert.Equal(source.Length, next);
    }
}
