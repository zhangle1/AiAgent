using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace AiAgent.Backend.Services.Parsing;

internal static class DocumentTextConverters
{
    internal static async Task<string> ReadAsync(string path, CancellationToken ct)
        => (await ReadWithEncodingAsync(path, ct)).Text;

    internal sealed record DecodedText(string Text, string Encoding, bool Inferred);

    internal static async Task<DecodedText> ReadWithEncodingAsync(string path, CancellationToken ct)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("文本输入超过 64 MiB 上限。");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        Encoding encoding = new UTF8Encoding(false, true); var skip = 0; var inferred = false;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        string text;
        try { text = encoding.GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) when (skip == 0)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            try { text = encoding.GetString(bytes); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("文本编码无法识别，请保存为 UTF-8 后重试。", ex); }
            // GBK is a subset of GB18030. Without a BOM, require positive Chinese evidence, not a Latin-1 catch-all.
            if (!text.Any(c => c is >= '\u3400' and <= '\u9fff')) throw new InvalidDataException("文本编码不明确，请保存为 UTF-8 后重试。");
            inferred = true;
        }
        ct.ThrowIfCancellationRequested();
        if (text.Length > DocumentConversionService.MaxCharacters) throw new InvalidDataException("文本正文长度超限。");
        if (text.Contains('\0') || text.StartsWith("%PDF-", StringComparison.Ordinal) || text.StartsWith("PK\u0003\u0004", StringComparison.Ordinal))
            throw new InvalidDataException("文档包含二进制内容，不能按文本读取。");
        if (text.Count(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t' and not '\f') > 0) throw new InvalidDataException("文本包含异常控制字符，不能按正文读取。");
        return new(text, encoding.WebName, inferred);
    }

    internal static string Table(IEnumerable<IReadOnlyList<string>> rows, CancellationToken ct)
    {
        var result = new StringBuilder(); var columns = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (columns == 0)
            {
                columns = row.Count;
                if (columns is 0 or > 16384) throw new InvalidDataException("表格列数超限。");
                result.AppendLine("| " + string.Join(" | ", Enumerable.Range(1, columns).Select(i => "列 " + i)) + " |");
                result.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |");
            }
            result.AppendLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
            if (result.Length > DocumentConversionService.MaxCharacters) throw new InvalidDataException("表格正文长度超限。");
        }
        return result.ToString();
    }

    internal static string Delimited(string text, char separator, CancellationToken ct)
    {
        // 先解析字段行再统一列宽，支持引号内分隔符、换行、双引号转义和空字段。
        var rows = new List<IReadOnlyList<string>>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closed = false; var started = false;
        var cells = 0;
        void FinishField()
        {
            if (++cells > 2_000_000) throw new InvalidDataException("CSV 单元格数量超限。");
            row.Add(field.ToString()); field.Clear();
        }
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            var c = text[i];
            if (row.Count > 16384 || rows.Count > 1000000) throw new InvalidDataException("CSV 行数或列数超限。");
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                else field.Append(c);
                continue;
            }
            if (c == '"' && !started && field.Length == 0) { quoted = true; started = true; continue; }
            if (c == separator) { FinishField(); closed = false; started = false; continue; }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                FinishField(); rows.Add(row); row = []; closed = false; started = false; continue;
            }
            if (closed || c == '"') throw new InvalidDataException("CSV 引号或字段边界无效。");
            field.Append(c); started = true;
        }
        if (quoted) throw new InvalidDataException("CSV 引号未闭合。");
        if (field.Length > 0 || row.Count > 0 || started) { FinishField(); rows.Add(row); }
        var columns = rows.Select(r => r.Count).DefaultIfEmpty().Max();
        return Table(rows.Select(r => (IReadOnlyList<string>)r.Concat(Enumerable.Repeat("", columns - r.Count)).ToArray()), ct);
    }

    internal static (string Content, Dictionary<string, object?> Details) Spreadsheet(string path, CancellationToken ct)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path); using var reader = ExcelReaderFactory.CreateReader(stream);
        var output = new StringBuilder(); var sheets = new List<string>();
        do
        {
            ct.ThrowIfCancellationRequested(); sheets.Add(reader.Name); output.AppendLine("# " + reader.Name).AppendLine();
            IEnumerable<IReadOnlyList<string>> Rows()
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    yield return Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i) is DateTime date
                        ? date.ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "").ToArray();
                }
            }
            output.AppendLine(Table(Rows(), ct));
            if (output.Length > DocumentConversionService.MaxCharacters) throw new InvalidDataException("工作簿正文长度超限。");
        } while (reader.NextResult());
        return (output.ToString(), new() { ["sheets"] = sheets });
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\r", "<br>").Replace("\n", "<br>");
}
