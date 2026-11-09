using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Knowledge;

internal sealed class KnowledgeGitExportEntry
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "file";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
}

internal static class KnowledgeGitSessionPublication
{
    public static string ExportPath(string uri)
    {
        uri = KnowledgeResourceService.NormalizeUri(uri);
        if (!KnowledgeUserScope.IsSessionScope(uri) || uri.EndsWith('/') || KnowledgeResourceService.IsGeneratedUri(uri)
            || !uri.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择会话 Markdown 原文。");
        var parts = uri["viking://user/".Length..].Split('/');
        if (parts.Length < 3) throw new ArgumentException("请选择会话文件。");
        // Reuse shared path encoding (Windows device names, hidden names and reserved characters).
        var mapped = "viking://resources/" + parts[0] + "/" + string.Join('/', parts.Skip(2));
        var relative = KnowledgeTeamGitService.ExportPath(mapped);
        return Path.Combine("sessions", relative["resources".Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
