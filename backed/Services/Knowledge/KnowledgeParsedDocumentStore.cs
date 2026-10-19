using System.Text;
using System.Text.Json;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Parsing;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>A database row publishes one complete immutable parse generation, never a partially written layout.</summary>
public sealed class KnowledgeParsedDocumentStore(IKnowledgePathService paths)
{
    public sealed record Entry(string Path, int Order, int SourceStart, int SourceEnd, int RepeatedHeaderCharacters, int EstimatedTokens, string Hash);
    public sealed record Manifest(string Version, string RootName, string ContentHash, MarkdownParsingOptions Options, List<Entry> Files);
    public sealed record Published(string ContentPath, string ManifestPath, Manifest Layout);

    public string NewStaging(AiKnowledgeBase kb, long documentId)
    {
        var directory = Guard(Path.Combine(paths.GetKnowledgeBasePath(kb.Name), "parsed", documentId.ToString(), ".staging-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory); return directory;
    }

    public async Task<Published> PublishAsync(string staging, string content, MarkdownDocumentPlan plan, MarkdownParsingOptions options, CancellationToken ct)
    {
        staging = Guard(staging);
        if (!Path.GetFileName(staging).StartsWith(".staging-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid parsing staging directory.");
        var entries = new List<Entry>();
        var next = 0;
        foreach (var section in plan.Sections)
        {
            if (section.SourceStart != next || section.SourceEnd <= next || section.SourceEnd > content.Length ||
                section.Content != section.RepeatedHeader + content[section.SourceStart..section.SourceEnd])
                throw new InvalidOperationException("解析写入计划未完整覆盖正文。");
            next = section.SourceEnd;
        }
        if (next != content.Length || plan.Sections.Count == 0) throw new InvalidOperationException("解析写入计划未完整覆盖正文。");
        var contentPath = Guard(Path.Combine(staging, "document.md"));
        await File.WriteAllTextAsync(contentPath, content, new UTF8Encoding(false), ct);
        foreach (var (section, index) in plan.Sections.Select((s, i) => (s, i)))
        {
            ct.ThrowIfCancellationRequested(); ValidateRelative(section.RelativePath);
            var target = Guard(Path.Combine(staging, "sections", section.RelativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, section.Content, new UTF8Encoding(false), ct);
            entries.Add(new(section.RelativePath, index, section.SourceStart, section.SourceEnd, section.RepeatedHeader.Length,
                MarkdownDocumentPlanner.EstimateTokens(section.Content), KnowledgeSemanticStore.Hash(section.Content)));
        }
        var manifest = new Manifest(MarkdownDocumentPlanner.Version, plan.RootName, KnowledgeSemanticStore.Hash(content), options, entries);
        await File.WriteAllTextAsync(Guard(Path.Combine(staging, "layout.json")), JsonSerializer.Serialize(manifest), new UTF8Encoding(false), ct);
        ct.ThrowIfCancellationRequested();
        var published = Guard(Path.Combine(Path.GetDirectoryName(staging)!, Guid.NewGuid().ToString("N")));
        Directory.Move(staging, published);
        return new(Path.Combine(published, "document.md"), Path.Combine(published, "layout.json"), manifest);
    }

    public Manifest? ReadManifest(AiKnowledgeBase kb, AiKnowledgeParsedDocument row)
    {
        if (row.ParserVersion != MarkdownDocumentPlanner.Version || string.IsNullOrWhiteSpace(row.ContentPath)) return null;
        try
        {
            var root = GenerationRoot(kb, row);
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Guard(Path.Combine(root, "layout.json")), Encoding.UTF8));
            if (manifest is null || manifest.Version != MarkdownDocumentPlanner.Version || manifest.ContentHash != row.ContentHash ||
                manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Any(f => f is null) || manifest.Options is null || string.IsNullOrWhiteSpace(manifest.RootName)) return null;
            manifest.Options.Validate();
            var next = 0; var pathsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files.OrderBy(f => f.Order))
            {
                if (entry is null) return null;
                ValidateRelative(entry.Path);
                if (entry.SourceStart != next || entry.SourceEnd <= next || entry.RepeatedHeaderCharacters < 0 || !pathsSeen.Add(entry.Path)) return null;
                next = entry.SourceEnd;
            }
            return next == row.CharacterCount ? manifest : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }

    public string SectionPath(AiKnowledgeBase kb, AiKnowledgeParsedDocument row, Entry entry)
    {
        ValidateRelative(entry.Path);
        var path = Guard(Path.Combine(GenerationRoot(kb, row), "sections", entry.Path));
        if (!File.Exists(path)) throw new FileNotFoundException("解析章节文件缺失，请重新解析。");
        if (KnowledgeSemanticStore.Hash(File.ReadAllText(path, Encoding.UTF8)) != entry.Hash) throw new InvalidOperationException("解析章节校验失败，请重新解析。");
        return path;
    }

    public void DiscardStaging(string staging)
    {
        staging = Guard(staging);
        if (!Path.GetFileName(staging).StartsWith(".staging-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid parsing staging directory.");
        if (!Directory.Exists(staging)) return;
        // Only the staging directory created by this parse is removable; reject links anywhere in it.
        CheckChildren(staging); Directory.Delete(staging, true);
        void CheckChildren(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                Guard(entry);
                if (Directory.Exists(entry)) CheckChildren(entry);
            }
        }
    }

    private string GenerationRoot(AiKnowledgeBase kb, AiKnowledgeParsedDocument row)
    {
        var path = Guard(row.ContentPath!);
        var root = Guard(Path.Combine(paths.GetKnowledgeBasePath(kb.Name), "parsed", row.DocumentId!.Value.ToString())) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new UnauthorizedAccessException("解析版本超出所属文档范围。");
        var generation = Path.GetDirectoryName(path)!;
        if (!Guid.TryParseExact(Path.GetFileName(generation), "N", out _) || Path.GetFileName(path) != "document.md") throw new InvalidOperationException("Invalid parsing generation.");
        return generation;
    }
    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Split('/').Any(p => p is "" or "." or ".." || p.Length > 80 ||
            p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => char.IsControl(c) || "<>:\"|?*#%".Contains(c)) ||
            System.Text.RegularExpressions.Regex.IsMatch(p.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new ArgumentException("Invalid parsed section path.");
    }
    private string Guard(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(paths.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new UnauthorizedAccessException("解析路径超出知识存储范围。");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("解析路径不能包含符号链接。");
        return full;
    }
}
