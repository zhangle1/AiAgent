using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiAgent.Knowledge.Core;

namespace AiAgent.Backend.Services.Knowledge;

public sealed class KnowledgeWorkspaceService(ISqlSugarClient db)
{
    public static KnowledgeOrganizationDto ReadOrganization(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return new();
        try { return JsonNode.Parse(metadata)?["organization"]?.Deserialize<KnowledgeOrganizationDto>() ?? new(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(); }
    }

    public KnowledgeOrganizationDto SaveOrganization(string name, KnowledgeOrganizationDto value)
    {
        value.Company = value.Company?.Trim();
        value.Project = value.Project?.Trim();
        if (value.Company?.Length > 180 || value.Project?.Length > 180)
            throw new ArgumentException("Company and project names must be at most 180 characters.");
        if (!string.IsNullOrEmpty(value.Project) && string.IsNullOrEmpty(value.Company))
            throw new ArgumentException("A project must belong to a company.");
        var kb = Find(name);
        var metadata = string.IsNullOrWhiteSpace(kb.MetadataJson) ? new JsonObject() : JsonNode.Parse(kb.MetadataJson)!.AsObject();
        metadata["organization"] = JsonSerializer.SerializeToNode(value);
        kb.MetadataJson = metadata.ToJsonString();
        kb.UpdatedAt = DateTime.UtcNow;
        db.Updateable(kb).UpdateColumns(x => new { x.MetadataJson, x.UpdatedAt }).ExecuteCommand();
        return value;
    }

    public List<KnowledgePageDto> ListPages(string name)
    {
        var kb = Find(name);
        var documents = db.Queryable<AiKnowledgeDocument>().Where(x => x.KnowledgeBaseId == kb.Id && !x.IsDeleted)
            .ToList().ToDictionary(x => x.Id);
        var latestIds = db.Queryable<AiKnowledgeArtifact>().Where(x => x.KnowledgeBaseId == kb.Id && x.DocumentId != null)
            .GroupBy(x => x.DocumentId).Select(x => SqlFunc.AggregateMax(x.Id)).ToList();
        if (latestIds.Count == 0) return [];
        return db.Queryable<AiKnowledgeArtifact>().Where(x => latestIds.Contains(x.Id))
            .OrderByDescending(x => x.Id).ToList()
            .Where(x => x.DocumentId.HasValue && documents.ContainsKey(x.DocumentId.Value))
            .GroupBy(x => x.DocumentId).Select(group => group.First())
            .SelectMany(x => ExpandPages(x, documents[x.DocumentId!.Value].OriginalFileName)).ToList();
    }

    // Older artifacts contain only Markdown; keep them visible without a migration.
    public static IReadOnlyList<KnowledgePageDto> ExpandPages(AiKnowledgeArtifact artifact, string sourceName)
    {
        List<KnowledgePage>? pages = null;
        try { pages = JsonNode.Parse(artifact.EvidenceJson ?? "{}")?["pages"]?.Deserialize<List<KnowledgePage>>(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        if (pages is not { Count: > 0 } || pages.Any(p => p is null || p.Title is null || p.Markdown is null || p.Evidence is null || p.Evidence.Any(e => e is null || e.Quote is null)))
            return [Map(sourceName, artifact.Content, 0)];
        return pages.Select((page, index) => Map(page.Title,
            page.Markdown + "\n\n## 来源证据\n\n" + string.Join("\n\n", page.Evidence.Select(e =>
                $"文本分段 {e.Part}\n\n> {e.Quote.Replace("\n", "\n> ")}")), index)).ToList();

        KnowledgePageDto Map(string title, string? content, int index) => new() {
            Id = artifact.Id, PageIndex = index, DocumentId = artifact.DocumentId,
            Title = title, SourceName = sourceName, Content = content,
            ReviewStatus = artifact.ReviewStatus, Generator = artifact.GeneratorType,
            Model = artifact.Model, CreatedAt = artifact.CreatedAt
        };
    }

    private AiKnowledgeBase Find(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return db.Queryable<AiKnowledgeBase>().Where(x => x.Name == normalized && !x.IsDeleted).First()
            ?? throw new InvalidOperationException("Knowledge base does not exist.");
    }
}
