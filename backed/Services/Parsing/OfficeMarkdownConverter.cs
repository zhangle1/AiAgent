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

    public static string ReadOpenXml(string path, string extension, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(path);
        return extension == ".docx" ? Word(archive, ct) : Slides(archive, ct);
    }

    private static string Word(ZipArchive archive, CancellationToken ct)
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
        var output = new StringBuilder();
        var body = document.Root?.Element(W + "body") ?? throw new InvalidDataException("Word 正文缺失。");
        foreach (var element in body.Elements())
        {
            ct.ThrowIfCancellationRequested();
            if (element.Name == W + "p") AppendParagraph(element);
            else if (element.Name == W + "tbl") AppendTable(output, element.Elements(W + "tr").Select(row => row.Elements(W + "tc").Select(cell => string.Join("\n", cell.Elements(W + "p").Select(p => Text(p, W)))).ToList()));
            else if (element.Name == W + "sdt")
                foreach (var paragraph in element.Descendants(W + "p")) AppendParagraph(paragraph);
        }
        foreach (var noteName in new[] { "footnotes", "endnotes" })
            if (archive.GetEntry("word/" + noteName + ".xml") is not null)
                foreach (var note in Load(archive, "word/" + noteName + ".xml").Root!.Elements().Where(n => (int?)n.Attribute(W + "id") > 0))
                {
                    ct.ThrowIfCancellationRequested();
                    output.AppendLine($"\n## {noteName} {note.Attribute(W + "id")?.Value}\n");
                    foreach (var paragraph in note.Elements(W + "p")) AppendParagraph(paragraph);
                }
        return output.ToString();

        void AppendParagraph(XElement paragraph)
        {
            var text = Text(paragraph, W);
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
            output.AppendLine((outline is >= 0 and <= 5 ? new string('#', outline.Value + 1) + " " : properties?.Element(W + "numPr") is not null ? "- " : "") + text);
            output.AppendLine();
        }
    }

    private static string Slides(ZipArchive archive, CancellationToken ct)
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
            foreach (var shape in tree.Elements())
            {
                if (shape.Descendants(A + "tbl").FirstOrDefault() is { } table)
                    AppendTable(output, table.Elements(A + "tr").Select(row => row.Elements(A + "tc").Select(cell => string.Join("\n", cell.Descendants(A + "p").Select(p => Text(p, A)))).ToList()));
                else foreach (var paragraph in shape.Descendants(A + "p"))
                {
                    var text = Text(paragraph, A);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var title = shape.Descendants(P + "ph").Any(p => (string?)p.Attribute("type") is "title" or "ctrTitle");
                    output.AppendLine((title ? "## " : "") + text + "\n");
                }
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
            var first = range.RangeAddress.FirstAddress.ColumnNumber; var last = range.RangeAddress.LastAddress.ColumnNumber;
            var rows = new List<IEnumerable<string>> { Enumerable.Range(first, last - first + 1).Select(n => "列 " + n) };
            foreach (var row in range.Rows())
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(Enumerable.Range(first, last - first + 1).Select(n => sheet.Cell(row.RowNumber(), n).GetFormattedString()).ToList());
            }
            AppendTable(output, rows);
        }
        return (output.ToString(), new() { ["sheets"] = sheets });
    }

    private static string Text(XElement node, XNamespace ns) => string.Concat(node.Descendants().Select(e => e.Name == ns + "t" ? e.Value : e.Name == ns + "tab" ? "\t" : e.Name == ns + "br" || e.Name == ns + "cr" ? "\n" : ""));
    private static XDocument Load(ZipArchive archive, string name)
    {
        using var stream = (archive.GetEntry(name) ?? throw new InvalidDataException("Office 必需部件缺失：" + name)).Open();
        return XDocument.Load(stream);
    }
    private static void AppendTable(StringBuilder output, IEnumerable<IEnumerable<string>> rows)
    {
        var table = rows.Select(row => row.Select(cell => cell.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>")).ToList()).ToList();
        if (table.Count == 0) return;
        var columns = table.Max(row => row.Count);
        for (var i = 0; i < table.Count; i++)
        {
            output.AppendLine("| " + string.Join(" | ", table[i].Concat(Enumerable.Repeat("", columns - table[i].Count))) + " |");
            if (i == 0) output.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |");
        }
        output.AppendLine();
    }
}
