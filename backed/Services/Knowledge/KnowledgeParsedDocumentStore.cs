using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Parsing;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>A database row publishes one complete immutable parse generation, never a partially written layout.</summary>
public sealed class KnowledgeParsedDocumentStore(IKnowledgePathService paths)
{
    public sealed record Entry(string Path, int Order, int SourceStart, int SourceEnd, int RepeatedHeaderCharacters, int EstimatedTokens, string Hash);
    public sealed record Asset(string Path, string Hash, int Page, string Kind);
    public sealed record Manifest(string Version, string RootName, string ContentHash, MarkdownParsingOptions Options, List<Entry> Files, List<Asset>? Assets = null, List<MarkdownBundleNavigation>? Navigation = null);
    public sealed record Published(string ContentPath, string ManifestPath, Manifest Layout);

    public string NewStaging(AiKnowledgeBase kb, long documentId)
    {
        var directory = Guard(Path.Combine(paths.GetKnowledgeBasePath(kb.Name), "parsed", documentId.ToString(), ".staging-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory); return directory;
    }

    public async Task<Published> PublishAsync(string staging, string content, MarkdownDocumentPlan plan, MarkdownParsingOptions options, CancellationToken ct,
        IReadOnlyList<DocumentParseAsset>? sourceAssets = null, string? assetSourceRoot = null)
    {
        staging = Guard(staging);
        if (!Path.GetFileName(staging).StartsWith(".staging-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid parsing staging directory.");
        var assets = new List<Asset>();
        long assetBytes = 0;
        foreach (var asset in sourceAssets ?? [])
        {
            ct.ThrowIfCancellationRequested(); ValidateRelative(asset.Path);
            if (!asset.Path.StartsWith("assets/", StringComparison.Ordinal) || !asset.Path.EndsWith(".png", StringComparison.Ordinal) ||
                assets.Count >= 5000 || assets.Any(a => a.Path == asset.Path)) throw new InvalidOperationException("Invalid parsed image manifest.");
            var source = Guard(Path.Combine(assetSourceRoot ?? throw new InvalidOperationException("Missing image source root."), asset.Path));
            var length = new FileInfo(source).Length;
            if (length > 32 * 1024 * 1024 || (assetBytes += length) > 256 * 1024 * 1024) throw new InvalidOperationException("Parsed images exceed size limits.");
            var bytes = await File.ReadAllBytesAsync(source, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || hash != asset.Sha256)
                throw new InvalidOperationException("Parsed image validation failed.");
            var target = Guard(Path.Combine(staging, asset.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes, ct);
            assets.Add(new(asset.Path, hash, asset.Page, asset.Kind));
        }
        var contentPath = Guard(Path.Combine(staging, "document.md"));
        await File.WriteAllTextAsync(contentPath, content, new UTF8Encoding(false), ct);
        var output = await MarkdownBundleWriter.WriteAsync(staging, content, plan, assets.Select(a => a.Path).ToList(), ct);
        var entries = output.Files.Select(e => new Entry(e.Path, e.Order, e.SourceStart, e.SourceEnd, e.RepeatedHeaderCharacters, e.EstimatedTokens, e.Hash)).ToList();
        var manifest = new Manifest(MarkdownDocumentPlanner.Version, plan.RootName, KnowledgeSemanticStore.Hash(content), options, entries, assets, output.Navigation);
        await File.WriteAllTextAsync(Guard(Path.Combine(staging, "layout.json")), JsonSerializer.Serialize(manifest), new UTF8Encoding(false), ct);
        ct.ThrowIfCancellationRequested();
        var published = Guard(Path.Combine(Path.GetDirectoryName(staging)!, Guid.NewGuid().ToString("N")));
        Directory.Move(staging, published);
        return new(Path.Combine(published, "document.md"), Path.Combine(published, "layout.json"), manifest);
    }

    public Manifest? ReadManifest(AiKnowledgeBase kb, AiKnowledgeParsedDocument row)
    {
        if (!MarkdownDocumentPlanner.SupportsVersion(row.ParserVersion) || string.IsNullOrWhiteSpace(row.ContentPath)) return null;
        try
        {
            var root = GenerationRoot(kb, row);
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Guard(Path.Combine(root, "layout.json")), Encoding.UTF8));
            if (manifest is null || manifest.Version != row.ParserVersion || manifest.ContentHash != row.ContentHash ||
                manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Any(f => f is null) || manifest.Options is null || string.IsNullOrWhiteSpace(manifest.RootName)) return null;
            manifest.Options.Validate();
            var assetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in manifest.Assets ?? [])
            {
                if (asset is null) return null;
                ValidateRelative(asset.Path);
                if (!asset.Path.StartsWith("assets/", StringComparison.Ordinal) || !asset.Path.EndsWith(".png", StringComparison.Ordinal) || !assetPaths.Add(asset.Path)) return null;
            }
            var next = 0; var pathsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files.OrderBy(f => f.Order))
            {
                if (entry is null) return null;
                ValidateRelative(entry.Path);
                if (entry.SourceStart != next || entry.SourceEnd <= next || entry.RepeatedHeaderCharacters < 0 || !pathsSeen.Add(entry.Path)) return null;
                next = entry.SourceEnd;
            }
            foreach (var index in manifest.Navigation ?? [])
            {
                if (index is null) return null;
                ValidateRelative(index.Path);
                if (Path.GetFileName(index.Path) != MarkdownBundleWriter.IndexName || !pathsSeen.Add(index.Path)) return null;
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

    public string NavigationPath(AiKnowledgeBase kb, AiKnowledgeParsedDocument row, MarkdownBundleNavigation index)
    {
        ValidateRelative(index.Path);
        var path = Guard(Path.Combine(GenerationRoot(kb, row), "sections", index.Path));
        if (!File.Exists(path) || MarkdownBundleWriter.Hash(File.ReadAllText(path, Encoding.UTF8)) != index.Hash)
            throw new InvalidOperationException("解析目录索引缺失或校验失败，请重新解析。");
        return path;
    }

    public string AssetPath(AiKnowledgeBase kb, AiKnowledgeParsedDocument row, Asset asset)
    {
        ValidateRelative(asset.Path);
        var path = Guard(Path.Combine(GenerationRoot(kb, row), asset.Path));
        if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() != asset.Hash)
            throw new InvalidOperationException("解析图片缺失或校验失败，请重新解析。");
        return path;
    }

    public static string RewriteAssetLinks(string text, IReadOnlyList<Asset> assets, Func<Asset, string> resolve) =>
        Regex.Replace(text, @"!\[([^\]\r\n]*)\]\(([^)\r\n]+)\)", match => {
            var path = match.Groups[2].Value;
            while (path.StartsWith("../", StringComparison.Ordinal)) path = path[3..];
            var asset = assets.FirstOrDefault(a => a.Path == path);
            return asset is null ? match.Value : $"![{match.Groups[1].Value}]({resolve(asset)})";
        });

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
