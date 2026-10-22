using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiAgent.Backend.Dtos.KunBuddy;
using AiAgent.Backend.Services.Auth;

namespace AiAgent.Backend.Services.KunBuddy;

/// <summary>Immutable installer files and an atomically published catalog. Never runs uploaded code.</summary>
public sealed class ClientReleaseService(IHostEnvironment environment, IConfiguration configuration)
{
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _root = Path.Combine(Path.GetFullPath(string.IsNullOrWhiteSpace(configuration["DataPath"]) ? "data" : configuration["DataPath"]!, environment.ContentRootPath), "client-releases");
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static Version ParseVersion(string version) => Regex.IsMatch(version ?? "", @"^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$") ? Version.Parse(version!) : throw new ArgumentException("版本号请填写 major.minor.patch，例如 0.20.2。");
    public static void ValidateTarget(string platform, string architecture)
    {
        if (platform is not ("windows" or "macos") || architecture is not ("x64" or "arm64")) throw new ArgumentException("仅支持 Windows / macOS 的 x64 或 arm64 安装包。");
    }
    private string CatalogPath => Path.Combine(_root, "catalog.json");
    private List<ClientReleaseDto> Load()
    {
        for (var current = new DirectoryInfo(_root); current is not null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Release storage cannot use symbolic links.");
        if (File.Exists(CatalogPath) && File.GetAttributes(CatalogPath).HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException();
        return File.Exists(CatalogPath) ? JsonSerializer.Deserialize<List<ClientReleaseDto>>(File.ReadAllText(CatalogPath)) ?? [] : [];
    }
    private async Task Save(List<ClientReleaseDto> rows, CancellationToken ct)
    {
        Directory.CreateDirectory(_root); var temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(rows), ct);
        ct.ThrowIfCancellationRequested(); File.Move(temporary, CatalogPath, true);
    }
    public List<ClientReleaseDto> List()
    {
        _gate.Wait(); try { return Load().OrderByDescending(x => ParseVersion(x.Version)).ThenByDescending(x => x.CreatedAt).ToList(); } finally { _gate.Release(); }
    }
    public ClientReleaseDto? Latest(string platform, string architecture)
    {
        ValidateTarget(platform, architecture); return List().FirstOrDefault(x => x.Published && x.Platform == platform && x.Architecture == architecture);
    }
    public async Task<ClientReleaseDto> Upload(AuthenticatedUser administrator, IFormFile file, string version, string platform, string architecture, string? notes, CancellationToken ct)
    {
        if (!administrator.IsAdministrator) throw new UnauthorizedAccessException();
        ParseVersion(version); ValidateTarget(platform, architecture);
        if (file is null || file.Length < 64 || file.Length > MaxBytes) throw new ArgumentException("安装包大小须为 64 字节至 2 GiB。");
        if ((notes?.Length ?? 0) > 10000) throw new ArgumentException("更新说明最多 10000 字符。");
        var filename = file.FileName.Replace('\\', '/').Split('/').Last(); var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (filename.Length > 200 || filename.Any(char.IsControl) || (platform == "windows" ? extension != ".exe" : extension is not (".dmg" or ".zip"))) throw new ArgumentException("Windows 请上传 EXE，macOS 请上传 DMG 或应用 ZIP。");
        await _gate.WaitAsync(ct);
        try
        {
            var catalog = Load();
            if (catalog.Any(x => x.Version == version && x.Platform == platform && x.Architecture == architecture)) throw new InvalidOperationException("该平台和架构的版本已存在，请递增版本号。");
            Directory.CreateDirectory(_root); var id = Guid.NewGuid().ToString("N"); var temporary = Path.Combine(_root, id + ".tmp"); Directory.CreateDirectory(temporary);
            var path = Path.Combine(temporary, "installer" + extension);
            try
            {
                await using (var input = file.OpenReadStream())
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920]; long total = 0; int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0) { total += count; if (total > MaxBytes || total > file.Length) throw new ArgumentException("安装包超过大小限制。"); await output.WriteAsync(buffer.AsMemory(0, count), ct); }
                    if (total != file.Length) throw new ArgumentException("安装包传输不完整。");
                }
                ValidatePackage(path, extension);
                string sha; await using (var input = File.OpenRead(path)) sha = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
                var row = new ClientReleaseDto { Id = id, Version = version, Platform = platform, Architecture = architecture, Filename = filename, Size = file.Length, Sha256 = sha, Notes = notes?.Trim() ?? "", Published = true, CreatedAt = DateTime.UtcNow };
                ct.ThrowIfCancellationRequested(); Directory.Move(temporary, Path.Combine(_root, id));
                catalog.Add(row); await Save(catalog, ct); return row;
            }
            catch { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); throw; }
        }
        finally { _gate.Release(); }
    }
    private static void ValidatePackage(string path, string extension)
    {
        using var input = File.OpenRead(path);
        if (extension == ".exe")
        {
            using var reader = new BinaryReader(input);
            if (reader.ReadUInt16() != 0x5a4d) throw new ArgumentException("文件不是 Windows EXE 安装包。");
            input.Position = 0x3c; var offset = reader.ReadInt32();
            if (offset < 64 || offset > input.Length - 4) throw new ArgumentException("EXE 文件头无效。");
            input.Position = offset; if (reader.ReadUInt32() != 0x4550) throw new ArgumentException("EXE 文件头无效。");
        }
        else if (extension == ".dmg")
        {
            if (input.Length < 512) throw new ArgumentException("DMG 文件无效。");
            input.Position = input.Length - 512; Span<byte> signature = stackalloc byte[4]; input.ReadExactly(signature);
            if (!signature.SequenceEqual("koly"u8)) throw new ArgumentException("文件不是 UDIF DMG 安装镜像。");
        }
        else
        {
            try
            {
                using var zip = new ZipArchive(input, ZipArchiveMode.Read);
                if (zip.Entries.Count > 200000 || !zip.Entries.Any(x => x.FullName == "KunBuddy.app/Contents/Info.plist" || x.FullName.EndsWith("/KunBuddy.app/Contents/Info.plist", StringComparison.Ordinal))) throw new ArgumentException("ZIP 中未发现 KunBuddy.app。");
                if (zip.Entries.Any(x => x.FullName.StartsWith('/') || x.FullName.Contains('\\') || x.FullName.Split('/').Any(p => p == ".." || p.Contains(':')))) throw new ArgumentException("ZIP 包含不安全路径。");
            }
            catch (InvalidDataException) { throw new ArgumentException("ZIP 安装包无效。"); }
        }
    }
    public async Task Withdraw(AuthenticatedUser administrator, string id, CancellationToken ct)
    {
        if (!administrator.IsAdministrator) throw new UnauthorizedAccessException();
        await _gate.WaitAsync(ct);
        try { var rows = Load(); var row = rows.SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException(); row.Published = false; await Save(rows, ct); }
        finally { _gate.Release(); }
    }
    public (string Path, ClientReleaseDto Release) Open(string id, bool administrator = false)
    {
        if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) throw new KeyNotFoundException();
        var row = List().SingleOrDefault(x => x.Id == id && (administrator || x.Published)) ?? throw new KeyNotFoundException();
        var directory = Path.Combine(_root, row.Id); var path = Path.Combine(directory, "installer" + Path.GetExtension(row.Filename).ToLowerInvariant());
        if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint) || File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException();
        if (!File.Exists(path)) throw new KeyNotFoundException(); return (path, row);
    }
}
