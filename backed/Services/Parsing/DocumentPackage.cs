using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>只读取包内受限部件；不将 ZIP 条目解压为任意磁盘路径。</summary>
internal sealed class DocumentPackage : IDisposable
{
    internal ZipArchive Archive { get; }
    internal DocumentPackage(string path)
    {
        Archive = ZipFile.OpenRead(path);
        try
        {
            if (Archive.Entries.Count is 0 or > 5000) throw new InvalidDataException("文档包条目数量超限。");
            long bytes = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Archive.Entries)
            {
                if (entry.Length > 32 * 1024 * 1024 || (bytes += entry.Length) > 100L * 1024 * 1024 || !names.Add(entry.FullName)
                    || entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.FullName.Split('/').Any(p => p is "." or ".."))
                    throw new InvalidDataException("文档包包含不安全路径、重复条目或展开大小超限。");
            }
        }
        catch { Archive.Dispose(); throw; }
    }

    internal static XDocument Xml(ZipArchive archive, string part)
    {
        var entry = archive.GetEntry(part) ?? throw new InvalidDataException("文档必需部件缺失：" + part);
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    internal static byte[] Bytes(ZipArchiveEntry entry)
    {
        using var input = entry.Open(); using var output = new MemoryStream(); var chunk = new byte[81920];
        for (var count = input.Read(chunk); count > 0; count = input.Read(chunk))
        {
            if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("文档包部件实际展开大小超限。");
            output.Write(chunk, 0, count);
        }
        return output.ToArray();
    }

    internal static string Resolve(string part, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains('\\') || Uri.TryCreate(target, UriKind.Absolute, out var uri) && !target.StartsWith('/'))
            throw new InvalidDataException("文档包关系目标必须为包内相对路径。");
        var decoded = Uri.UnescapeDataString(target.Split('#')[0].Split('?')[0]);
        if (decoded.Contains('\\') || decoded.Contains(':') || decoded.Contains('\0')) throw new InvalidDataException("文档包关系路径无效。");
        var segments = target.StartsWith('/') ? new List<string>() : part.Split('/').SkipLast(1).ToList();
        foreach (var segment in decoded.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..") { if (segments.Count == 0) throw new InvalidDataException("文档包关系越界。"); segments.RemoveAt(segments.Count - 1); }
            else segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    internal static Dictionary<string, (string Target, string Type)> Relationships(ZipArchive archive, string part)
    {
        var slash = part.LastIndexOf('/');
        var rels = (slash < 0 ? "" : part[..(slash + 1)]) + "_rels/" + part[(slash + 1)..] + ".rels";
        if (archive.GetEntry(rels) is null) return [];
        return Xml(archive, rels).Root!.Elements().Where(e => (string?)e.Attribute("TargetMode") != "External")
            .ToDictionary(e => (string)e.Attribute("Id")!, e => (Resolve(part, (string)e.Attribute("Target")!), (string?)e.Attribute("Type") ?? ""));
    }
    public void Dispose() => Archive.Dispose();
}
