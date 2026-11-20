using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AngleSharp.Html.Parser;

namespace AiAgent.Backend.Services.Parsing;

internal static class DocumentHtmlConverter
{
    internal static string Html(string html, ZipArchive? archive, string? part, DocumentConversionAssets assets, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 只解析 DOM；不创建有网络加载器或脚本执行器的浏览器上下文。
        var document = new HtmlParser().ParseDocument(html);
        foreach (var vector in document.QuerySelectorAll("svg")) vector.Replace(document.CreateTextNode(assets.Missing("HTML 内联 SVG 暂未转图")));
        foreach (var hidden in document.QuerySelectorAll("script,style,noscript,template,iframe,object,embed,link,meta,base,svg")) hidden.Remove();
        foreach (var element in document.All)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var attribute in element.Attributes.ToArray())
                if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attribute.Name is "style" or "srcset") element.RemoveAttribute(attribute.Name);
            if (element.LocalName == "a" && element.GetAttribute("href") is { } href)
            {
                if (href.StartsWith('#')) continue;
                if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")) element.RemoveAttribute("href");
            }
        }
        var replacements = new Dictionary<string, string>();
        foreach (var image in document.QuerySelectorAll("img"))
        {
            ct.ThrowIfCancellationRequested();
            var src = image.GetAttribute("src") ?? "";
            string reference = "";
            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma > 0 && comma < 100 && src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase) && src.Length < 44 * 1024 * 1024)
                {
                    try { var bytes = Convert.FromBase64String(src[(comma + 1)..]); reference = assets.Bytes(bytes, "data:" + Convert.ToHexStringLower(SHA256.HashData(bytes))); }
                    catch (FormatException) { reference = assets.Missing("图片 Data URI 无效"); }
                }
                else reference = assets.Missing("图片 Data URI 不支持或超限");
            }
            else if (archive is not null && part is not null && !string.IsNullOrWhiteSpace(src) && !Uri.TryCreate(src, UriKind.Absolute, out _))
                reference = assets.Entry(archive, DocumentPackage.Resolve(part, src));
            else reference = assets.Missing("HTML 外部或本地旁路图片未加载");
            var marker = "AIAGENTIMAGE" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            replacements[marker] = reference;
            image.Replace(document.CreateTextNode(marker));
        }
        // v6 的 GitHub flavor 会直接保留整块 HTML；Default + GithubFlavored 才会真正转换为 GFM。
        var converter = new ReverseMarkdown.Converter(new ReverseMarkdown.Config { GithubFlavored = true, Formatting = { RemoveComments = true } });
        var markdown = converter.Convert(document.Body?.InnerHtml ?? "");
        if (!string.IsNullOrWhiteSpace(document.Title)) markdown = "# " + document.Title.Replace("\n", " ").Trim() + "\n\n" + markdown;
        foreach (var (marker, reference) in replacements) markdown = markdown.Replace(marker, reference, StringComparison.Ordinal);
        ct.ThrowIfCancellationRequested();
        return markdown.ReplaceLineEndings("\n");
    }

    internal static string Epub(string path, DocumentConversionAssets assets, CancellationToken ct)
    {
        using var package = new DocumentPackage(path); var archive = package.Archive;
        if (archive.GetEntry("META-INF/encryption.xml") is not null) throw new InvalidDataException("此 EPUB 带有加密或混淆资源，暂不支持转换。");
        var container = DocumentPackage.Xml(archive, "META-INF/container.xml");
        var rootFile = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("EPUB 缺少根文档。");
        var opfPath = DocumentPackage.Resolve("container.xml", rootFile);
        var opf = DocumentPackage.Xml(archive, opfPath);
        var manifest = opf.Descendants().Where(e => e.Name.LocalName == "manifest").Elements()
            .ToDictionary(e => (string)e.Attribute("id")!, e => (Href: (string)e.Attribute("href")!, Mime: (string?)e.Attribute("media-type")));
        var output = new StringBuilder();
        foreach (var reference in opf.Descendants().Where(e => e.Name.LocalName == "spine").Elements())
        {
            ct.ThrowIfCancellationRequested();
            if (!manifest.TryGetValue((string?)reference.Attribute("idref") ?? "", out var item)) throw new InvalidDataException("EPUB 阅读顺序引用缺失。");
            if (item.Mime is not ("application/xhtml+xml" or "text/html")) throw new InvalidDataException("EPUB 包含不支持的正文格式。");
            var part = DocumentPackage.Resolve(opfPath, item.Href);
            var entry = archive.GetEntry(part) ?? throw new InvalidDataException("EPUB 章节缺失。");
            using var input = new MemoryStream(DocumentPackage.Bytes(entry)); using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            output.AppendLine(Html(reader.ReadToEnd(), archive, part, assets, ct)).AppendLine();
            if (output.Length > DocumentConversionService.MaxCharacters) throw new InvalidDataException("EPUB 正文长度超限。");
        }
        return output.ToString();
    }
}
