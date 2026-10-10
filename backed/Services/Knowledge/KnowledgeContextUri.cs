using AiAgent.Backend.Entities.Knowledge;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Builds the public, stable URI surface of the Viking-style context tree.</summary>
public static partial class KnowledgeContextUri
{
    private const string Scheme = "viking://resources/";

    [GeneratedRegex("[^a-zA-Z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeSegmentRegex();

    public static string Root(string knowledgeBaseName) => $"{Scheme}{Segment(knowledgeBaseName)}/";

    public static string Document(AiKnowledgeDocument document, string knowledgeBaseName)
    {
        if (!string.IsNullOrWhiteSpace(document.ResourceUri)) return document.ResourceUri;
        var extension = Path.GetExtension(document.OriginalFileName ?? document.FileName);
        var stem = Path.GetFileNameWithoutExtension(document.OriginalFileName ?? document.FileName);
        return $"{Root(knowledgeBaseName)}{Segment(stem)}-{document.Id}{extension.ToLowerInvariant()}";
    }

    public static string Artifact(string knowledgeBaseName, AiKnowledgeArtifact artifact, string? sourceName)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceName ?? "knowledge");
        var root = artifact.OwnerRoot is null ? Root(knowledgeBaseName) + "knowledge/" : artifact.OwnerRoot + "wiki/";
        return $"{root}{Segment(stem)}-{artifact.Id}.md";
    }

    public static string Segment(string? value)
    {
        var normalized = UnsafeSegmentRegex().Replace((value ?? "").Trim(), "-").Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(normalized) ? "resource" : normalized[..Math.Min(160, normalized.Length)];
    }
}
