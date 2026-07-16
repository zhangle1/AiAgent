using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>
/// 知识库索引物化器，负责把 Python worker 输出的 chunks.jsonl 导入结构化 chunk 表。
/// </summary>
public interface IKnowledgeIndexMaterializer
{
    /// <summary>
    /// 导入指定索引版本的 chunk 文件。
    /// </summary>
    int ImportChunks(AiKnowledgeBase kb, AiKnowledgeIndexVersion version, IReadOnlyList<AiKnowledgeDocument> documents);
}

/// <summary>
/// 默认知识库索引物化器。
/// </summary>
public sealed class KnowledgeIndexMaterializer : IKnowledgeIndexMaterializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISqlSugarClient _db;
    private readonly ILogger<KnowledgeIndexMaterializer> _logger;

    /// <summary>
    /// 初始化索引物化器。
    /// </summary>
    public KnowledgeIndexMaterializer(ISqlSugarClient db, ILogger<KnowledgeIndexMaterializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 从版本目录读取 chunks.jsonl，并写入 ai_knowledge_chunk。
    /// </summary>
    public int ImportChunks(AiKnowledgeBase kb, AiKnowledgeIndexVersion version, IReadOnlyList<AiKnowledgeDocument> documents)
    {
        if (string.IsNullOrWhiteSpace(version.StoragePath))
        {
            return 0;
        }

        var chunkPath = Path.Combine(version.StoragePath, "chunks.jsonl");
        if (!File.Exists(chunkPath))
        {
            _logger.LogWarning("Knowledge chunk export was not found. Kb={KbName}, VersionId={VersionId}, Path={ChunkPath}", kb.Name, version.Id, chunkPath);
            return 0;
        }

        var documentMap = BuildDocumentMap(documents);
        var rows = new List<AiKnowledgeChunk>();
        foreach (var line in File.ReadLines(chunkPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<ExportedChunkRecord>(line, JsonOptions);
            if (record is null || string.IsNullOrWhiteSpace(record.Content))
            {
                continue;
            }

            var document = ResolveDocument(documentMap, record) ?? documents.FirstOrDefault();
            rows.Add(new AiKnowledgeChunk
            {
                KnowledgeBaseId = kb.Id,
                DocumentId = document?.Id ?? 0,
                IndexVersionId = version.Id,
                ChunkNo = record.ChunkNo <= 0 ? rows.Count + 1 : record.ChunkNo,
                Title = record.Title,
                Content = record.Content,
                TokenCount = record.TokenCount,
                PageNo = record.PageNo,
                MetadataJson = JsonSerializer.Serialize(record.Metadata ?? [], JsonOptions),
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.Deleteable<AiKnowledgeChunk>()
            .Where(x => x.KnowledgeBaseId == kb.Id && x.IndexVersionId == version.Id)
            .ExecuteCommand();

        if (rows.Count == 0)
        {
            return 0;
        }

        _db.Insertable(rows).ExecuteCommand();
        return rows.Count;
    }

    private static Dictionary<string, AiKnowledgeDocument> BuildDocumentMap(IReadOnlyList<AiKnowledgeDocument> documents)
    {
        var map = new Dictionary<string, AiKnowledgeDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in documents)
        {
            AddKey(map, doc.StoragePath, doc);
            AddKey(map, Path.GetFullPath(doc.StoragePath), doc);
            AddKey(map, doc.FileName, doc);
            AddKey(map, doc.OriginalFileName, doc);
        }

        return map;
    }

    private static void AddKey(Dictionary<string, AiKnowledgeDocument> map, string? key, AiKnowledgeDocument document)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        map.TryAdd(key.Trim(), document);
    }

    private static AiKnowledgeDocument? ResolveDocument(Dictionary<string, AiKnowledgeDocument> map, ExportedChunkRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.FilePath))
        {
            var path = record.FilePath.Trim();
            if (map.TryGetValue(path, out var byPath))
            {
                return byPath;
            }

            if (map.TryGetValue(Path.GetFullPath(path), out var byFullPath))
            {
                return byFullPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(record.FileName) && map.TryGetValue(record.FileName.Trim(), out var byName))
        {
            return byName;
        }

        return null;
    }

    private sealed class ExportedChunkRecord
    {
        [JsonPropertyName("chunk_no")]
        public int ChunkNo { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("token_count")]
        public int TokenCount { get; set; }

        [JsonPropertyName("page_no")]
        public int? PageNo { get; set; }

        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object?>? Metadata { get; set; }
    }
}