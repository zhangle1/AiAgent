using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Immutable sidecar generations, published together by one atomic manifest replacement.</summary>
public sealed class KnowledgeSemanticStore(IKnowledgePathService paths)
{
    public sealed record Snapshot(string Fingerprint, string Overview, string Abstract, string? Model, DateTime GeneratedAt);
    private sealed record Manifest(string Version, string Fingerprint, string? Model, DateTime GeneratedAt);

    public Snapshot? Read(string uri, string fingerprint)
    {
        // Semantics are rebuildable. A damaged or incomplete generation must not
        // prevent the resource API from returning the original and parsed text.
        try { return ReadSnapshot(uri, fingerprint); }
        catch (JsonException) { return null; }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private Snapshot? ReadSnapshot(string uri, string fingerprint)
    {
        var root = Location(uri);
        var manifestPath = Guard(Path.Combine(root, "current.json"));
        if (!File.Exists(manifestPath)) return null;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
        if (manifest is null || manifest.Fingerprint != fingerprint || !Guid.TryParseExact(manifest.Version, "N", out _)) return null;
        var version = Path.Combine(root, manifest.Version);
        var overview = Guard(Path.Combine(version, ".overview.md"));
        var abstractPath = Guard(Path.Combine(version, ".abstract.md"));
        if (!File.Exists(overview) || !File.Exists(abstractPath)) return null;
        var overviewText = File.ReadAllText(overview);
        var abstractText = File.ReadAllText(abstractPath);
        if (string.IsNullOrWhiteSpace(overviewText) || string.IsNullOrWhiteSpace(abstractText)) return null;
        return new(fingerprint, overviewText, abstractText, manifest.Model, manifest.GeneratedAt);
    }

    public async Task SaveAsync(string uri, Snapshot snapshot, CancellationToken ct)
    {
        if (Read(uri, snapshot.Fingerprint) is not null) return;
        var root = Location(uri);
        var version = Guid.NewGuid().ToString("N");
        var directory = Guard(Path.Combine(root, version));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Guard(Path.Combine(directory, ".overview.md")), snapshot.Overview, Encoding.UTF8, ct);
        await File.WriteAllTextAsync(Guard(Path.Combine(directory, ".abstract.md")), snapshot.Abstract, Encoding.UTF8, ct);
        var temporary = Guard(Path.Combine(root, version + ".tmp"));
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Manifest(version, snapshot.Fingerprint, snapshot.Model, snapshot.GeneratedAt)), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, Guard(Path.Combine(root, "current.json")), true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string Location(string uri) => Guard(Path.Combine(paths.RootPath, ".resource-semantic", Hash(uri)));
    private string Guard(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(paths.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new UnauthorizedAccessException("语义文件超出存储范围。");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("语义存储路径不能包含符号链接。");
        return full;
    }
}
