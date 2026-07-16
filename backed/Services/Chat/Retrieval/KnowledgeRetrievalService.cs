using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Planning;
using AiAgent.Backend.Services.Rag;
using SqlSugar;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat.Retrieval;

/// <summary>
/// 知识库检索服务，为 Agent 工具提供语义检索和结构化页码读取能力。
/// </summary>
public interface IKnowledgeRetrievalService
{
    /// <summary>
    /// 执行 RAG 语义检索。
    /// </summary>
    Task<ToolResult> SearchAsync(AgentContext context, string query, int topK, CancellationToken cancellationToken);

    /// <summary>
    /// 按页码范围读取已入库的 chunk。
    /// </summary>
    Task<ToolResult> ReadPageRangeAsync(AgentContext context, int pageStart, int pageEnd, CancellationToken cancellationToken);
}

/// <summary>
/// 默认知识库检索服务，封装数据库、RAG provider 和引用片段格式化。
/// </summary>
public sealed class KnowledgeRetrievalService : IKnowledgeRetrievalService
{
    private readonly ISqlSugarClient _db;
    private readonly IRagService _ragService;

    /// <summary>
    /// 初始化知识库检索服务。
    /// </summary>
    public KnowledgeRetrievalService(ISqlSugarClient db, IRagService ragService)
    {
        _db = db;
        _ragService = ragService;
    }

    /// <summary>
    /// 执行向量/混合检索，适合普通语义问题。
    /// </summary>
    public async Task<ToolResult> SearchAsync(AgentContext context, string query, int topK, CancellationToken cancellationToken)
    {
        if (context.KnowledgeBaseNames.Count > 1)
        {
            return await SearchManyAsync(context, query, topK, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(context.KnowledgeBaseName))
        {
            return ToolResult.Failed("未选择知识库。");
        }

        var kb = FindKnowledgeBase(context.KnowledgeBaseName);
        var version = FindActiveVersion(kb);
        if (version is null)
        {
            return ToolResult.Failed("当前知识库没有可用的激活索引版本。");
        }

        var result = await _ragService.SearchAsync(kb.EngineType, new RagSearchRequest
        {
            KnowledgeBaseName = kb.Name,
            PersistDir = version.StoragePath ?? string.Empty,
            Query = query,
            TopK = Math.Clamp(topK, 1, 12)
        }, cancellationToken);

        if (!result.Ok)
        {
            return ToolResult.Failed(result.ErrorMessage ?? "知识库语义检索失败。");
        }

        var citations = result.Citations
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new KnowledgeCitationDto
            {
                Score = x.Score,
                Text = x.Text,
                Metadata = x.Metadata
            })
            .ToList();

        return new ToolResult
        {
            Success = true,
            Content = BuildCitationContext("语义检索结果", citations),
            Citations = citations,
            Metadata =
            {
                ["tool"] = AgentToolNames.RagSearch,
                ["provider"] = result.Provider
            }
        };
    }

    /// <summary>
    /// 按页码范围读取 chunk，适合“前50页”“第10到30页”等问题。
    /// </summary>
    public Task<ToolResult> ReadPageRangeAsync(AgentContext context, int pageStart, int pageEnd, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.KnowledgeBaseName))
        {
            return Task.FromResult(ToolResult.Failed("未选择知识库。"));
        }

        var kb = FindKnowledgeBase(context.KnowledgeBaseName);
        var version = FindActiveVersion(kb);
        if (version is null)
        {
            return Task.FromResult(ToolResult.Failed("当前知识库没有可用的激活索引版本。"));
        }

        pageStart = Math.Clamp(pageStart, 1, 5000);
        pageEnd = Math.Clamp(pageEnd, 1, 5000);
        if (pageStart > pageEnd)
        {
            (pageStart, pageEnd) = (pageEnd, pageStart);
        }

        var chunks = _db.Queryable<AiKnowledgeChunk>()
            .Where(x => x.KnowledgeBaseId == kb.Id
                && x.IndexVersionId == version.Id
                && x.PageNo >= pageStart
                && x.PageNo <= pageEnd)
            .OrderBy(x => x.PageNo)
            .OrderBy(x => x.ChunkNo)
            .ToList();

        if (chunks.Count == 0)
        {
            return Task.FromResult(ToolResult.Failed(
                $"没有在结构化 chunk 表中找到第 {pageStart}-{pageEnd} 页内容。需要先在重建索引后把 worker 产出的 chunks 导入 ai_knowledge_chunk。"));
        }

        var documentIds = chunks.Select(x => x.DocumentId).Distinct().ToList();
        var docs = _db.Queryable<AiKnowledgeDocument>()
            .Where(x => documentIds.Contains(x.Id))
            .ToList()
            .ToDictionary(x => x.Id);

        var citations = chunks
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .Take(80)
            .Select(x =>
            {
                docs.TryGetValue(x.DocumentId, out var doc);
                return new KnowledgeCitationDto
                {
                    Text = TrimForContext(x.Content, 1400),
                    Metadata =
                    {
                        ["file_name"] = doc?.OriginalFileName ?? doc?.FileName ?? "document",
                        ["page_label"] = x.PageNo?.ToString() ?? "",
                        ["page_no"] = x.PageNo,
                        ["chunk_no"] = x.ChunkNo,
                        ["document_id"] = x.DocumentId,
                        ["index_version_id"] = x.IndexVersionId,
                        ["source"] = "ai_knowledge_chunk"
                    }
                };
            })
            .ToList();

        return Task.FromResult(new ToolResult
        {
            Success = true,
            Content = BuildCitationContext($"第 {pageStart}-{pageEnd} 页结构化内容", citations),
            Citations = citations,
            Metadata =
            {
                ["tool"] = AgentToolNames.ReadPageRange,
                ["page_start"] = pageStart,
                ["page_end"] = pageEnd,
                ["chunk_count"] = citations.Count
            }
        });
    }

    private async Task<ToolResult> SearchManyAsync(AgentContext context, string query, int topK, CancellationToken cancellationToken)
    {
        var citations = new List<KnowledgeCitationDto>();
        var succeeded = 0;
        foreach (var knowledgeBaseName in context.KnowledgeBaseNames)
        {
            var selectedContext = new AgentContext
            {
                KnowledgeBaseName = knowledgeBaseName,
                KnowledgeBaseNames = [knowledgeBaseName],
                TopK = context.TopK,
                Stats = context.Stats
            };
            var result = await SearchAsync(selectedContext, query, topK, cancellationToken);
            if (!result.Success)
            {
                continue;
            }

            succeeded++;
            foreach (var citation in result.Citations)
            {
                citation.Metadata["knowledge_base_name"] = knowledgeBaseName;
                citations.Add(citation);
            }
        }

        if (citations.Count == 0)
        {
            return ToolResult.Failed("No readable retrieval result was returned from the selected knowledge bases.");
        }

        return new ToolResult
        {
            Success = true,
            Content = BuildCitationContext("Multi-source retrieval results", citations),
            Citations = citations,
            Metadata =
            {
                ["tool"] = AgentToolNames.RagSearch,
                ["knowledge_base_count"] = succeeded
            }
        };
    }

    private AiKnowledgeBase FindKnowledgeBase(string kbName)
    {
        var normalized = kbName.Trim().ToLowerInvariant();
        var kb = _db.Queryable<AiKnowledgeBase>()
            .Where(x => x.Name == normalized && !x.IsDeleted)
            .First();
        return kb ?? throw new InvalidOperationException($"Knowledge base '{kbName}' does not exist.");
    }

    private AiKnowledgeIndexVersion? FindActiveVersion(AiKnowledgeBase kb)
    {
        if (!kb.ActiveVersionId.HasValue)
        {
            return null;
        }

        return _db.Queryable<AiKnowledgeIndexVersion>()
            .Where(x => x.Id == kb.ActiveVersionId.Value)
            .First();
    }

    private static string BuildCitationContext(string title, List<KnowledgeCitationDto> citations)
    {
        if (citations.Count == 0)
        {
            return $"{title}：没有可用片段。";
        }

        var builder = new StringBuilder();
        builder.AppendLine(title);
        for (var i = 0; i < citations.Count; i++)
        {
            var citation = citations[i];
            builder.AppendLine($"[{i + 1}] {BuildCitationTitle(citation.Metadata)}");
            builder.AppendLine(citation.Text);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildCitationTitle(Dictionary<string, object?> metadata)
    {
        var file = MetadataValue(metadata, "file_name") ?? MetadataValue(metadata, "file_path") ?? "chunk";
        var page = MetadataValue(metadata, "page_label") ?? MetadataValue(metadata, "page_no");
        return string.IsNullOrWhiteSpace(page) ? file : $"{file} p.{page}";
    }

    private static string? MetadataValue(Dictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        }

        return value.ToString();
    }

    private static string TrimForContext(string value, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "...";
    }
}