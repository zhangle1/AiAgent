using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>
/// 知识库文件路径和文件保存服务。
/// </summary>
public interface IKnowledgePathService
{
    /// <summary>
    /// 知识库根目录。
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// 规范化知识库名称，保证可用于 URL 和目录名。
    /// </summary>
    string NormalizeName(string name);

    /// <summary>
    /// 获取知识库目录。
    /// </summary>
    string GetKnowledgeBasePath(string name);

    /// <summary>
    /// 获取知识库 raw 文件目录。
    /// </summary>
    string GetRawPath(string name);

    /// <summary>
    /// 获取索引版本目录。
    /// </summary>
    string GetVersionPath(string name, int versionNo, string provider);

    /// <summary>
    /// 保存上传文件并返回存储路径、哈希和大小。
    /// </summary>
    Task<(string StoragePath, string FileHash, long FileSize)> SaveFileAsync(string kbName, IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算文件 SHA256 哈希。
    /// </summary>
    string ComputeFileHash(string path);
}

/// <summary>
/// 知识库文件路径和文件保存实现。
/// </summary>
public sealed class KnowledgePathService : IKnowledgePathService
{
    private static readonly Regex InvalidNameChars = new("[^a-zA-Z0-9_-]+", RegexOptions.Compiled);
    private readonly IConfiguration _configuration;
    private readonly ILogger<KnowledgePathService> _logger;

    /// <summary>
    /// 初始化知识库路径服务，并读取数据根目录配置。
    /// </summary>
    public KnowledgePathService(IConfiguration configuration, ILogger<KnowledgePathService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 知识库数据根目录，默认落到 data/knowledge_bases。
    /// </summary>
    public string RootPath
    {
        get
        {
            var dataPath = _configuration["DataPath"];
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                dataPath = "data";
            }

            return Path.GetFullPath(Path.Combine(dataPath, "knowledge_bases"));
        }
    }

    /// <summary>
    /// 将知识库名称转换为安全的目录名和路由名。
    /// </summary>
    public string NormalizeName(string name)
    {
        var normalized = InvalidNameChars.Replace((name ?? "").Trim(), "-").Trim('-').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Knowledge base name is required.");
        }

        if (normalized.Length > 96)
        {
            normalized = normalized[..96].Trim('-');
        }

        return normalized;
    }

    /// <summary>
    /// 获取指定知识库的完整目录路径。
    /// </summary>
    public string GetKnowledgeBasePath(string name)
    {
        return Path.Combine(RootPath, NormalizeName(name));
    }

    /// <summary>
    /// 获取指定知识库保存原始上传文件的目录。
    /// </summary>
    public string GetRawPath(string name)
    {
        return Path.Combine(GetKnowledgeBasePath(name), "raw");
    }

    /// <summary>
    /// 获取指定知识库索引版本的持久化目录。
    /// </summary>
    public string GetVersionPath(string name, int versionNo, string provider)
    {
        return Path.Combine(GetKnowledgeBasePath(name), $"version-{versionNo}", provider);
    }

    /// <summary>
    /// 保存上传文件，并返回存储路径、文件哈希和文件大小。
    /// </summary>
    public async Task<(string StoragePath, string FileHash, long FileSize)> SaveFileAsync(string kbName, IFormFile file, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (file.Length <= 0)
        {
            throw new InvalidOperationException($"File '{file.FileName}' is empty.");
        }

        var rawPath = GetRawPath(kbName);
        Directory.CreateDirectory(rawPath);
        var originalName = Path.GetFileName(file.FileName);
        var safeName = MakeSafeFileName(originalName);
        var targetPath = Path.Combine(rawPath, safeName);
        if (File.Exists(targetPath))
        {
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var extension = Path.GetExtension(safeName);
            targetPath = Path.Combine(rawPath, $"{stem}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}");
        }

        using var sha = SHA256.Create();
        await using (var source = file.OpenReadStream())
        await using (var target = File.Create(targetPath))
        await using (var crypto = new CryptoStream(target, sha, CryptoStreamMode.Write))
        {
            await source.CopyToAsync(crypto, cancellationToken);
            await crypto.FlushAsync(cancellationToken);
        }

        var hash = ToHex(sha.Hash);
        _logger.LogInformation("Knowledge file saved. Kb={KbName}, File={FileName}, Bytes={Bytes}, ElapsedMs={ElapsedMs}", kbName, file.FileName, file.Length, stopwatch.ElapsedMilliseconds);
        return (targetPath, hash, file.Length);
    }

    /// <summary>
    /// 计算本地文件的 SHA256 哈希，用于去重和索引追踪。
    /// </summary>
    public string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var bytes = sha.ComputeHash(stream);
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    private static string MakeSafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(name) ? $"upload-{DateTime.UtcNow:yyyyMMddHHmmssfff}.txt" : name;
    }

    private static string ToHex(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}