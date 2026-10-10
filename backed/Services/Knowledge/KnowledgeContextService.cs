using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Maintains the URI tree while preserving the existing raw and artifact stores.</summary>
public sealed class KnowledgeContextService(ISqlSugarClient database, IHttpContextAccessor? httpContextAccessor = null)
{
    public AiKnowledgeContextNode EnsureKnowledgeBaseRoot(AiKnowledgeBase knowledgeBase)
    {
        var uri = KnowledgeContextUri.Root(knowledgeBase.Name);
        using var db = database.CopyNew();
        var existing = db.Queryable<AiKnowledgeContextNode>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.Uri == uri).First();
        if (existing is not null) return existing;
        var root = new AiKnowledgeContextNode
        {
            KnowledgeBaseId = knowledgeBase.Id,
            Uri = uri,
            Name = knowledgeBase.DisplayName,
            NodeType = "directory",
            Layer = 1,
            Status = "ready",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        root.Id = db.Insertable(root).ExecuteReturnBigIdentity();
        return root;
    }

    public AiKnowledgeContextNode EnsureDocumentNode(AiKnowledgeBase knowledgeBase, AiKnowledgeDocument document)
    {
        var root = EnsureKnowledgeBaseRoot(knowledgeBase);
        var uri = KnowledgeContextUri.Document(document, knowledgeBase.Name);
        using var db = database.CopyNew();
        var existing = db.Queryable<AiKnowledgeContextNode>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.SourceDocumentId == document.Id && x.Layer == 2).First();
        if (existing is not null) return existing;
        var node = new AiKnowledgeContextNode
        {
            KnowledgeBaseId = knowledgeBase.Id,
            ParentId = root.Id,
            Uri = uri,
            Name = document.OriginalFileName,
            NodeType = "document",
            Layer = 2,
            SourceDocumentId = document.Id,
            ContentPath = document.StoragePath,
            ContentHash = document.FileHash,
            Status = document.Status == "error" ? "error" : "ready",
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };
        node.Id = db.Insertable(node).ExecuteReturnBigIdentity();
        return node;
    }

    public AiKnowledgeContextNode EnsureArtifactNode(AiKnowledgeBase knowledgeBase, AiKnowledgeArtifact artifact, string? sourceName)
    {
        var root = EnsureKnowledgeBaseRoot(knowledgeBase);
        var uri = KnowledgeContextUri.Artifact(knowledgeBase.Name, artifact, sourceName);
        using var db = database.CopyNew();
        var existing = db.Queryable<AiKnowledgeContextNode>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && x.ArtifactId == artifact.Id).First();
        if (existing is not null) return existing;
        var node = new AiKnowledgeContextNode
        {
            KnowledgeBaseId = knowledgeBase.Id,
            ParentId = root.Id,
            Uri = uri,
            Name = sourceName is null ? $"knowledge-{artifact.Id}.md" : $"{Path.GetFileNameWithoutExtension(sourceName)}.md",
            NodeType = "artifact",
            Layer = 2,
            ArtifactId = artifact.Id,
            Status = artifact.ReviewStatus == "error" ? "error" : "ready",
            CreatedAt = artifact.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = artifact.CreatedAt
        };
        node.Id = db.Insertable(node).ExecuteReturnBigIdentity();
        return node;
    }

    public List<KnowledgeContextNodeDto> ListTree(AiKnowledgeBase knowledgeBase)
    {
        var root = EnsureKnowledgeBaseRoot(knowledgeBase);
        using var db = database.CopyNew();
        var documents = db.Queryable<AiKnowledgeDocument>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && !x.IsDeleted).ToList();
        foreach (var document in documents) EnsureDocumentNode(knowledgeBase, document);
        var ownerRoot = httpContextAccessor is null ? null : KnowledgeUserScope.Resolve(db, httpContextAccessor);
        var artifacts = db.Queryable<AiKnowledgeArtifact>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id && (x.OwnerRoot == null || x.OwnerRoot == ownerRoot)).ToList();
        foreach (var artifact in artifacts)
        {
            var sourceName = documents.FirstOrDefault(x => x.Id == artifact.DocumentId)?.OriginalFileName;
            EnsureArtifactNode(knowledgeBase, artifact, sourceName);
        }

        var visibleArtifacts = artifacts.Select(x => x.Id).ToHashSet();
        using var refreshed = database.CopyNew();
        return refreshed.Queryable<AiKnowledgeContextNode>()
            .Where(x => x.KnowledgeBaseId == knowledgeBase.Id)
            .OrderBy(x => new { x.Layer, x.Uri })
            .ToList().Where(x => x.ArtifactId is null || visibleArtifacts.Contains(x.ArtifactId.Value)).Select(ToDto).ToList();
    }

    private static KnowledgeContextNodeDto ToDto(AiKnowledgeContextNode node) => new()
    {
        Id = node.Id,
        ParentId = node.ParentId,
        Uri = node.Uri,
        Name = node.Name,
        NodeType = node.NodeType,
        Layer = node.Layer,
        SourceDocumentId = node.SourceDocumentId,
        ArtifactId = node.ArtifactId,
        Status = node.Status,
        ContentHash = node.ContentHash,
        CreatedAt = node.CreatedAt,
        UpdatedAt = node.UpdatedAt
    };
}
