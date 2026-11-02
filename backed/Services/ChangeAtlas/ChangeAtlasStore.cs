using System.Text.Json;
using System.Text.RegularExpressions;
using AiAgent.Backend.Dtos.ChangeAtlas;

namespace AiAgent.Backend.Services.ChangeAtlas;

public sealed class ChangeAtlasStore(IConfiguration configuration)
{
    private readonly string _root = Path.GetFullPath(Path.Combine(configuration["DataPath"] ?? "data", "change-atlas"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Envelope(string OwnerId, AtlasSnapshot Snapshot);
    private static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, "\\A[a-f0-9]{32}\\z", RegexOptions.CultureInvariant)) throw new ArgumentException("无效的地图标识。");
    }
    private string OwnerRoot(string userId) => SafeDirectory(Path.Combine("owners", ChangeAtlasGitReader.Hash(userId)));
    private string SafeDirectory(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("无效的地图目录。");
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (current.Exists && current.LinkTarget != null) throw new InvalidOperationException("地图存储目录不能使用符号链接。");
            current = current.Parent;
        }
        Directory.CreateDirectory(path);
        return path;
    }
    public string Cache(string userId, long repositoryId) => SafeDirectory(Path.Combine("owners", ChangeAtlasGitReader.Hash(userId), "repos", repositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    public string AnalysisWorkspace(string userId, string snapshotId)
    {
        ValidateId(snapshotId);
        return SafeDirectory(Path.Combine("owners", ChangeAtlasGitReader.Hash(userId), "analysis", snapshotId));
    }
    public async Task SaveAsync(string userId, AtlasSnapshot snapshot, CancellationToken token)
    {
        ValidateId(snapshot.Id);
        await AtomicWrite(Path.Combine(OwnerRoot(userId), snapshot.Id + ".json"), new Envelope(userId, snapshot), token);
    }
    public async Task<AtlasSnapshot> ReadAsync(string userId, long projectId, string id, CancellationToken token)
    {
        ValidateId(id);
        var path = Path.Combine(OwnerRoot(userId), id + ".json");
        if (!File.Exists(path)) throw new KeyNotFoundException("地图不存在或不属于当前用户，请重新生成。");
        if (new FileInfo(path).LinkTarget != null || new FileInfo(path).Length > 32_000_000) throw new InvalidOperationException("地图快照无效。");
        var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path, token), Json);
        if (envelope == null || envelope.OwnerId != userId || envelope.Snapshot.ProjectId != projectId) throw new UnauthorizedAccessException("无权读取该地图。");
        if (envelope.Snapshot.CreatedAt < DateTime.UtcNow.AddHours(-24)) throw new KeyNotFoundException("地图已超过 24 小时，请重新生成以读取最新变化。");
        return envelope.Snapshot;
    }
    public async Task<AtlasAnalysis?> ReadAnalysisAsync(string userId, string snapshotId, string key, CancellationToken token)
    {
        ValidateId(snapshotId);
        var path = Path.Combine(AnalysisWorkspace(userId, snapshotId), ChangeAtlasGitReader.Hash(key) + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<AtlasAnalysis>(await File.ReadAllTextAsync(path, token), Json) : null;
    }
    public Task SaveAnalysisAsync(string userId, string snapshotId, string key, AtlasAnalysis value, CancellationToken token) =>
        AtomicWrite(Path.Combine(AnalysisWorkspace(userId, snapshotId), ChangeAtlasGitReader.Hash(key) + ".json"), value, token);
    private static async Task AtomicWrite<T>(string path, T value, CancellationToken token)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, Json);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 32_000_000) throw new InvalidOperationException("地图数据过大，请选择单个仓库或缩小时间范围。");
            await File.WriteAllTextAsync(temp, json, token);
            token.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
