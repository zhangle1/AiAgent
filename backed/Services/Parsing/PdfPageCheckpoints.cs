using System.Security.Cryptography;
using System.Text.Json;

namespace AiAgent.Backend.Services.Parsing;

internal sealed record PdfPageResult(int Number, string Markdown, string Text, int Headings, bool Scanned, List<DocumentParseAsset> Assets);

/// <summary>Only a complete page receipt makes its text and images available to a later attempt.</summary>
internal sealed class PdfPageCheckpoints
{
    // Bump this version when page extraction or image references change.
    private const string Version = "v2";
    private readonly string _root;
    private readonly HashSet<string> _verifiedImages = new(StringComparer.Ordinal);

    internal PdfPageCheckpoints(string directory, Stream source, bool writeImages, CancellationToken ct)
    {
        var hash = Hash(source, ct);
        source.Position = 0;
        _root = DocumentConversionPaths.RequireNoLinks(Path.Combine(directory, $"{Version}-{hash}-{(writeImages ? "images" : "text")}"));
        Directory.CreateDirectory(_root);
    }

    internal PdfPageResult? ReadPage(int number, CancellationToken ct)
    {
        var path = PagePath(number);
        if (!File.Exists(path)) return null;
        PdfPageResult? page;
        try
        {
            using var input = File.OpenRead(path);
            page = JsonSerializer.Deserialize<PdfPageResult>(input);
        }
        catch (JsonException) { return null; }
        if (page is null || page.Number != number || page.Markdown is null || page.Text is null || page.Assets is null)
            return null;
        foreach (var asset in page.Assets)
        {
            ct.ThrowIfCancellationRequested();
            if (asset is null || asset.Page != number || asset.Sha256 is not { Length: 64 } ||
                !asset.Sha256.All(c => char.IsAsciiHexDigitLower(c)) ||
                asset.Path != $"assets/{asset.Sha256}.png" || !VerifyImage(asset, ct))
                return null;
        }
        return page;
    }

    internal void SavePage(PdfPageResult page, CancellationToken ct) =>
        Replace(PagePath(page.Number), stream => JsonSerializer.Serialize(stream, page), ct);

    internal double ResolveBodySize(Func<double> detect, CancellationToken ct)
    {
        var path = DocumentConversionPaths.RequireNoLinks(Path.Combine(_root, "body-size.json"));
        if (File.Exists(path))
        {
            try
            {
                using var input = File.OpenRead(path);
                var size = JsonSerializer.Deserialize<double>(input);
                if (double.IsFinite(size) && size >= 0) return size;
            }
            catch (JsonException) { }
        }
        var bodySize = detect();
        Replace(path, stream => JsonSerializer.Serialize(stream, bodySize), ct);
        return bodySize;
    }

    internal void SaveImage(string relative, byte[] bytes, CancellationToken ct)
    {
        if (_verifiedImages.Contains(relative)) return;
        var target = AssetPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Replace(target, stream => stream.Write(bytes), ct);
        _verifiedImages.Add(relative);
    }

    internal void CopyImage(string relative, string output, CancellationToken ct)
    {
        var target = Path.Combine(output, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var source = File.OpenRead(AssetPath(relative));
        using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyToAsync(destination, ct).GetAwaiter().GetResult();
    }

    private bool VerifyImage(DocumentParseAsset asset, CancellationToken ct)
    {
        if (_verifiedImages.Contains(asset.Path)) return true;
        var path = AssetPath(asset.Path);
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Hash(stream, ct) == asset.Sha256 && _verifiedImages.Add(asset.Path);
    }

    private string PagePath(int number) => DocumentConversionPaths.RequireNoLinks(Path.Combine(_root, $"page-{number:D4}.json"));
    private string AssetPath(string relative) => DocumentConversionPaths.RequireNoLinks(Path.Combine(_root, relative));

    private static string Hash(Stream stream, CancellationToken ct) =>
        Convert.ToHexStringLower(SHA256.HashDataAsync(stream, ct).AsTask().GetAwaiter().GetResult());

    private static void Replace(string target, Action<Stream> write, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(stream);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
