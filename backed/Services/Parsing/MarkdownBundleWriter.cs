using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Parsing;

public sealed record MarkdownBundleSection(string Path, int Order, int SourceStart, int SourceEnd, int RepeatedHeaderCharacters, int EstimatedTokens, string Hash);
public sealed record MarkdownBundleNavigation(string Path, string Hash);
public sealed record MarkdownBundleOutput(List<MarkdownBundleSection> Files, List<MarkdownBundleNavigation> Navigation);

/// <summary>Materializes a planned document tree; callers own staging, source authorization and publication.</summary>
public static class MarkdownBundleWriter
{
    public const string IndexName = "_index.md";

    public static async Task<MarkdownBundleOutput> WriteAsync(string output, string content, MarkdownDocumentPlan plan,
        IReadOnlyList<string> assetPaths, CancellationToken ct)
    {
        var next = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in plan.Sections)
        {
            ct.ThrowIfCancellationRequested();
            if (section.SourceStart != next || section.SourceEnd <= next || section.SourceEnd > content.Length ||
                section.Content != section.RepeatedHeader + content[section.SourceStart..section.SourceEnd] || !paths.Add(section.RelativePath) ||
                Path.GetFileName(section.RelativePath).Equals(IndexName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("解析写入计划未完整覆盖正文，或包含重复／保留路径。");
            next = section.SourceEnd;
        }
        if (next != content.Length || plan.Sections.Count is 0 or > 10000) throw new InvalidOperationException("解析计划为空、超限或未完整覆盖正文。");
        var sections = new List<MarkdownBundleSection>();
        foreach (var (section, order) in plan.Sections.Select((value, index) => (value, index)))
        {
            var target = Target(output, "sections/" + section.RelativePath);
            var text = RewriteAssetLinks(section.Content, assetPaths, asset =>
                Path.GetRelativePath(Path.GetDirectoryName(target)!, Target(output, asset)).Replace('\\', '/'));
            await WriteNew(target, text, ct);
            sections.Add(new(section.RelativePath, order, section.SourceStart, section.SourceEnd, section.RepeatedHeader.Length,
                MarkdownDocumentPlanner.EstimateTokens(section.Content), Hash(text)));
        }
        var navigation = new List<MarkdownBundleNavigation>();
        foreach (var entry in Navigation(plan))
        {
            await WriteNew(Target(output, "sections/" + entry.Path), entry.Content, ct);
            navigation.Add(new(entry.Path, Hash(entry.Content)));
        }
        return new(sections, navigation);
    }

    private static IEnumerable<(string Path, string Content)> Navigation(MarkdownDocumentPlan plan)
    {
        // Every directory lists only its immediate children, in source order. No quadratic all-descendant index.
        var directories = new Dictionary<string, List<string>>(StringComparer.Ordinal) { [""] = [] };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in plan.Sections)
        {
            var parts = section.RelativePath.Split('/'); var parent = "";
            for (var i = 0; i < parts.Length; i++)
            {
                var child = parent + parts[i] + (i < parts.Length - 1 ? "/" : "");
                if (seen.Add(child)) directories[parent].Add(child);
                if (i < parts.Length - 1) { directories.TryAdd(child, []); parent = child; }
            }
        }
        foreach (var (directory, children) in directories)
        {
            var title = directory.Length == 0 ? plan.RootName : directory.TrimEnd('/').Split('/').Last();
            var text = new StringBuilder("# " + Escape(title) + "\n\n");
            if (directory.Length > 0) text.AppendLine($"[上级目录](../{IndexName})\n");
            foreach (var child in children)
            {
                var name = child[directory.Length..].TrimEnd('/');
                var relative = child[directory.Length..] + (child.EndsWith('/') ? IndexName : "");
                var href = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
                text.AppendLine($"- [{Escape(Path.GetFileNameWithoutExtension(name))}](./{href})");
            }
            yield return (directory + IndexName, text.ToString());
        }
    }

    public static string RewriteNavigationLinks(string content, string currentPath, IEnumerable<string> knownPaths, Func<string, string> resolve)
    {
        var known = knownPaths.ToHashSet(StringComparer.Ordinal);
        var baseUri = new Uri("https://markdown.invalid/" + string.Join('/', currentPath.Split('/').Select(Uri.EscapeDataString)));
        return Regex.Replace(content, @"(?<!!)\[([^\]\r\n]*)\]\(([^)\r\n]+)\)", match => {
            if (!Uri.TryCreate(baseUri, match.Groups[2].Value, out var target) || target.Host != baseUri.Host) return match.Value;
            var path = Uri.UnescapeDataString(target.AbsolutePath.TrimStart('/'));
            return known.Contains(path) ? $"[{match.Groups[1].Value}]({resolve(path)})" : match.Value;
        });
    }

    public static string RewriteAssetLinks(string text, IReadOnlyList<string> paths, Func<string, string> resolve)
    {
        var known = paths.ToHashSet(StringComparer.Ordinal);
        return Regex.Replace(text, @"!\[([^\]\r\n]*)\]\(([^)\r\n]+)\)", match => {
            var path = match.Groups[2].Value;
            while (path.StartsWith("../", StringComparison.Ordinal)) path = path[3..];
            return known.Contains(path) ? $"![{match.Groups[1].Value}]({resolve(path)})" : match.Value;
        });
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`");
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Target(string output, string relative)
    {
        if (relative.Contains('\\') || relative.Split('/').Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.Length > 80 || p.EndsWith('.') || p.EndsWith(' ') ||
            p.Any(c => char.IsControl(c) || "<>:\"|?*#%".Contains(c)) || Regex.IsMatch(p.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Markdown 产物路径无效。");
        var root = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(output, relative));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new UnauthorizedAccessException("Markdown 产物超出输出目录。");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Markdown 产物路径不能包含链接。");
        return path;
    }

    private static async Task WriteNew(string target, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
    }
}
