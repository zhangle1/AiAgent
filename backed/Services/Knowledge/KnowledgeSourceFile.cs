using AiAgent.Backend.Entities.Knowledge;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Checks stored paths without opening content or trusting paths from old installations.</summary>
public static class KnowledgeSourceFile
{
    public sealed record State(string Status, string? Path, string? Message)
    {
        public bool Available => Status == "available";
        public bool CanClean => !Available && Status != "unreadable";
    }

    public static State Inspect(IKnowledgePathService paths, AiKnowledgeBase kb, AiKnowledgeDocument document)
    {
        var root = System.IO.Path.GetFullPath(paths.GetKnowledgeBasePath(kb.Name))
            .TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        try
        {
            if (string.IsNullOrWhiteSpace(document.StoragePath) || !System.IO.Path.IsPathFullyQualified(document.StoragePath))
                return new("invalid_path", null, "历史记录的原文件路径无效，可清理此失效记录后重新导入。");
            var path = System.IO.Path.GetFullPath(document.StoragePath);
            if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return new("outside_root", null, "历史记录指向当前存储目录之外，可能尚未迁移。可迁移资料，或清理此失效记录后重新导入。");

            FileSystemInfo? current = new FileInfo(path);
            var exists = false;
            while (current is not null)
            {
                FileAttributes attributes;
                try { attributes = System.IO.File.GetAttributes(current.FullName); }
                catch (FileNotFoundException) { attributes = (FileAttributes)(-1); }
                catch (DirectoryNotFoundException) { attributes = (FileAttributes)(-1); }
                if (current is FileInfo) exists = attributes != (FileAttributes)(-1) && (attributes & FileAttributes.Directory) == 0;
                // Missing files are not reparse points; access errors must remain distinct from missing data.
                if (attributes != (FileAttributes)(-1) && (attributes & FileAttributes.ReparsePoint) != 0)
                    return new("unsafe_path", null, "原文件路径包含符号链接，无法作为受控知识资料读取。");
                current = current is FileInfo file ? file.Directory : (current as DirectoryInfo)?.Parent;
            }
            return exists
                ? new("available", path, null)
                : new("missing", null, "原文件已不存在，可清理此失效记录后重新导入。");
        }
        catch (ArgumentException) { return new("invalid_path", null, "历史记录的原文件路径无效，可清理此失效记录后重新导入。"); }
        catch (NotSupportedException) { return new("invalid_path", null, "历史记录的原文件路径无效，可清理此失效记录后重新导入。"); }
        catch (PathTooLongException) { return new("invalid_path", null, "历史记录的原文件路径过长，可清理此失效记录后重新导入。"); }
        catch (UnauthorizedAccessException) { return new("unreadable", null, "暂时无法检查原文件，请先检查服务账号的目录权限。"); }
        catch (IOException) { return new("unreadable", null, "暂时无法检查原文件，请先检查存储设备是否可用。"); }
    }
}
