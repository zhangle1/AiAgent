using AiAgent.Backend.Services.Knowledge;

namespace AiAgent.Backend.Services.Parsing;

internal static class DocumentConversionPaths
{
    internal static string Validate(IConfiguration configuration, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException("文档路径必须位于受控存储目录。");
        var target = Path.GetFullPath(path);
        var roots = configuration.GetSection("DocumentParsing:AllowedRoots").Get<string[]>()
            ?? configuration.GetSection("PythonWorkers:AllowedRoots").Get<string[]>() ?? ["data"];
        var data = configuration["DataPath"] ?? "data";
        var attachments = configuration["ChatAttachments:RootPath"];
        var resolved = roots.Where(x => !string.IsNullOrWhiteSpace(x)).SelectMany(x => Path.IsPathFullyQualified(x)
                ? new[] { Path.GetFullPath(x) } : new[] { Path.GetFullPath(x), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, x)) })
            .Append(KnowledgePathService.ResolveRootPath(configuration))
            .Append(Path.GetFullPath(string.IsNullOrWhiteSpace(attachments) ? Path.Combine(data, "chat_attachments", "files") : Path.Combine(attachments, "files")));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolved.Any(root => (target + Path.DirectorySeparatorChar).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)))
            throw new UnauthorizedAccessException("文档路径位于允许的存储目录之外。");
        return RequireNoLinks(target);
    }

    internal static string RequireNoLinks(string target)
    {
        for (string? current = target; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("文档路径不能包含符号链接。");
        return target;
    }
}
