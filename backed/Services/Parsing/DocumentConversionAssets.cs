using System.IO.Compression;
using System.Security.Cryptography;
using SkiaSharp;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>所有新格式共用既有 PNG／相对路径／SHA-256 协议。</summary>
public sealed class DocumentConversionAssets(string outputDirectory, bool enabled, CancellationToken cancellationToken)
{
    public List<DocumentParseAsset> Assets { get; } = [];
    public List<string> Warnings { get; } = [];
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private long _bytes;

    internal string Reference(ZipArchive archive, string part, string id)
    {
        if (!enabled) return "";
        var rels = DocumentPackage.Relationships(archive, part);
        if (!rels.TryGetValue(id, out var relationship)) return Missing("未找到内嵌图片关系");
        return Entry(archive, relationship.Target);
    }

    internal string Entry(ZipArchive archive, string target)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled) return "";
        if (_cache.TryGetValue(target, out var known)) return $"![图片]({known})";
        var entry = archive.GetEntry(target);
        if (entry is null) return Missing("内嵌图片文件缺失");
        if (entry.Length > 32 * 1024 * 1024) return Missing("内嵌图片超过单图大小限制");
        return Bytes(DocumentPackage.Bytes(entry), target);
    }

    internal string Bytes(byte[] raw, string key)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled) return "";
        if (_cache.TryGetValue(key, out var known)) return $"![图片]({known})";
        if (raw.Length > 32 * 1024 * 1024) return Missing("内嵌图片超过单图大小限制");
        using var data = SKData.CreateCopy(raw);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 4_000_000
            || codec.Info.Width > 4096 || codec.Info.Height > 4096) return Missing("内嵌图片格式不支持或像素超限");
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) return Missing("内嵌图片无法解码");
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        if (png is null) return Missing("内嵌图片无法编码");
        var bytes = png.ToArray();
        if (Assets.Count >= 5000 || bytes.Length > 32 * 1024 * 1024 || (_bytes += bytes.Length) > 256L * 1024 * 1024)
            throw new InvalidDataException("文档图片总量超限。");
        var relative = $"assets/image-{Assets.Count + 1:D4}.png";
        Directory.CreateDirectory(Path.Combine(outputDirectory, "assets"));
        cancellationToken.ThrowIfCancellationRequested();
        using (var file = new FileStream(Path.Combine(outputDirectory, relative), FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes);
        Assets.Add(new() { Path = relative, Kind = "image", Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
        _cache[key] = relative;
        return $"![图片]({relative})";
    }

    internal string Missing(string message)
    {
        if (!Warnings.Contains(message)) Warnings.Add(message);
        return $"[图片未导出：{message}]";
    }
}
