using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.ChangeAtlas;

public sealed class AtlasCreateRequest
{
    [JsonPropertyName("repository_id")] public long? RepositoryId { get; set; }
    [JsonPropertyName("days")] public int Days { get; set; } = 7;
    [JsonPropertyName("ref")] public string Ref { get; set; } = "HEAD";
    [JsonPropertyName("refresh_remote")] public bool RefreshRemote { get; set; }
}

public sealed class AtlasSnapshot
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("start_at")] public DateTime StartAt { get; set; }
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("repositories")] public List<AtlasRepository> Repositories { get; set; } = [];
    [JsonPropertyName("modules")] public List<AtlasModule> Modules { get; set; } = [];
    [JsonPropertyName("files")] public List<AtlasFile> Files { get; set; } = [];
    [JsonPropertyName("commits")] public List<AtlasCommit> Commits { get; set; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
    [JsonPropertyName("coverage")] public string Coverage { get; set; } = "directory";
}

public sealed record AtlasRepository(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("head_sha")] string HeadSha,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("refs")] List<string> Refs);
public sealed record AtlasModule(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("repository_id")] long RepositoryId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("directory")] string Directory);
public sealed record AtlasFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("repository_id")] long RepositoryId,
    [property: JsonPropertyName("module_id")] string ModuleId,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("at_head")] bool AtHead);
public sealed record AtlasFileChange(
    [property: JsonPropertyName("file_id")] string FileId,
    [property: JsonPropertyName("added")] int? Added,
    [property: JsonPropertyName("deleted")] int? Deleted);
public sealed record AtlasCommit(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("repository_id")] long RepositoryId,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("parent_sha")] string? ParentSha,
    [property: JsonPropertyName("is_merge")] bool IsMerge,
    [property: JsonPropertyName("author_id")] string AuthorId,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("committed_at")] DateTime CommittedAt,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("files")] List<AtlasFileChange> Files,
    [property: JsonPropertyName("truncated")] bool Truncated);
public sealed record AtlasEvidence(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("file_id")] string FileId,
    [property: JsonPropertyName("commit_id")] string CommitId,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("patch")] string Patch,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("suppressed")] bool Suppressed);

public sealed class AtlasAnalysisRequest
{
    [JsonPropertyName("module_id")] public string ModuleId { get; set; } = "";
    [JsonPropertyName("author_id")] public string? AuthorId { get; set; }
    [JsonPropertyName("until")] public string? Until { get; set; }
    [JsonPropertyName("daily")] public bool Daily { get; set; }
    [JsonPropertyName("perspective")] public string Perspective { get; set; } = "delivery";
    [JsonPropertyName("question")] public string Question { get; set; } = "解释当前模块的近期变化，并提出需要验证的业务场景。";
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }
    [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; set; }
}
public sealed record AtlasClaim(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("evidence_ids")] List<string> EvidenceIds);
public sealed record AtlasAnalysis(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("claims")] List<AtlasClaim> Claims,
    [property: JsonPropertyName("checks")] List<string> Checks,
    [property: JsonPropertyName("unknowns")] List<string> Unknowns,
    [property: JsonPropertyName("module_label")] string? ModuleLabel,
    [property: JsonPropertyName("evidence")] List<AtlasEvidence> Evidence,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);
