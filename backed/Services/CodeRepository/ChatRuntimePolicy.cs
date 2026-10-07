using AiAgent.Backend.Dtos.CodeRepository;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.CodeRepository;

public static class ChatRuntimePolicy
{
    // Reject junctions/symlinks at every segment, including the repository itself.
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 500 || relative.Contains(':') || relative.Contains('\\') || relative.Any(char.IsControl)
            || Path.IsPathRooted(relative) || relative.Split('/').Any(x => x is "" or "." or ".."))
            throw new ArgumentException("Expected a repository-relative path without traversal.");
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Path leaves the repository.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Linked runtime paths are not allowed.");
        return full;
    }

    public static void ValidateTarget(ChatRuntimeTarget target)
    {
        if (target.Role is not ("frontend" or "backend")) throw new ArgumentException("Role must be frontend or backend.");
        if (target.PreferredPort is null or < 1024 or > 65535) throw new ArgumentException("An explicit port between 1024 and 65535 is required.");
        ValidatePagePath(target.HealthPath);
        ValidatePagePath(target.PagePath);
        if (target.Environment is null || target.Environment.Count > 30) throw new ArgumentException("Too many environment overrides.");
        foreach (var pair in target.Environment)
        {
            if (!Regex.IsMatch(pair.Key, @"^(NEXT_PUBLIC_[A-Z0-9_]+|VITE_[A-Z0-9_]+|REACT_APP_[A-Z0-9_]+|API_BASE_URL|ASPNETCORE_ENVIRONMENT|DOTNET_ENVIRONMENT)$")
                || pair.Value is null || pair.Value.Length > 2048 || pair.Value.Any(char.IsControl))
                throw new ArgumentException("Only public application URL/configuration environment overrides are supported; do not include secrets.");
        }
    }

    public static void ValidatePagePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 512 || !path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\') || path.Contains('#') || path.Any(char.IsControl))
            throw new ArgumentException("Page/health path must be a local absolute URL path.");
    }
}
