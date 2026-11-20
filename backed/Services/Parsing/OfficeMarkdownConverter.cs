using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>Extracts document structure rather than flattening all XML text (including styles) into one paragraph.</summary>
public static class OfficeMarkdownConverter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static string ReadOpenXml(string path, string extension, CancellationToken ct, DocumentConversionAssets? assets = null)
    {
        using var archive = ZipFile.OpenRead(path);
        return extension == ".docx" ? Word(archive, ct, assets) : Slides(archive, ct, assets);
    }

    private static string Word(ZipArchive archive, CancellationToken ct, DocumentConversionAssets? assets)
    {
        var document = Load(archive, "word/document.xml");
        var styles = new Dictionary<string, (int? Level, string? BasedOn)>();
        if (archive.GetEntry("word/styles.xml") is not null)
            foreach (var style in Load(archive, "word/styles.xml").Descendants(W + "style"))
            {
                var id = (string?)style.Attribute(W + "styleId");
                if (id is null) continue;
                var level = (int?)style.Element(W + "pPr")?.Element(W + "outlineLvl")?.Attribute(W + "val");
                styles[id] = (level, (string?)style.Element(W + "basedOn")?.Attribute(W + "val"));
            }
        var output = new StringBuilder(); var currentPart = "word/document.xml";
        var numbering = archive.GetEntry("word/numbering.xml") is null ? null : Load(archive, "word/numbering.xml");
        var hyperlinks = new Dictionary<string, Dictionary<string, string>>();
        var body = document.Root?.Element(W + "body") ?? throw new InvalidDataException("Word 正文缺失。");
        AppendBlocks(body.Elements());

        void AppendBlocks(IEnumerable<XElement> elements)
        {
            foreach (var element in elements)
            {
                ct.ThrowIfCancellationRequested();
                if (element.Name == W + "p") AppendParagraph(element);
                else if (element.Name == W + "tbl")
                {
                    var rows = element.Elements(W + "tr").ToList();
                    AppendTable(output, rows.Select(row => row.Elements(W + "tc").Select(cell => string.Join("\n", cell.Elements(W + "p").Select(Inline))).ToList()),
                        rows.FirstOrDefault()?.Element(W + "trPr")?.Element(W + "tblHeader") is not null);
                }
                else if (element.Name == W + "sdt") AppendBlocks(element.Element(W + "sdtContent")?.Elements() ?? []);
                CheckLength(output);
            }
        }
        foreach (var noteName in new[] { "footnotes", "endnotes" })
            if (archive.GetEntry("word/" + noteName + ".xml") is not null)
            {
                currentPart = "word/" + noteName + ".xml";
                foreach (var note in Load(archive, "word/" + noteName + ".xml").Root!.Elements().Where(n => (int?)n.Attribute(W + "id") > 0))
                {
                    ct.ThrowIfCancellationRequested();
                    var noteText = string.Join("\n\n", note.Elements(W + "p").Select(Inline));
                    output.AppendLine($"[^{noteName}-{note.Attribute(W + "id")?.Value}]: " + noteText.Replace("\n", "\n    ")).AppendLine();
                    CheckLength(output);
                }
            }
        return output.ToString();

        void AppendParagraph(XElement paragraph)
        {
            var text = Inline(paragraph);
            if (string.IsNullOrWhiteSpace(text)) { output.AppendLine(); return; }
            var properties = paragraph.Element(W + "pPr");
            var outline = (int?)properties?.Element(W + "outlineLvl")?.Attribute(W + "val");
            var styleId = (string?)properties?.Element(W + "pStyle")?.Attribute(W + "val");
            var visited = new HashSet<string>();
            while (outline is null && styleId is not null && visited.Add(styleId))
            {
                if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(styleId[7..], out var builtin)) { outline = builtin - 1; break; }
                if (styles.TryGetValue(styleId, out var style)) { outline = style.Level; styleId = style.BasedOn; }
                else break;
            }
            output.AppendLine((outline is >= 0 and <= 5 ? new string('#', outline.Value + 1) + " " : properties?.Element(W + "numPr") is { } num ? ListPrefix(num) : "") + text);
            output.AppendLine();
        }

        string ListPrefix(XElement properties)
        {
            var level = Math.Clamp((int?)properties.Element(W + "ilvl")?.Attribute(W + "val") ?? 0, 0, 8);
            var id = (string?)properties.Element(W + "numId")?.Attribute(W + "val");
            var abstractId = (string?)numbering?.Root?.Elements(W + "num").FirstOrDefault(n => (string?)n.Attribute(W + "numId") == id)?.Element(W + "abstractNumId")?.Attribute(W + "val");
            var definition = numbering?.Root?.Elements(W + "abstractNum").FirstOrDefault(n => (string?)n.Attribute(W + "abstractNumId") == abstractId)?
                .Elements(W + "lvl").FirstOrDefault(n => (int?)n.Attribute(W + "ilvl") == level);
            var format = (string?)definition?.Element(W + "numFmt")?.Attribute(W + "val");
            return new string(' ', level * 4) + (format is null or "bullet" ? "- " : "1. ");
        }

        string Inline(XElement node)
        {
            ct.ThrowIfCancellationRequested();
            if (node.Name == W + "t") return EscapeInline(node.Value);
            if (node.Name == W + "tab") return "\t";
            if (node.Name == W + "br" || node.Name == W + "cr") return "\n";
            if (node.Name == W + "drawing" || node.Name == W + "pict") return Drawing(node);
            if (node.Name == W + "del" || node.Name == W + "instrText" || node.Name == W + "pPr" || node.Name == W + "rPr") return "";
            if (node.Name == W + "footnoteReference" || node.Name == W + "endnoteReference")
                return $"[^{(node.Name.LocalName == "footnoteReference" ? "footnotes" : "endnotes")}-{node.Attribute(W + "id")?.Value}]";
            var text = string.Concat(node.Elements().Select(Inline));
            if (node.Name == W + "r" && !string.IsNullOrWhiteSpace(text))
            {
                var properties = node.Element(W + "rPr");
                bool Enabled(string name) => properties?.Element(W + name) is { } value && (string?)value.Attribute(W + "val") is not "0" and not "false" and not "off";
                if (Enabled("b")) text = Format(text, "**");
                if (Enabled("i")) text = Format(text, "*");
                if (Enabled("strike")) text = Format(text, "~~");
            }
            if (node.Name == W + "hyperlink" && (string?)node.Attribute(R + "id") is { } id)
            {
                if (!hyperlinks.TryGetValue(currentPart, out var targets))
                {
                    targets = []; hyperlinks[currentPart] = targets;
                    var slash = currentPart.LastIndexOf('/');
                    var rels = currentPart[..(slash + 1)] + "_rels/" + currentPart[(slash + 1)..] + ".rels";
                    if (archive.GetEntry(rels) is not null)
                        foreach (var rel in Load(archive, rels).Root!.Elements())
                            if ((string?)rel.Attribute("Type") is { } type && type.EndsWith("/hyperlink", StringComparison.Ordinal) &&
                                (string?)rel.Attribute("Id") is { } key && Uri.TryCreate((string?)rel.Attribute("Target"), UriKind.Absolute, out var target) && target.Scheme is "https" or "http" or "mailto")
                                targets[key] = target.AbsoluteUri.Replace("(", "%28").Replace(")", "%29");
                }
                if (targets.TryGetValue(id, out var href)) return $"[{text}]({href})";
            }
            return text;
        }

        string Drawing(XElement node)
        {
            if (assets is null) return "";
            var references = node.Descendants().Select(e => (string?)e.Attribute(R + "embed") ?? (e.Name.LocalName == "imagedata" ? (string?)e.Attribute(R + "id") : null))
                .Where(id => id is not null).Distinct().ToArray();
            return string.Join(" ", references.Select(id => assets.Reference(archive, currentPart, id!)));
        }
    }

    private static string Slides(ZipArchive archive, CancellationToken ct, DocumentConversionAssets? assets)
    {
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var order = new List<string>();
        if (archive.GetEntry("ppt/presentation.xml") is not null && archive.GetEntry("ppt/_rels/presentation.xml.rels") is not null)
        {
            var targets = Load(archive, "ppt/_rels/presentation.xml.rels").Descendants(relationships + "Relationship")
                .Where(e => (string?)e.Attribute("TargetMode") != "External")
                .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
            foreach (var slide in Load(archive, "ppt/presentation.xml").Descendants(P + "sldId"))
                if (targets.TryGetValue((string?)slide.Attribute(R + "id") ?? "", out var target))
                {
                    var name = target.StartsWith('/') ? target.TrimStart('/') : "ppt/" + target;
                    if (!name.StartsWith("ppt/slides/", StringComparison.Ordinal) || name.Split('/').Contains("..")) throw new InvalidDataException("PPT 幻灯片路径无效。");
                    order.Add(name);
                }
        }
        else order.AddRange(archive.Entries.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"))
            .OrderBy(e => int.Parse(System.Text.RegularExpressions.Regex.Match(e.Name, @"\d+").Value)).Select(e => e.FullName));
        var output = new StringBuilder(); var index = 0;
        foreach (var name in order)
        {
            ct.ThrowIfCancellationRequested();
            var slide = Load(archive, name);
            output.AppendLine($"# 幻灯片 {++index}\n");
            var tree = slide.Descendants(P + "spTree").FirstOrDefault();
            if (tree is null) continue;
            foreach (var shape in tree.Elements().SelectMany(e => e.Name == P + "grpSp" ? e.Descendants().Where(n => n.Name == P + "sp" || n.Name == P + "pic" || n.Name == P + "graphicFrame") : new[] { e }))
            {
                ct.ThrowIfCancellationRequested();
                if (shape.Name == P + "pic" && assets is not null)
                    foreach (var blip in shape.Descendants(A + "blip"))
                        if ((string?)blip.Attribute(R + "embed") is { } id) output.AppendLine(assets.Reference(archive, name, id)).AppendLine();
                if (shape.Descendants(A + "tbl").FirstOrDefault() is { } table)
                    AppendTable(output, table.Elements(A + "tr").Select(row => row.Elements(A + "tc").Select(cell => string.Join("\n", cell.Descendants(A + "p").Select(p => Text(p, A)))).ToList()));
                else foreach (var paragraph in shape.Descendants(A + "p"))
                {
                    var text = Text(paragraph, A);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var title = shape.Descendants(P + "ph").Any(p => (string?)p.Attribute("type") is "title" or "ctrTitle");
                    output.AppendLine((title ? "## " : "") + text + "\n");
                }
                CheckLength(output);
            }
            foreach (var note in DocumentPackage.Relationships(archive, name).Values.Where(r => r.Type.EndsWith("/notesSlide", StringComparison.Ordinal)))
            {
                ct.ThrowIfCancellationRequested();
                var notes = DocumentPackage.Xml(archive, note.Target);
                var paragraphs = notes.Descendants(P + "sp").Where(s => s.Descendants(P + "ph").All(p => (string?)p.Attribute("type") is null or "body"))
                    .SelectMany(s => s.Descendants(A + "p")).Select(p => Text(p, A)).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
                if (paragraphs.Length > 0) output.AppendLine("### 备注\n").AppendLine(string.Join("\n\n", paragraphs)).AppendLine();
                CheckLength(output);
            }
        }
        return output.ToString();
    }

    public static (string Content, Dictionary<string, object?> Locators) ReadWorkbook(string path, CancellationToken ct)
    {
        using var workbook = new XLWorkbook(path);
        var output = new StringBuilder(); var sheets = new List<string>();
        foreach (var sheet in workbook.Worksheets)
        {
            ct.ThrowIfCancellationRequested(); sheets.Add(sheet.Name); output.AppendLine("# " + sheet.Name + "\n");
            var range = sheet.RangeUsed(); if (range is null) continue;
            if ((long)range.RowCount() * range.ColumnCount() > 2_000_000) throw new InvalidDataException("工作表使用区域超过 200 万单元格上限。");
            var first = range.RangeAddress.FirstAddress.ColumnNumber; var last = range.RangeAddress.LastAddress.ColumnNumber;
            var rows = new List<IEnumerable<string>> { Enumerable.Range(first, last - first + 1).Select(n => "列 " + n) };
            foreach (var row in range.Rows())
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(Enumerable.Range(first, last - first + 1).Select(n => sheet.Cell(row.RowNumber(), n).GetFormattedString()).ToList());
            }
            AppendTable(output, rows);
            CheckLength(output);
        }
        return (output.ToString(), new() { ["sheets"] = sheets });
    }

    private static string EscapeInline(string text)
    {
        foreach (var value in new[] { "\\", "*", "_", "[", "]", "`", "#", "~", "!" }) text = text.Replace(value, "\\" + value);
        return text.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string Format(string text, string marker)
    {
        var start = text.Length - text.TrimStart().Length; var end = text.TrimEnd().Length;
        return start >= end ? text : text[..start] + marker + text[start..end] + marker + text[end..];
    }

    private static string Text(XElement node, XNamespace ns, Func<XElement, string>? drawing = null)
    {
        if (node.Name == ns + "t") return node.Value;
        if (node.Name == ns + "tab") return "\t";
        if (node.Name == ns + "br" || node.Name == ns + "cr") return "\n";
        if (drawing is not null && (node.Name == W + "drawing" || node.Name == W + "pict")) return drawing(node);
        return string.Concat(node.Elements().Select(child => Text(child, ns, drawing)));
    }
    private static XDocument Load(ZipArchive archive, string name)
    {
        return DocumentPackage.Xml(archive, name);
    }
    private static void AppendTable(StringBuilder output, IEnumerable<IEnumerable<string>> rows, bool hasHeader = true)
    {
        var table = rows.Select(row => row.Select(cell => cell.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>")).ToList()).ToList();
        if (table.Count == 0) return;
        var columns = table.Max(row => row.Count);
        if (!hasHeader) table.Insert(0, Enumerable.Repeat("", columns).ToList());
        for (var i = 0; i < table.Count; i++)
        {
            output.AppendLine("| " + string.Join(" | ", table[i].Concat(Enumerable.Repeat("", columns - table[i].Count))) + " |");
            if (i == 0) output.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |");
            CheckLength(output);
        }
        output.AppendLine();
    }

    private static void CheckLength(StringBuilder output)
    {
        if (output.Length > DocumentConversionService.MaxCharacters) throw new InvalidDataException("Office 转换正文长度超限。");
    }
}
