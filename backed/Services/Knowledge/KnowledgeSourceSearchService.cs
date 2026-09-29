using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;
using System.Text;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>
/// LLM Wiki 风格的来源层检索：原始文件是事实源，解析文本只是可追溯的读取副本。
/// 该检索不依赖向量索引或模型，适合上传后立即验证入库结果。
/// </summary>
public sealed class KnowledgeSourceSearchService(ISqlSugarClient db, IKnowledgePathService paths)
{
    private const int MaxQueryLength = 4000;
    private const int MaxTopK = 20;
    private const int MaxTextCharacters = 4_000_000;
    private const int SnippetRadius = 240;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".html", ".htm",
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", ".sql", ".sh", ".ps1", ".css"
    };

    public async Task<KnowledgeSearchResponse> SearchAsync(string name, string query, int topK, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaxQueryLength)
            throw new ArgumentException("Query must contain 1-4000 characters.");

        var normalizedName = paths.NormalizeName(name);
        AiKnowledgeBase knowledgeBase;
        List<AiKnowledgeDocument> documents;
        Dictionary<long, string?> parsedPaths;
        using (var connection = db.CopyNew())
        {
            knowledgeBase = connection.Queryable<AiKnowledgeBase>()
                .Where(x => x.Name == normalizedName && !x.IsDeleted).First()
                ?? throw new InvalidOperationException($"Knowledge base '{name}' does not exist.");
            documents = connection.Queryable<AiKnowledgeDocument>()
                .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt).ToList();
            parsedPaths = connection.Queryable<AiKnowledgeParsedDocument>()
                .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.DocumentId != null)
                .OrderByDescending(x => x.Id).ToList()
                .GroupBy(x => x.DocumentId!.Value)
                .ToDictionary(x => x.Key, x => x.First().ContentPath);
        }
        var root = Path.GetFullPath(paths.GetKnowledgeBasePath(knowledgeBase.Name));

        var hits = new List<KnowledgeCitationDto>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.GetFullPath(document.StoragePath);
            EnsureInside(sourcePath, root);
            var parsedPath = parsedPaths.GetValueOrDefault(document.Id);
            var searchablePath = sourcePath;
            if (!string.IsNullOrWhiteSpace(parsedPath) && File.Exists(parsedPath))
            {
                var resolvedParsedPath = Path.GetFullPath(parsedPath);
                EnsureInside(resolvedParsedPath, root);
                searchablePath = resolvedParsedPath;
            }
            string? text = null;
            try
            {
                text = await ReadSearchableTextAsync(searchablePath, cancellationToken);
            }
            catch (IOException)
            {
                // Keep filename/hash retrieval useful even when a source is temporarily locked.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep filename/hash retrieval useful even when a source is temporarily locked.
            }
            var fileNameMatch = document.OriginalFileName.Contains(query, StringComparison.OrdinalIgnoreCase);
            var contentIndex = text?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1;
            if (!fileNameMatch && contentIndex < 0) continue;

            var quote = contentIndex >= 0
                ? BuildSnippet(text!, contentIndex, query.Length)
                : $"原始文件名匹配：{document.OriginalFileName}";
            var score = contentIndex >= 0 ? (fileNameMatch ? 1d : 0.9d) : 0.7d;
            hits.Add(new KnowledgeCitationDto
            {
                Score = score,
                Text = quote,
                Metadata = new()
                {
                    ["source"] = "raw_source",
                    ["knowledge_base_name"] = knowledgeBase.Name,
                    ["document_id"] = document.Id,
                    ["file_name"] = document.OriginalFileName,
                    ["file_hash"] = document.FileHash,
                    ["document_status"] = document.Status,
                    ["matched_in"] = contentIndex >= 0 ? "parsed_content_or_text" : "file_name",
                    ["raw_file_url"] = $"/api/v1/knowledge/{Uri.EscapeDataString(knowledgeBase.Name)}/documents/{document.Id}/file"
                }
            });
        }

        var ordered = hits.OrderByDescending(x => x.Score).Take(Math.Clamp(topK, 1, MaxTopK)).ToList();
        var content = ordered.Count == 0 ? "原始文件中未找到相关内容。" :
            "原始文件检索结果（内容来自上传文件或其解析副本）：\n\n" +
            string.Join("\n\n", ordered.Select((hit, index) => $"[{index + 1}] {hit.Metadata["file_name"]}\n{hit.Text}"));
        return new KnowledgeSearchResponse
        {
            Query = query,
            Provider = "raw-source",
            Answer = content,
            Content = content,
            Citations = ordered
        };
    }

    private static async Task<string?> ReadSearchableTextAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path);
        if (!TextExtensions.Contains(extension)) return null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0 || info.Length > MaxTextCharacters * 4L) return null;
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        return text.Length > MaxTextCharacters ? text[..MaxTextCharacters] : text;
    }

    private static string BuildSnippet(string text, int index, int matchLength)
    {
        var start = Math.Max(0, index - SnippetRadius);
        var end = Math.Min(text.Length, index + matchLength + SnippetRadius);
        var snippet = text[start..end].Replace("\r", " ").Replace("\n", " ");
        return (start > 0 ? "…" : string.Empty) + snippet + (end < text.Length ? "…" : string.Empty);
    }

    private static void EnsureInside(string path, string root)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Knowledge source file is outside the allowed knowledge base directory.");
    }
}
