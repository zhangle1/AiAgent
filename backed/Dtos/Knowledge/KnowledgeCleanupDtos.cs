using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class KnowledgeCleanupItem
{
    [JsonPropertyName("document_id")] public long DocumentId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("can_cleanup")] public bool CanCleanup { get; set; }
    [JsonPropertyName("blocked_reason")] public string? BlockedReason { get; set; }
}

public sealed class KnowledgeCleanupPreview
{
    [JsonPropertyName("items")] public List<KnowledgeCleanupItem> Items { get; set; } = [];
    [JsonPropertyName("snapshot_sha256")] public string SnapshotSha256 { get; set; } = "";
    [JsonPropertyName("includes_shared")] public bool IncludesShared { get; set; }
}

public sealed class KnowledgeCleanupRequest
{
    [JsonPropertyName("document_ids")] public List<long> DocumentIds { get; set; } = [];
    [JsonPropertyName("snapshot_sha256")] public string SnapshotSha256 { get; set; } = "";
}

public sealed class KnowledgeCleanupResult
{
    [JsonPropertyName("removed_count")] public int RemovedCount { get; set; }
}
