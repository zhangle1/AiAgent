using AiAgent.Backend.Services.Chat.Agentic;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Chat.Planning;

/// <summary>
/// 知识库查询计划器，负责识别用户问题属于语义问答、页码范围总结、整本书概览等类型。
/// </summary>
public interface IKnowledgeQueryPlanner
{
    /// <summary>
    /// 根据 Agent 上下文生成工具调用计划。
    /// </summary>
    KnowledgeQueryPlan Plan(AgentContext context);
}

/// <summary>
/// 基于规则的第一版 Query Planner，后续可升级成 LLM planner。
/// </summary>
public sealed class KnowledgeQueryPlanner : IKnowledgeQueryPlanner
{
    private static readonly Regex FirstPagesRegex = new(@"前\s*(\d{1,4})\s*页", RegexOptions.Compiled);
    private static readonly Regex PageRangeRegex = new(@"第?\s*(\d{1,4})\s*(?:到|-|~|至)\s*(\d{1,4})\s*页", RegexOptions.Compiled);

    /// <summary>
    /// 根据关键词和页码表达式生成检索计划。
    /// </summary>
    public KnowledgeQueryPlan Plan(AgentContext context)
    {
        var question = (context.UserMessage ?? string.Empty).Trim();
        var plan = new KnowledgeQueryPlan
        {
            Intent = KnowledgeQueryIntent.SemanticQuestion,
            NormalizedQuestion = question,
            SearchQuery = question,
            TopK = context.TopK
        };

        var range = DetectPageRange(question);
        if (range is not null)
        {
            plan.Intent = KnowledgeQueryIntent.PageRangeSummary;
            plan.PageStart = range.Value.Start;
            plan.PageEnd = range.Value.End;
            plan.NeedsPageRange = true;
            plan.ToolCalls.Add(new ToolCall
            {
                Name = AgentToolNames.ReadPageRange,
                Arguments =
                {
                    ["page_start"] = range.Value.Start,
                    ["page_end"] = range.Value.End
                }
            });
            return plan;
        }

        if (LooksLikeDocumentOverview(question))
        {
            plan.Intent = KnowledgeQueryIntent.DocumentOverview;
            plan.NeedsPageRange = true;
            plan.NeedsVectorSearch = true;
            plan.PageStart = 1;
            plan.PageEnd = 30;
            plan.ToolCalls.Add(new ToolCall
            {
                Name = AgentToolNames.ReadPageRange,
                Arguments =
                {
                    ["page_start"] = 1,
                    ["page_end"] = 30
                }
            });
            plan.ToolCalls.Add(new ToolCall
            {
                Name = AgentToolNames.RagSearch,
                Arguments =
                {
                    ["query"] = question,
                    ["top_k"] = Math.Min(context.TopK, 5)
                }
            });
            return plan;
        }

        plan.NeedsVectorSearch = true;
        plan.ToolCalls.Add(new ToolCall
        {
            Name = AgentToolNames.RagSearch,
            Arguments =
            {
                ["query"] = question,
                ["top_k"] = context.TopK
            }
        });
        return plan;
    }

    private static (int Start, int End)? DetectPageRange(string question)
    {
        var rangeMatch = PageRangeRegex.Match(question);
        if (rangeMatch.Success
            && int.TryParse(rangeMatch.Groups[1].Value, out var start)
            && int.TryParse(rangeMatch.Groups[2].Value, out var end))
        {
            return NormalizeRange(start, end);
        }

        var firstPagesMatch = FirstPagesRegex.Match(question);
        if (firstPagesMatch.Success && int.TryParse(firstPagesMatch.Groups[1].Value, out var firstPages))
        {
            return NormalizeRange(1, firstPages);
        }

        return null;
    }

    private static (int Start, int End) NormalizeRange(int start, int end)
    {
        start = Math.Clamp(start, 1, 5000);
        end = Math.Clamp(end, 1, 5000);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (end - start > 120)
        {
            end = start + 120;
        }

        return (start, end);
    }

    private static bool LooksLikeDocumentOverview(string question)
    {
        return question.Contains("这本书", StringComparison.OrdinalIgnoreCase)
            && (question.Contains("讲了什么", StringComparison.OrdinalIgnoreCase)
                || question.Contains("主要内容", StringComparison.OrdinalIgnoreCase)
                || question.Contains("主要讲", StringComparison.OrdinalIgnoreCase)
                || question.Contains("总结", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 知识库查询计划。
/// </summary>
public sealed class KnowledgeQueryPlan
{
    /// <summary>
    /// 查询意图。
    /// </summary>
    public KnowledgeQueryIntent Intent { get; set; } = KnowledgeQueryIntent.SemanticQuestion;

    /// <summary>
    /// 规范化后的用户问题。
    /// </summary>
    public string NormalizedQuestion { get; set; } = string.Empty;

    /// <summary>
    /// 用于语义检索的查询文本。
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>
    /// 页码范围起点。
    /// </summary>
    public int? PageStart { get; set; }

    /// <summary>
    /// 页码范围终点。
    /// </summary>
    public int? PageEnd { get; set; }

    /// <summary>
    /// 最终引用片段数量。
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 是否需要页码范围读取。
    /// </summary>
    public bool NeedsPageRange { get; set; }

    /// <summary>
    /// 是否需要向量/混合检索。
    /// </summary>
    public bool NeedsVectorSearch { get; set; }

    /// <summary>
    /// 本计划要执行的工具调用。
    /// </summary>
    public List<ToolCall> ToolCalls { get; set; } = [];
}

/// <summary>
/// 知识库查询意图。
/// </summary>
public enum KnowledgeQueryIntent
{
    /// <summary>
    /// 普通语义问答，适合向量或混合检索。
    /// </summary>
    SemanticQuestion,

    /// <summary>
    /// 页码范围总结，例如“前50页讲了什么”。
    /// </summary>
    PageRangeSummary,

    /// <summary>
    /// 整本书或整份文档概览。
    /// </summary>
    DocumentOverview,

    /// <summary>
    /// 章节总结，后续接目录结构。
    /// </summary>
    ChapterSummary
}

/// <summary>
/// Agent 内置工具名称。
/// </summary>
public static class AgentToolNames
{
    /// <summary>
    /// RAG 语义检索工具。
    /// </summary>
    public const string RagSearch = "rag_search";

    /// <summary>
    /// 按页码范围读取 chunk 工具。
    /// </summary>
    public const string ReadPageRange = "read_page_range";

    public const string CodeSearch = "code_search";
    public const string FindSymbol = "find_symbol";
    public const string CodeRepositoryOverview = "code_repository_overview";
    public const string InspectDashboardWorkspace = "inspect_dashboard_workspace";
    public const string SearchDashboardCode = "search_dashboard_code";
    public const string ReadDashboardFile = "read_dashboard_file";
    public const string ApplyDashboardPatch = "apply_dashboard_patch";
    public const string ValidateDashboardChange = "validate_dashboard_change";
    public const string WriteDashboardFile = "write_dashboard_file";
}
