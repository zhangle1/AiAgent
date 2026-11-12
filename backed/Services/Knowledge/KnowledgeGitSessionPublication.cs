using System.Text.Json.Serialization;
using System.Text;

namespace AiAgent.Backend.Services.Knowledge;

internal sealed class KnowledgeGitExportEntry
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "file";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("project_name")] public string? ProjectName { get; set; }
}

internal static class KnowledgeGitSessionPublication
{
    public static string ExportPath(string uri, long? projectId = null, string? projectName = null)
    {
        var parts = LegacyExportPath(uri).Split(Path.DirectorySeparatorChar);
        var folder = projectId is > 0 ? $"{SafeProjectName(projectName)}-{projectId}" : "未分配项目";
        return Path.Combine(parts[0], parts[1], folder, Path.Combine(parts.Skip(2).ToArray()));
    }

    private static string SafeProjectName(string? name)
    {
        // Bound UTF-8 length and replace Windows separators; the ID keeps duplicate names distinct.
        var safe = string.Concat((name ?? "项目").EnumerateRunes().Take(48)
            .Select(rune => Rune.IsControl(rune) || "<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal) ? "_" : rune.ToString())).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "项目" : safe;
    }

    public static string LegacyExportPath(string uri)
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
