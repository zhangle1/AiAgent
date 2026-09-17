using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using ClosedXML.Excel;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Local, read-only Office previews. No document is sent to an online viewer.</summary>
public sealed class KnowledgeOfficePreviewService(IKnowledgePathService paths, IConfiguration configuration)
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const long MaxExpandedBytes = 100 * 1024 * 1024;

    public async Task<KnowledgeOfficePreviewDto> PreviewAsync(AiKnowledgeBase kb, AiKnowledgeDocument document, CancellationToken token)
    {
        var path = Path.GetFullPath(document.StoragePath);
        var root = Path.GetFullPath(paths.GetKnowledgeBasePath(kb.Name)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("文件不在知识库目录内。");
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Office 预览支持最大 32 MB 文件，请下载查看。");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".doc" or ".xls")
        {
            using var converted = await ConvertLegacyAsync(path, configuration["Knowledge:LibreOfficePath"], token);
            path = converted.Path;
            extension = Path.GetExtension(path);
            return Render(path, extension, token);
        }
        return Render(path, extension, token);
    }

    public static KnowledgeOfficePreviewDto Render(string path, string extension, CancellationToken token)
    {
        if (extension is not (".docx" or ".xlsx")) throw new NotSupportedException("此格式不支持 Office 预览。");
        using (var archive = ZipFile.OpenRead(path))
            if (archive.Entries.Sum(e => e.Length) > MaxExpandedBytes || archive.Entries.Count > 10000)
                throw new InvalidOperationException("文档展开后过大，请下载查看。");
        var preview = new KnowledgeOfficePreviewDto();
        if (extension == ".xlsx")
        {
            using var workbook = new XLWorkbook(path);
            var totalCharacters = 0;
            foreach (var sheet in workbook.Worksheets.Take(20))
            {
                token.ThrowIfCancellationRequested();
                var html = new StringBuilder("<table>");
                var range = sheet.RangeUsed();
                if (range is not null)
                {
                    var lastRow = Math.Min(range.LastRow().RowNumber(), range.FirstRow().RowNumber() + 199);
                    var lastColumn = Math.Min(range.LastColumn().ColumnNumber(), range.FirstColumn().ColumnNumber() + 49);
                    preview.Truncated |= lastRow < range.LastRow().RowNumber() || lastColumn < range.LastColumn().ColumnNumber();
                    for (var row = range.FirstRow().RowNumber(); row <= lastRow; row++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (totalCharacters + html.Length > 4_000_000) { preview.Truncated = true; break; }
                        html.Append("<tr><th>").Append(row).Append("</th>");
                        for (var column = range.FirstColumn().ColumnNumber(); column <= lastColumn; column++)
                        {
                            var cell = sheet.Cell(row, column);
                            var value = cell.HasFormula ? cell.CachedValue.ToString() : cell.GetFormattedString();
                            if (value.Length > 2000) { value = value[..2000]; preview.Truncated = true; }
                            html.Append("<td>").Append(WebUtility.HtmlEncode(value)).Append("</td>");
                        }
                        html.Append("</tr>");
                    }
                }
                html.Append("</table>");
                preview.Sections.Add(new() { Name = sheet.Name, Html = html.ToString() });
                totalCharacters += html.Length;
                if (totalCharacters > 4_000_000) { preview.Truncated = true; break; }
            }
            preview.Truncated |= workbook.Worksheets.Count > 20;
            return preview;
        }

        using var zip = ZipFile.OpenRead(path);
        var main = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Word 正文缺失。");
        var xml = ReadXml(main);
        var relationships = zip.GetEntry("word/_rels/document.xml.rels");
        var images = relationships is null ? new Dictionary<string, string>() : ReadXml(relationships).Root!.Elements()
            .Where(e => (string?)e.Attribute("TargetMode") != "External" && ((string?)e.Attribute("Type"))?.EndsWith("/image") == true)
            .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
        var output = new StringBuilder();
        long imageBytes = 0;
        foreach (var block in xml.Descendants(W + "body").Elements())
        {
            token.ThrowIfCancellationRequested();
            if (output.Length > 2_000_000) { preview.Truncated = true; break; }
            if (block.Name == W + "tbl")
            {
                output.Append("<table>");
                foreach (var row in block.Elements(W + "tr"))
                {
                    output.Append("<tr>");
                    foreach (var cell in row.Elements(W + "tc"))
                        output.Append("<td>").Append(WebUtility.HtmlEncode(string.Join("\n", cell.Elements(W + "p").Select(Text)))).Append("</td>");
                    output.Append("</tr>");
                }
                output.Append("</table>");
            }
            else output.Append("<p>").Append(WebUtility.HtmlEncode(Text(block))).Append("</p>");
            foreach (var blip in block.Descendants(A + "blip"))
            {
                if (!images.TryGetValue((string?)blip.Attribute(R + "embed") ?? "", out var target)) continue;
                var entry = zip.GetEntry("word/" + target);
                var mime = Path.GetExtension(target).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", _ => null };
                if (entry is null || mime is null) continue;
                if (entry.Length + imageBytes > 12 * 1024 * 1024) { preview.Truncated = true; continue; }
                imageBytes += entry.Length;
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                output.Append("<img alt=\"文档图片\" src=\"data:").Append(mime).Append(";base64,").Append(Convert.ToBase64String(buffer.ToArray())).Append("\" />");
            }
        }
        preview.Sections.Add(new() { Name = "正文", Html = output.ToString() });
        return preview;
    }

    private static string Text(XElement element) => string.Concat(element.Descendants().Select(e =>
        e.Name == W + "t" ? e.Value : e.Name == W + "tab" ? "\t" : e.Name == W + "br" ? "\n" : ""));

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxExpandedBytes });
        return XDocument.Load(reader);
    }

    public static async Task<ConvertedOfficeFile> ConvertLegacyAsync(string path, string? executable, CancellationToken token)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aiagent-office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var converted = new ConvertedOfficeFile(directory, System.IO.Path.Combine(directory, System.IO.Path.GetFileNameWithoutExtension(path) + (System.IO.Path.GetExtension(path).Equals(".doc", StringComparison.OrdinalIgnoreCase) ? ".docx" : ".xlsx")));
        try
        {
            var start = new ProcessStartInfo(executable ?? "soffice") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-env:UserInstallation=" + new Uri(System.IO.Path.Combine(directory, "profile")).AbsoluteUri, "--headless", "--convert-to", System.IO.Path.GetExtension(converted.Path)[1..], "--outdir", directory, path })
                start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("旧版 DOC/XLS 预览需要服务器安装 LibreOffice，并配置 Knowledge:LibreOfficePath；也可另存为 DOCX/XLSX 后上传。"); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr); }
            catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
            if (process.ExitCode != 0 || !File.Exists(converted.Path)) throw new InvalidOperationException("旧版 Office 文件转换失败，请检查文件是否加密或损坏。");
            return converted;
        }
        catch { converted.Dispose(); throw; }
    }

    public sealed class ConvertedOfficeFile(string directory, string path) : IDisposable
    {
        public string Path { get; } = path;
        public void Dispose()
        {
            var root = System.IO.Path.GetFullPath(directory);
            if (root.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                System.IO.Path.GetFileName(root).StartsWith("aiagent-office-", StringComparison.Ordinal) && Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
