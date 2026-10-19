using System.Text;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Parsing;

public sealed record MarkdownSection(string RelativePath, string Content, int SourceStart, int SourceEnd, string RepeatedHeader = "");
public sealed record MarkdownDocumentPlan(string RootName, IReadOnlyList<MarkdownSection> Sections);

/// <summary>Deterministic source layout. Model context splitting belongs to the later semantic stage.</summary>
public sealed class MarkdownDocumentPlanner
{
    public const string Version = "markdown-layout-1";
    private sealed record Heading(int Start, int Level, string Name);
    private sealed record Section(string Name, int Start, int End, List<Section> Children);
    private static readonly Regex HeadingLine = new(@"^ {0,3}(#{1,6})(?:[\t ]+|$)(.*)$", RegexOptions.Compiled);
    private static readonly Regex FenceLine = new(@"^ {0,3}(`{3,}|~{3,})(.*)$", RegexOptions.Compiled);
    private static readonly Regex ParagraphEnd = new(@"\r?\n[\t ]*\r?\n", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Compiled);

    public MarkdownDocumentPlan Plan(string content, string documentName, MarkdownParsingOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        options ??= new(); options.Validate();
        var root = SafeName(Path.GetFileNameWithoutExtension(documentName));
        var files = new List<MarkdownSection>();
        var allocated = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (!options.SplitContent || Fits(content, options)) Write(root + ".md", 0, content.Length);
        else
        {
            var headings = FindHeadings(content, ct);
            if (headings.Count == 0) Split(0, content.Length, "", root);
            else Process(BuildSections(headings), "", root);
        }
        return new(root, files);

        void Write(string path, int start, int end, string prefix = "") => files.Add(new(path, prefix + content[start..end], start, end, prefix));
        string Allocate(string parent, string name, bool directory)
        {
            if (!allocated.TryGetValue(parent, out var names)) allocated[parent] = names = new(StringComparer.OrdinalIgnoreCase);
            var stem = SafeName(name); var candidate = stem; var count = 1;
            while (!names.Add(candidate)) candidate = stem + "-" + ++count;
            return parent + candidate + (directory ? "/" : ".md");
        }
        void Save(Section section, string parent)
        {
            ct.ThrowIfCancellationRequested();
            if (Fits(content[section.Start..section.End], options)) { Write(Allocate(parent, section.Name, false), section.Start, section.End); return; }
            var directory = Allocate(parent, section.Name, true);
            if (section.Children.Count > 0)
            {
                var children = section.Children.ToList();
                var first = children[0];
                if (first.Start > section.Start)
                {
                    // Keep the parent's heading/introduction with its first child when it is short.
                    if (EstimateTokens(content[section.Start..first.Start]) < options.MinSectionTokens)
                        children[0] = first with { Start = section.Start };
                    else children.Insert(0, new(section.Name, section.Start, first.Start, []));
                }
                Process(children, directory, section.Name);
            }
            else Split(section.Start, section.End, directory, section.Name);
        }
        void Process(List<Section> sections, string parent, string fallback)
        {
            if (sections.Count == 0) { Split(0, content.Length, parent, fallback); return; }
            var merged = new List<Section>(); Section? pending = null;
            foreach (var section in sections)
            {
                ct.ThrowIfCancellationRequested();
                var small = section.Children.Count == 0 && EstimateTokens(content[section.Start..section.End]) < options.MinSectionTokens;
                if (pending is not null)
                {
                    var combinedFits = Fits(content[pending.Start..section.End], options);
                    if (section.Children.Count > 0 && EstimateTokens(content[pending.Start..pending.End]) < options.MinSectionTokens)
                    {
                        merged.Add(section with { Start = pending.Start }); pending = null; continue;
                    }
                    if (section.Children.Count == 0 && (combinedFits || EstimateTokens(content[pending.Start..pending.End]) < options.MinSectionTokens))
                    {
                        pending = new(SafeName(pending.Name + "_" + section.Name), pending.Start, section.End, []);
                        if (!small) { merged.Add(pending); pending = null; }
                        continue;
                    }
                    merged.Add(pending); pending = null;
                }
                if (small) pending = section;
                else merged.Add(section);
            }
            if (pending is not null)
            {
                if (merged.Count > 0 && merged[^1].Children.Count == 0 && Fits(content[merged[^1].Start..pending.End], options))
                    merged[^1] = merged[^1] with { End = pending.End, Name = merged[^1].Name + "_" + pending.Name };
                else merged.Add(pending);
            }
            foreach (var section in merged) Save(section, parent);
        }
        void Split(int start, int end, string parent, string name)
        {
            var parts = new List<(int Start, int End, string Prefix)>();
            var current = start; var paragraphStart = start;
            foreach (Match match in ParagraphEnd.Matches(content, start))
            {
                if (match.Index >= end) break;
                var boundary = Math.Min(end, match.Index + match.Length);
                AddParagraph(paragraphStart, boundary); paragraphStart = boundary;
            }
            if (paragraphStart < end) AddParagraph(paragraphStart, end);
            if (current < end) AddPart(current, end);
            for (var i = 0; i < parts.Count; i++)
                Write(Allocate(parent, name + (parts.Count > 1 ? "_" + (i + 1) : ""), false), parts[i].Start, parts[i].End, parts[i].Prefix);

            void AddPart(int from, int to, string prefix = "")
            {
                if (from < to) parts.Add((from, to, prefix));
            }
            void AddParagraph(int from, int to)
            {
                ct.ThrowIfCancellationRequested();
                if (Fits(content[current..to], options)) return;
                if (current < from && EstimateTokens(content[current..from]) >= options.MinSectionTokens)
                { AddPart(current, from); current = from; }
                if (Fits(content[current..to], options)) return;
                if (SplitTable(current, to, out var tableParts))
                { parts.AddRange(tableParts); current = to; return; }
                while (current < to)
                {
                    var cut = current + SafeWidth(content.AsSpan(current, to - current), options);
                    if (cut == to) break;
                    AddPart(current, cut); current = cut;
                }
            }
            bool SplitTable(int from, int to, out List<(int, int, string)> tableParts)
            {
                tableParts = [];
                var lines = Lines(content, from, to).ToList();
                var tableStart = lines.FindIndex(l => TableSeparator.IsMatch(content[l.Start..l.End].TrimEnd('\r', '\n')));
                if (tableStart < 1 || tableStart + 1 >= lines.Count) return false;
                tableStart--;
                if (!IsTableRow(content[lines[tableStart].Start..lines[tableStart].End]) ||
                    lines.Skip(tableStart + 2).Any(l => !string.IsNullOrWhiteSpace(content[l.Start..l.End]) && !IsTableRow(content[l.Start..l.End]))) return false;
                var header = content[lines[tableStart].Start..lines[tableStart + 1].End];
                var repeat = header.Length < 1000 && Fits(header, options);
                var partStart = from; var rows = 0; var prefix = "";
                for (var i = tableStart + 2; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (rows > 0 && !Fits(prefix + content[partStart..line.End], options))
                    {
                        tableParts.Add((partStart, line.Start, prefix));
                        partStart = line.Start; prefix = repeat ? header : ""; rows = 0;
                    }
                    if (!string.IsNullOrWhiteSpace(content[line.Start..line.End])) rows++;
                }
                tableParts.Add((partStart, to, prefix));
                return true;
            }
        }
        List<Section> BuildSections(List<Heading> headings)
        {
            var ends = Enumerable.Repeat(content.Length, headings.Count).ToArray();
            var openIndices = new Stack<int>();
            for (var i = 0; i < headings.Count; i++)
            {
                while (openIndices.TryPeek(out var previous) && headings[previous].Level >= headings[i].Level)
                    ends[openIndices.Pop()] = headings[i].Start;
                openIndices.Push(i);
            }
            var result = new List<Section>();
            var open = new Stack<(int Level, Section Section)>();
            if (headings[0].Start > 0) result.Add(new(root, 0, headings[0].Start, []));
            for (var i = 0; i < headings.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var heading = headings[i];
                while (open.TryPeek(out var ancestor) && ancestor.Level >= heading.Level) open.Pop();
                var section = new Section(heading.Name, heading.Start, ends[i], []);
                if (open.TryPeek(out var parent)) parent.Section.Children.Add(section); else result.Add(section);
                open.Push((heading.Level, section));
            }
            return result;
        }
    }

    private static List<Heading> FindHeadings(string content, CancellationToken ct)
    {
        var result = new List<Heading>(); var fence = '\0'; var fenceLength = 0; var comment = false;
        var frontmatter = Regex.Match(content, @"\A\uFEFF?---[\t ]*\r?\n[\s\S]*?\r?\n(?:---|\.\.\.)[\t ]*(?:\r?\n|$)");
        foreach (var (start, end) in Lines(content, 0, content.Length))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[start..end].TrimEnd('\r', '\n');
            if (start < frontmatter.Length) continue;
            var match = FenceLine.Match(line);
            if (fence != '\0')
            {
                if (match.Success && match.Groups[1].Value[0] == fence && match.Groups[1].Length >= fenceLength && string.IsNullOrWhiteSpace(match.Groups[2].Value)) fence = '\0';
                continue;
            }
            if (!comment && match.Success && !(match.Groups[1].Value[0] == '`' && match.Groups[2].Value.Contains('`')))
            { fence = match.Groups[1].Value[0]; fenceLength = match.Groups[1].Length; continue; }
            var excluded = comment;
            var pos = 0;
            while (pos < line.Length)
            {
                var marker = line.IndexOf(comment ? "-->" : "<!--", pos, StringComparison.Ordinal);
                if (marker < 0) break;
                if (!comment && marker == 0) excluded = true;
                pos = marker + (comment ? 3 : 4); comment = !comment;
            }
            if (excluded) continue;
            var heading = HeadingLine.Match(line);
            if (!heading.Success) continue;
            var title = Regex.Replace(heading.Groups[2].Value, @"\s+#+\s*$|<!--.*?-->", "").Trim();
            result.Add(new(start, heading.Groups[1].Length, SafeName(title)));
        }
        return result;
    }

    private static IEnumerable<(int Start, int End)> Lines(string content, int start, int end)
    {
        while (start < end)
        {
            var newline = content.IndexOf('\n', start, end - start);
            var next = newline < 0 ? end : newline + 1;
            yield return (start, next); start = next;
        }
    }

    public static int EstimateTokens(string content)
    {
        long tenths = 0;
        foreach (var rune in content.EnumerateRunes())
            if (!Rune.IsWhiteSpace(rune)) tenths += rune.Value is >= 0x4E00 and <= 0x9FFF or >= 0x3040 and <= 0x30FF or >= 0xAC00 and <= 0xD7AF ? 7 : 3;
        return (int)Math.Min(int.MaxValue, tenths / 10);
    }
    private static bool Fits(string text, MarkdownParsingOptions options) => text.Length <= options.MaxSectionChars && EstimateTokens(text) <= options.MaxSectionTokens;
    private static int SafeWidth(ReadOnlySpan<char> text, MarkdownParsingOptions options)
    {
        var width = Math.Min(text.Length, options.MaxSectionChars);
        // CJK is the highest estimated density. Do not cut a UTF-16 pair.
        width = Math.Min(width, Math.Max(2, options.MaxSectionTokens * 10 / 7));
        if (width < text.Length && char.IsHighSurrogate(text[width - 1]) && char.IsLowSurrogate(text[width])) width--;
        return width;
    }
    private static bool IsTableRow(string line) => line.Trim().Contains('|');
    public static string SafeName(string? value)
    {
        var name = Regex.Replace((value ?? "").Normalize(NormalizationForm.FormC), @"[\x00-\x1f<>:""/\\|?*#%]", "_").Trim().TrimEnd('.');
        if (name.Length > 48) { name = name[..48]; if (char.IsHighSurrogate(name[^1])) name = name[..^1]; }
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "文档";
        if (Regex.IsMatch(name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) name = "_" + name;
        return name;
    }
}

public sealed class MarkdownParsingOptions
{
    [System.Text.Json.Serialization.JsonPropertyName("split_content")] public bool SplitContent { get; set; } = true;
    [System.Text.Json.Serialization.JsonPropertyName("max_section_tokens")] public int MaxSectionTokens { get; set; } = 2048;
    [System.Text.Json.Serialization.JsonPropertyName("max_section_chars")] public int MaxSectionChars { get; set; } = 6000;
    [System.Text.Json.Serialization.JsonPropertyName("min_section_tokens")] public int MinSectionTokens { get; set; } = 512;
    public void Validate()
    {
        if (MaxSectionTokens is < 1 or > 1_000_000 || MaxSectionChars is < 2 or > 4_000_000 || MinSectionTokens < 0 || MinSectionTokens > MaxSectionTokens)
            throw new ArgumentException("解析章节阈值无效：token 估值须为 1–1000000，字符数须为 2–4000000，合并阈值须介于 0 和章节 token 估值之间。");
    }
}
