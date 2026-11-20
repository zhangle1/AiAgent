using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Outline;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>PdfPig 文字/版面 + PDFium 渲染；保持 Markdown、PNG 和 manifest 协议。</summary>
internal static class CSharpPdfLayoutConverter
{
    private const int MaxPages = 2000, MaxPageLetters = 200000, MaxPagePaths = 20000;
    private const int MaxTextCharacters = 32 * 1024 * 1024;
    private sealed record Block(Box Bounds, string Markdown);
    private sealed record Region(Box Bounds, string Kind);
    private sealed record Table(Box Bounds, string Markdown);
    private sealed record ScanWord(Word Word, Box Bounds, double Baseline, double Size);
    private sealed record ScanLine(List<ScanWord> Words, Box Bounds, double Baseline, double Size, string Text);
    private static readonly Regex PageLabel = new(@"^\s*[-–—(]?\s*(?:\d{1,5}|[ivxlcdm]{1,8})\s*[-–—)]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CaptionLabel = new(@"^(?:fig(?:ure)?\.?|table|图|表)\s*\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // PdfPig 0.1.16 已对 CropBox 和 Rotate 应用初始矩阵，原点在可见页左下。
    private readonly record struct Box(double Left, double Bottom, double Right, double Top)
    {
        public double Width => Right - Left;
        public double Height => Top - Bottom;
        public bool Finite => double.IsFinite(Left) && double.IsFinite(Bottom) && double.IsFinite(Right) && double.IsFinite(Top);
        public bool Contains(Box b) => b.Left >= Left - 1 && b.Right <= Right + 1 && b.Bottom >= Bottom - 1 && b.Top <= Top + 1;
        public bool Touches(Box b, double gap = 4) => Left <= b.Right + gap && Right + gap >= b.Left && Bottom <= b.Top + gap && Top + gap >= b.Bottom;
        public Box Union(Box b) => new(Math.Min(Left, b.Left), Math.Min(Bottom, b.Bottom), Math.Max(Right, b.Right), Math.Max(Top, b.Top));
        public Box Clip(double width, double height, double pad = 0) => new(Math.Clamp(Left - pad, 0, width), Math.Clamp(Bottom - pad, 0, height), Math.Clamp(Right + pad, 0, width), Math.Clamp(Top + pad, 0, height));
        public static Box From(PdfRectangle r) => new(Math.Min(r.BottomLeft.X, Math.Min(r.TopLeft.X, Math.Min(r.TopRight.X, r.BottomRight.X))),
            Math.Min(r.BottomLeft.Y, Math.Min(r.TopLeft.Y, Math.Min(r.TopRight.Y, r.BottomRight.Y))),
            Math.Max(r.BottomLeft.X, Math.Max(r.TopLeft.X, Math.Max(r.TopRight.X, r.BottomRight.X))),
            Math.Max(r.BottomLeft.Y, Math.Max(r.TopLeft.Y, Math.Max(r.TopRight.Y, r.BottomRight.Y))));
    }

    internal static DocumentParseResult Convert(string source, string output, bool writeImages, CancellationToken cancellationToken,
        string? checkpointDirectory = null, Action<PdfParseProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 不覆盖已有结果，不删除调用方目录（项目导入会将 source.pdf 放在同目录）。
        if (File.Exists(Path.Combine(output, "document.md")) || File.Exists(Path.Combine(output, "document.txt")) || Directory.Exists(Path.Combine(output, "assets")))
            throw new InvalidDataException("PDF 解析输出目录已包含结果，请为本次解析创建新的临时目录。");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var checkpoints = checkpointDirectory is null ? null : new PdfPageCheckpoints(checkpointDirectory, input, writeImages, cancellationToken);
        using var document = PdfDocument.Open(input);
        if (document.NumberOfPages is < 1 or > MaxPages) throw new InvalidDataException($"PDF 页数必须为 1–{MaxPages} 页。");
        var result = new DocumentParseResult { Engine = DocumentParsingService.Engine, Action = "parse_pdf", PageCount = document.NumberOfPages };
        var outlines = document.TryGetBookmarks(out var bookmarks) ? bookmarks.GetNodes().OfType<DocumentBookmarkNode>()
            .Where(x => x.PageNumber > 0 && x.PageNumber <= document.NumberOfPages && !string.IsNullOrWhiteSpace(x.Title)).ToList() : [];
        var minLevel = outlines.Count == 0 ? 0 : outlines.Min(x => x.Level);
        double? documentBodySize = outlines.Count > 0 ? 0 : null;
        var imagePaths = new HashSet<string>(StringComparer.Ordinal);
        var markdown = new StringBuilder();
        var text = new StringBuilder();
        var scanned = new List<int>();
        var failures = new List<int>();
        string? firstError = null;
        var duplicateImages = 0;
        var headingCount = 0;
        var completed = 0;
        var reused = 0;
        var startedExtraction = false;
        var lastReportedBatch = 0;
        var lastReportedAt = Stopwatch.GetTimestamp();
        var total = document.NumberOfPages;
        Directory.CreateDirectory(output);
        using var renderInput = writeImages ? new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

        void SaveImage(string relative, byte[] bytes)
        {
            if (checkpoints is not null)
            {
                checkpoints.SaveImage(relative, bytes, cancellationToken);
                return;
            }
            var path = Path.Combine(output, relative);
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteNew(path, bytes, cancellationToken);
        }

        void Report(int number, bool force = false)
        {
            if (progress is null) return;
            var state = new PdfParseProgress(number, total, completed, reused, failures.Count);
            if (!force && state.Batch == lastReportedBatch && Stopwatch.GetElapsedTime(lastReportedAt).TotalMilliseconds < 750) return;
            lastReportedBatch = state.Batch;
            lastReportedAt = Stopwatch.GetTimestamp();
            progress(state);
        }

        for (var number = 1; number <= total; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = checkpoints?.ReadPage(number, cancellationToken);
            var restored = page is not null;
            if (page is null)
            {
                Report(number, force: !startedExtraction);
                startedExtraction = true;
                cancellationToken.ThrowIfCancellationRequested();
                documentBodySize ??= checkpoints is null ? DetectBodySize(document, false, cancellationToken)
                    : checkpoints.ResolveBodySize(() => DetectBodySize(document, true, cancellationToken), cancellationToken);
                try
                {
                    page = ExtractPage(document, renderInput, number, outlines, minLevel, documentBodySize.Value, SaveImage, cancellationToken);
                }
                catch (Exception ex) when (checkpoints is not null && IsPageFailure(ex))
                {
                    failures.Add(number);
                    firstError ??= ex is InvalidDataException ? ex.Message : "页面结构或图片无法读取。";
                    Report(number);
                    continue;
                }
                checkpoints?.SavePage(page, cancellationToken);
            }
            else reused++;
            completed++;
            if (restored) Report(number);
            markdown.Append(page.Markdown);
            text.Append(page.Text);
            headingCount += page.Headings;
            if (page.Scanned) scanned.Add(number);
            foreach (var asset in page.Assets)
            {
                if (imagePaths.Add(asset.Path)) result.Assets.Add(asset);
                else duplicateImages++;
            }
            if (markdown.Length > MaxTextCharacters || text.Length > MaxTextCharacters)
                throw new InvalidDataException("PDF 正文超过本地解析长度上限。");
        }

        Report(total, force: true);
        if (failures.Count > 0)
        {
            var pages = string.Join("、", failures.Take(8)) + (failures.Count > 8 ? "等" : "");
            throw new InvalidDataException($"已保留 {completed}/{total} 页。第 {pages} 页未完成（共 {failures.Count} 页）。{firstError} 重试时仅处理未完成的页面。");
        }
        if (checkpoints is not null)
            foreach (var asset in result.Assets) checkpoints.CopyImage(asset.Path, output, cancellationToken);
        if (scanned.Count > 0) result.Warnings.Add($"{scanned.Count} 页无可提取文本层，尚未 OCR；已{(writeImages ? "保留页面图片" : "跳过图片导出")}。");
        result.Details["scanned_pages"] = scanned;
        result.Details["python_required"] = false;
        result.Details["heading_source"] = outlines.Count > 0 ? "bookmarks" : "font";
        result.Details["headings_found"] = headingCount;
        result.Details["images_deduplicated"] = duplicateImages;
        result.MarkdownPath = Path.Combine(output, "document.md");
        result.TextPath = Path.Combine(output, "document.txt");
        WriteNew(result.MarkdownPath, Encoding.UTF8.GetBytes(markdown.ToString()), cancellationToken);
        WriteNew(result.TextPath, Encoding.UTF8.GetBytes(text.ToString()), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        result.Ok = true;
        return result;
    }

    private static bool IsPageFailure(Exception error) =>
        error is InvalidDataException or PdfDocumentFormatException or PDFtoImage.Exceptions.PdfException
            or ArgumentException or InvalidOperationException or NotSupportedException;

    private static PdfPageResult ExtractPage(PdfDocument document, Stream? renderInput, int number,
        IReadOnlyList<DocumentBookmarkNode> outlines, int minLevel, double documentBodySize,
        Action<string, byte[]> saveImage, CancellationToken cancellationToken)
    {
        var markdown = new StringBuilder();
        var text = new StringBuilder();
        var headingCount = 0;
        var assets = new List<DocumentParseAsset>();
        var page = document.GetPage(number);
        var visible = page.CropBox.GetVisibleBounds(page.Rotation);
        var width = visible.Width;
        var height = visible.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw new InvalidDataException("PDF 页面尺寸无效。");
        if (page.Letters.Count > MaxPageLetters || page.Paths.Count > MaxPagePaths) throw new InvalidDataException($"PDF 第 {number} 页版面过于复杂，超过本地解析上限。");
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToArray();
        var imageRegions = renderInput is not null && words.Length > 0
            ? page.GetImages().Select(image => new Region(Box.From(image.BoundingBox).Clip(width, height), "image"))
                .Where(region => region.Bounds.Width >= 3 && region.Bounds.Height >= 3).ToList()
            : [];
        var paths = page.Paths.Where(p => !p.IsClipping && (p.IsFilled || p.IsStroked))
            .SelectMany(p => p).Select(p => p.GetBoundingRectangle()).Where(r => r.HasValue).Select(r => Box.From(r!.Value))
            .Where(b => b.Finite).ToList();
        if (paths.Count > MaxPagePaths) throw new InvalidDataException($"PDF 第 {number} 页图形过于复杂。");
        var tables = FindTables(paths, words, cancellationToken);
        var blocks = new List<Block>();
        blocks.AddRange(tables.Select(t => new Block(t.Bounds, t.Markdown)));
        var bodySize = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).GroupBy(l => Math.Round(l.FontSize, 1))
            .OrderByDescending(g => g.Sum(l => l.Value.Length)).Select(g => g.Key).FirstOrDefault(12);
        if (documentBodySize > 0) bodySize = documentBodySize;
        var lines = DocstrumBoundingBoxes.Instance.GetBlocks(words).SelectMany(b => b.TextLines).ToList();
        var pageOutlines = outlines.Where(o => o.PageNumber == number).ToList();
        var useOcrHeading = outlines.Count == 0 && page.NumberOfImages > 0 &&
            (page.Operations.OfType<SetTextRenderingMode>().Any(op => op.Mode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip) ||
             imageRegions.Any(region => region.Bounds.Width >= width * .9 && region.Bounds.Height >= height * .9));
        var scanHeadingWords = useOcrHeading ? AddScanHeading(words, height, bodySize, blocks, cancellationToken) : [];
        if (scanHeadingWords.Count > 0) headingCount++;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = Box.From(line.BoundingBox);
            var remaining = line.Words.Where(w => !tables.Any(t => t.Bounds.Contains(Box.From(w.BoundingBox)))).ToArray();
            if (remaining.Length == 0) continue;
            text.AppendLine(string.Join(" ", remaining.Select(w => w.Text)).Trim());
            if (scanHeadingWords.Count > 0) remaining = remaining.Where(w => !scanHeadingWords.Contains(w)).ToArray();
            if (remaining.Length == 0) continue;
            var value = string.Join(" ", remaining.Select(w => w.Text)).Trim();
            var outline = pageOutlines.FirstOrDefault(o => Normalize(o.Title) == Normalize(value));
            var size = remaining.SelectMany(w => w.Letters).Select(l => l.FontSize).DefaultIfEmpty(bodySize).Average();
            var level = outline is not null ? Math.Clamp(outline.Level - minLevel + 1, 1, 6)
                : outlines.Count == 0 && !useOcrHeading && value.Length < 180 && size >= bodySize * 1.18 &&
                    value.Any(char.IsLetter) && !PageLabel.IsMatch(value)
                    ? (size >= bodySize * 1.8 ? 1 : size >= bodySize * 1.4 ? 2 : 3) : 0;
            if (outline is not null) pageOutlines.Remove(outline);
            if (level > 0) headingCount++;
            blocks.Add(new Block(bounds, (level > 0 ? new string('#', level) + " " : "") + EscapeText(value)));
        }
        foreach (var table in tables) text.AppendLine(table.Markdown);
        // 书签名称与印刷标题不一致时保留页级入口，原文字也完整保留。
        foreach (var outline in pageOutlines)
        {
            headingCount++;
            blocks.Add(new Block(new Box(0, height, width, height), new string('#', Math.Clamp(outline.Level - minLevel + 1, 1, 6)) + " " + EscapeText(outline.Title)));
        }
        var regions = new List<Region>();
        if (words.Length == 0)
        {
            blocks.Add(new Block(new Box(0, height, width, height), $"> 第 {number} 页无可提取文本层，尚未 OCR。"));
            if (renderInput is not null) regions.Add(new(new Box(0, 0, width, height), "page"));
        }
        else if (renderInput is not null)
        {
            regions.AddRange(imageRegions);
            foreach (var bounds in Merge(paths.Where(b => !tables.Any(t => t.Bounds.Contains(b)) && !(b.Width > width * .95 && b.Height > height * .95)).ToList(), cancellationToken))
                if (bounds.Width >= 12 && bounds.Height >= 12 && !regions.Any(r => r.Bounds.Contains(bounds))) regions.Add(new(bounds.Clip(width, height, 3), "vector"));
            regions = MergeRegions(regions, cancellationToken);
        }
        if (regions.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scale = Math.Min(2, Math.Min(4096 / Math.Max(width, height), Math.Sqrt(4_000_000 / (width * height))));
            var pixelWidth = Math.Max(1, (int)Math.Floor(width * scale));
            var pixelHeight = Math.Max(1, (int)Math.Floor(height * scale));
            renderInput!.Position = 0;
            using var bitmap = RenderPage(renderInput, number - 1, pixelWidth, pixelHeight);
            cancellationToken.ThrowIfCancellationRequested();
            var index = 0;
            foreach (var region in regions.OrderByDescending(r => r.Bounds.Top).ThenBy(r => r.Bounds.Left))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var b = region.Bounds.Clip(width, height, 1);
                var rectangle = new SKRectI(Math.Clamp((int)Math.Floor(b.Left / width * bitmap.Width), 0, bitmap.Width - 1),
                    Math.Clamp((int)Math.Floor((height - b.Top) / height * bitmap.Height), 0, bitmap.Height - 1),
                    Math.Clamp((int)Math.Ceiling(b.Right / width * bitmap.Width), 1, bitmap.Width),
                    Math.Clamp((int)Math.Ceiling((height - b.Bottom) / height * bitmap.Height), 1, bitmap.Height));
                using var cropped = new SKBitmap();
                if (!bitmap.ExtractSubset(cropped, rectangle)) throw new InvalidDataException("PDF 图片区域裁切失败。");
                using var image = SKImage.FromBitmap(cropped);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("PDF 图片编码失败。");
                var bytes = encoded.ToArray();
                index++;
                var hash = System.Convert.ToHexStringLower(SHA256.HashData(bytes));
                var relative = $"assets/{hash}.png";
                saveImage(relative, bytes);
                assets.Add(new DocumentParseAsset { Path = relative, Page = number, Kind = region.Kind, Sha256 = hash });
                blocks.Add(new Block(b, $"![第 {number} 页图 {index}]({relative})"));
            }
        }
        markdown.AppendLine($"<!-- page: {number} -->").AppendLine();
        foreach (var block in blocks.OrderByDescending(b => Math.Round(b.Bounds.Top / 3)).ThenBy(b => b.Bounds.Left))
            markdown.AppendLine(block.Markdown).AppendLine();
        text.AppendLine();
        return new PdfPageResult(number, markdown.ToString(), text.ToString(), headingCount, words.Length == 0, assets);
    }

    private static HashSet<Word> AddScanHeading(Word[] words, double height, double bodySize, List<Block> blocks, CancellationToken ct)
    {
        // Hidden OCR glyphs model positions, not typography. Infer one strong title at the top of a scan.
        var candidates = new List<ScanWord>();
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();
            var bounds = Box.From(word.BoundingBox);
            var fromTop = (height - bounds.Top) / height;
            if (fromTop is < .042 or > .40 || word.Letters.Count == 0) continue;
            var size = word.Letters.Average(letter => letter.FontSize);
            if (size < Math.Max(16, bodySize * (fromTop > .22 ? 2.35 : 1.75))) continue;
            var first = word.Letters[0].StartBaseLine;
            var last = word.Letters[^1].EndBaseLine;
            if (Math.Abs(first.Y - last.Y) > Math.Max(2, Math.Abs(first.X - last.X) * .12)) continue;
            candidates.Add(new(word, bounds, (first.Y + last.Y) / 2, size));
        }

        var titleLines = new List<ScanLine>();
        var ordered = candidates.OrderByDescending(word => word.Baseline).ToArray();
        for (var start = 0; start < ordered.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var anchor = ordered[start];
            var end = start + 1;
            while (end < ordered.Length && anchor.Baseline - ordered[end].Baseline <= anchor.Size * .25) end++;
            var row = ordered[start..end].OrderBy(word => word.Bounds.Left).ToList();
            var value = string.Join(" ", row.Select(word => word.Word.Text)).Trim();
            if (IsScanTitle(value))
                titleLines.Add(new(row, row.Select(word => word.Bounds).Aggregate((left, right) => left.Union(right)), anchor.Baseline,
                    row.Average(word => word.Size), value));
            start = end;
        }
        // A lowercase OCR fragment can continue a title, but cannot start one on its own.
        var firstTitle = titleLines.FindIndex(line => !char.IsLower(line.Text[0]));
        if (firstTitle < 0) return [];

        var title = titleLines[firstTitle];
        var used = title.Words.Select(word => word.Word).ToHashSet();
        var boundsOfTitle = title.Bounds;
        var heading = title.Text;
        var previous = title;
        foreach (var line in titleLines.Skip(firstTitle + 1))
        {
            var sizeRatio = line.Size / previous.Size;
            var distance = (previous.Baseline - line.Baseline) / Math.Max(previous.Size, line.Size);
            var aligned = Math.Abs(title.Bounds.Left - line.Bounds.Left) < title.Size ||
                Math.Abs(title.Bounds.Left + title.Bounds.Right - line.Bounds.Left - line.Bounds.Right) < title.Size * 2;
            if (!aligned || sizeRatio is < .75 or > 1.35 || distance is < .55 or > 1.65 || heading.Length + line.Text.Length > 140) break;
            heading += " " + line.Text;
            boundsOfTitle = boundsOfTitle.Union(line.Bounds);
            used.UnionWith(line.Words.Select(word => word.Word));
            previous = line;
        }
        blocks.Add(new(boundsOfTitle, "# " + EscapeText(heading)));
        return used;
    }

    private static bool IsScanTitle(string text)
    {
        if (text.Length is 0 or > 140 || !char.IsLetterOrDigit(text[0]) || text.EndsWith(';') ||
            text.Count(c => c == '|') > 1 || PageLabel.IsMatch(text) || CaptionLabel.IsMatch(text)) return false;
        return text.Count(char.IsLetter) >= 3 || text.Count(c => char.GetUnicodeCategory(c) == UnicodeCategory.OtherLetter) >= 2;
    }

    private static void WriteNew(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static double DetectBodySize(PdfDocument document, bool skipUnreadablePages, CancellationToken ct)
    {
        var sizes = new Dictionary<double, long>();
        // Evenly sample at most 32 pages so a title-only cover does not become its own body font.
        var count = Math.Min(32, document.NumberOfPages);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var number = count == 1 ? 1 : 1 + (int)((long)i * (document.NumberOfPages - 1) / (count - 1));
            Page page;
            try { page = document.GetPage(number); }
            catch (Exception ex) when (skipUnreadablePages && IsPageFailure(ex)) { continue; }
            if (page.Letters.Count > MaxPageLetters)
            {
                if (skipUnreadablePages) continue;
                throw new InvalidDataException("PDF 页面文字过于复杂。");
            }
            foreach (var letter in page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)))
            {
                var size = Math.Round(letter.FontSize * 2) / 2;
                if (double.IsFinite(size) && size > 0) sizes[size] = sizes.GetValueOrDefault(size) + letter.Value.Length;
            }
        }
        return sizes.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
    }
    private static string EscapeText(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]").Replace("<", "&lt;").Replace(">", "&gt;");
        // OCR can turn a printed bullet into a hash. Only inferred titles and bookmarks create Markdown headings.
        return escaped.StartsWith('#') ? "\\" + escaped : escaped;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
        Justification = "PDFtoImage uses noncanonical Linux/macOS platform annotations; runtime OS guard below restricts server platforms.")]
    internal static SKBitmap RenderPage(Stream stream, int page, int width, int height)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("PDF 渲染仅支持 Windows、Linux 和 macOS 服务端。");
        return PDFtoImage.Conversion.ToImage(stream, page: page, leaveOpen: true,
            options: new PDFtoImage.RenderOptions(Width: width, Height: height, BackgroundColor: SKColors.White));
    }

    private static List<Box> Merge(List<Box> input, CancellationToken cancellationToken)
    {
        var result = new List<Box>();
        foreach (var candidate in input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var merged = candidate;
            for (var i = 0; i < result.Count; i++)
                if (result[i].Touches(merged)) { merged = merged.Union(result[i]); result.RemoveAt(i); i = -1; }
            result.Add(merged);
        }
        return result;
    }

    private static List<Region> MergeRegions(List<Region> input, CancellationToken cancellationToken)
    {
        var result = new List<Region>();
        foreach (var candidate in input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var merged = candidate;
            for (var i = 0; i < result.Count; i++)
                if (result[i].Bounds.Touches(merged.Bounds, 1))
                {
                    merged = new(merged.Bounds.Union(result[i].Bounds), merged.Kind == "image" || result[i].Kind == "image" ? "image" : "vector");
                    result.RemoveAt(i); i = -1;
                }
            result.Add(merged);
        }
        return result;
    }

    private static List<Table> FindTables(List<Box> paths, Word[] words, CancellationToken cancellationToken)
    {
        var tables = new List<Table>();
        // 仅识别完整横纵网格；无框线/合并单元格保留原文字，避免猜测吞字。
        var horizontal = paths.Where(b => b.Height <= 1 && b.Width >= 20).ToList();
        var vertical = paths.Where(b => b.Width <= 1 && b.Height >= 15).ToList();
        foreach (var cluster in Merge(horizontal.Concat(vertical).ToList(), cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xs = DistinctCoordinates(vertical.Where(cluster.Contains).Select(b => b.Left));
            var ys = DistinctCoordinates(horizontal.Where(cluster.Contains).Select(b => b.Bottom)).OrderByDescending(x => x).ToArray();
            if (xs.Length < 3 || ys.Length < 3 || xs.Length > 40 || ys.Length > 200) continue;
            if (horizontal.Count(b => cluster.Contains(b) && b.Left <= xs[0] + 1 && b.Right >= xs[^1] - 1) != ys.Length
                || vertical.Count(b => cluster.Contains(b) && b.Bottom <= ys[^1] + 1 && b.Top >= ys[0] - 1) != xs.Length) continue;
            var cells = new List<string[]>();
            for (var row = 0; row < ys.Length - 1; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new string[xs.Length - 1];
                for (var col = 0; col < xs.Length - 1; col++)
                {
                    var cell = new Box(xs[col], ys[row + 1], xs[col + 1], ys[row]);
                    values[col] = string.Join(" ", words.Where(w => cell.Contains(Box.From(w.BoundingBox)))
                        .OrderByDescending(w => Math.Round(w.BoundingBox.Top / 3)).ThenBy(w => w.BoundingBox.Left).Select(w => EscapeText(w.Text).Replace("|", "\\|")));
                }
                cells.Add(values);
            }
            var within = words.Where(w => cluster.Contains(Box.From(w.BoundingBox))).ToList();
            if (within.Count == 0 || within.Any(w => !Enumerable.Range(0, ys.Length - 1).Any(row => Enumerable.Range(0, xs.Length - 1)
                .Any(col => new Box(xs[col], ys[row + 1], xs[col + 1], ys[row]).Contains(Box.From(w.BoundingBox)))))) continue;
            var md = new StringBuilder().AppendLine("| " + string.Join(" | ", cells[0]) + " |")
                .AppendLine("| " + string.Join(" | ", cells[0].Select(_ => "---")) + " |");
            foreach (var row in cells.Skip(1)) md.AppendLine("| " + string.Join(" | ", row) + " |");
            tables.Add(new Table(cluster, md.ToString().TrimEnd()));
        }
        return tables;
    }

    private static double[] DistinctCoordinates(IEnumerable<double> coordinates)
    {
        var result = new List<double>();
        foreach (var value in coordinates.OrderBy(x => x)) if (result.Count == 0 || value - result[^1] > 1) result.Add(value);
        return result.ToArray();
    }
}
