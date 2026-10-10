using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiAgent.Backend.Services.Knowledge.Core;

namespace AiAgent.Backend.Services.Knowledge;

public sealed class KnowledgeWorkspaceService(ISqlSugarClient database, IHttpContextAccessor? httpContextAccessor = null)
{
    public static KnowledgeOrganizationDto ReadOrganization(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return new();
        try { return JsonNode.Parse(metadata)?["organization"]?.Deserialize<KnowledgeOrganizationDto>() ?? new(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(); }
    }

    public KnowledgeOrganizationDto SaveOrganization(string name, KnowledgeOrganizationDto value)
    {
        using var db = database.CopyNew();
        value.Company = value.Company?.Trim();
        value.Project = value.Project?.Trim();
        if (value.Company?.Length > 180 || value.Project?.Length > 180)
            throw new ArgumentException("Company and project names must be at most 180 characters.");
        if (!string.IsNullOrEmpty(value.Project) && string.IsNullOrEmpty(value.Company))
            throw new ArgumentException("A project must belong to a company.");
        var kb = Find(db, name);
        var metadata = string.IsNullOrWhiteSpace(kb.MetadataJson) ? new JsonObject() : JsonNode.Parse(kb.MetadataJson)!.AsObject();
        metadata["organization"] = JsonSerializer.SerializeToNode(value);
        kb.MetadataJson = metadata.ToJsonString();
        kb.UpdatedAt = DateTime.UtcNow;
        db.Updateable(kb).UpdateColumns(x => new { x.MetadataJson, x.UpdatedAt }).ExecuteCommand();
        return value;
    }

    public List<KnowledgePageDto> ListPages(string name, string? ownerRoot = null)
    {
        if (ownerRoot is null && httpContextAccessor is not null)
        { using var scopeDb = database.CopyNew(); ownerRoot = KnowledgeUserScope.Resolve(scopeDb, httpContextAccessor); }
        AiKnowledgeBase kb;
        using (var db = database.CopyNew())
            kb = Find(db, name);

        Dictionary<long, AiKnowledgeDocument> documents;
        using (var db = database.CopyNew())
        {
            documents = db.Queryable<AiKnowledgeDocument>().Where(x => x.KnowledgeBaseId == kb.Id && !x.IsDeleted)
                .ToList().ToDictionary(x => x.Id);
        }

        var latestIds = Array.Empty<long>().ToList();
        using (var db = database.CopyNew())
        {
            latestIds = db.Queryable<AiKnowledgeArtifact>().Where(x => x.KnowledgeBaseId == kb.Id && x.DocumentId != null && (x.OwnerRoot == null || x.OwnerRoot == ownerRoot))
                .GroupBy(x => x.DocumentId).Select(x => SqlFunc.AggregateMax(x.Id)).ToList();
        }
        if (latestIds.Count == 0) return [];

        List<AiKnowledgeArtifact> artifacts;
        using (var db = database.CopyNew())
        {
            artifacts = db.Queryable<AiKnowledgeArtifact>().Where(x => latestIds.Contains(x.Id))
                .OrderByDescending(x => x.Id).ToList();
        }
        return artifacts
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

    private static AiKnowledgeBase Find(ISqlSugarClient db, string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return db.Queryable<AiKnowledgeBase>().Where(x => x.Name == normalized && !x.IsDeleted).First()
            ?? throw new InvalidOperationException("Knowledge base does not exist.");
    }
}
