namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Maps validated resource URIs to namespaced raw folders; existing flat paths remain readable.</summary>
public static class KnowledgeResourceRawStore
{
    public static string PathFor(IKnowledgePathService paths, string kbName, string uri)
    {
        var canonical = KnowledgeResourceService.NormalizeUri(uri, uri.EndsWith('/'));
        var relative = canonical["viking://".Length..].TrimEnd('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        foreach (var part in relative)
            if (part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => "<>\"|*".Contains(c)) ||
                System.Text.RegularExpressions.Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException("目录或文件名不能使用操作系统保留名称。");
        var root = System.IO.Path.GetFullPath(paths.GetRawPath(kbName)).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine([root, .. relative]));
        if (!target.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("资源路径超出 raw 目录。");
        for (var current = target; current is not null; current = System.IO.Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("资源路径不能包含符号链接。");
        return target;
    }
}
