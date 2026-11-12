using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.ChangeAtlas;

public sealed record AtlasImportEdge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("text")] string Text);
public sealed record AtlasImportGraph(
    [property: JsonPropertyName("snapshot_id")] string SnapshotId,
    [property: JsonPropertyName("head_sha")] string HeadSha,
    [property: JsonPropertyName("files")] List<AtlasFile> Files,
    [property: JsonPropertyName("edges")] List<AtlasImportEdge> Edges,
    [property: JsonPropertyName("scanned")] int Scanned,
    [property: JsonPropertyName("skipped")] int Skipped,
    [property: JsonPropertyName("unresolved")] int Unresolved,
    [property: JsonPropertyName("warnings")] string[] Warnings);
