using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace AiAgent.Backend.Services.Chat;

public sealed partial class ChatFileAttachmentService
{
    private const long MaxExpandedBytes = 100L * 1024 * 1024;
    private const long MaxEntryBytes = 20L * 1024 * 1024;
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".js", ".jsx", ".ts", ".tsx", ".py", ".java", ".c", ".cpp", ".h", ".hpp", ".json", ".xml", ".yaml", ".yml", ".sql", ".css", ".csproj", ".sln", ".config", ".ini", ".sh", ".ps1" };

    private static void ValidateArchive(string path, string extension)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        var count = stream.Read(header);
        var valid = extension == ".zip"
            ? count >= 4 && header[0] == 0x50 && header[1] == 0x4b && (header[2], header[3]) is (3, 4) or (5, 6)
            : count >= 7 && (header[..7].SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 0 })
                || count == 8 && header.SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0 }));
        if (!valid) throw new InvalidOperationException("文件内容与 ZIP/RAR 格式不符。");
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path);
            long total = 0;
            var entries = 0;
            foreach (var entry in archive.Entries)
            {
                if (++entries > 1000 || entry.Size < 0 || entry.Size > MaxEntryBytes || entry.Size > MaxExpandedBytes - total)
                    throw new InvalidOperationException("压缩包超出限制：最多 1000 项、单文件 20 MB、展开总计 100 MB。");
                total += entry.Size;
                if (entry.IsEncrypted) throw new InvalidOperationException("暂不支持加密压缩包，请解密后上传。");
                var name = (entry.Key ?? "").Replace('\\', '/');
                if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains("..") || !string.IsNullOrEmpty(entry.LinkTarget))
                    throw new InvalidOperationException("压缩包包含不安全路径或链接。");
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new InvalidOperationException("无法读取压缩包，请检查文件是否完整、未加密且不是分卷包。", ex); }
    }

    private async Task<string> ReadArchiveTextAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateArchive(path, Path.GetExtension(path).ToLowerInvariant());
        using var archive = ArchiveFactory.OpenArchive(path);
        var text = new StringBuilder("压缩包静态解析（不执行文件；嵌套压缩包不展开）。\n# 文件清单\n");
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            text.AppendLine($"{entry.Key} ({entry.Size} bytes)");
            if (text.Length > MaxCharactersPerFile) return text.ToString();
        }
        var parseRoot = Path.Combine(RootPath, "parse");
        var temporary = Path.Combine(parseRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        long expanded = 0;
        try
        {
            using var reader = ReaderFactory.OpenReader(path);
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;
                cancellationToken.ThrowIfCancellationRequested();
                if (text.Length > MaxCharactersPerFile) break;
                var extension = Path.GetExtension(entry.Key ?? "").ToLowerInvariant();
                text.AppendLine($"\n# 文件：{entry.Key}");
                var definition = GetDefinition(extension);
                if (extension is ".zip" or ".rar" || (definition?.ExtractionStatus != "ready" && !SourceExtensions.Contains(extension)))
                { text.AppendLine("[仅列出文件，未解析此格式]"); continue; }
                // Never use archive names as filesystem paths, including for document parsers.
                var target = Path.Combine(temporary, Guid.NewGuid().ToString("N") + extension);
                using (var source = reader.OpenEntryStream())
                await using (var output = File.Create(target))
                {
                    await CopyWithLimitAsync(source, output, Math.Min(MaxEntryBytes, MaxExpandedBytes - expanded), cancellationToken);
                    expanded += output.Length;
                }
                try
                {
                    if (definition != null) ValidateStoredFile(target, definition);
                    var content = SourceExtensions.Contains(extension) ? await ReadUtf8Async(target, cancellationToken) : await ExtractTextAsync(target, cancellationToken);
                    text.AppendLine(Limit(content, Math.Max(1, MaxCharactersPerFile - text.Length)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or DecoderFallbackException or BadImageFormatException)
                { text.AppendLine("[无法解析：文件格式无效、编码不支持或解析器不可用]"); }
                finally { TryDelete(target); }
            }
            return text.ToString();
        }
        finally { TryDeleteDirectory(temporary, parseRoot); }
    }

    private static string ReadDllText(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            // .NET application assemblies also use .dll, without the native DLL flag.
            if (pe.PEHeaders.PEHeader == null || (!pe.HasMetadata && !pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)))
                throw new InvalidOperationException("文件不是有效的 Windows DLL。");
            var text = new StringBuilder("DLL 静态元数据；未加载或执行，不代表完整源码或运行行为。\n");
            text.AppendLine($"架构：{pe.PEHeaders.CoffHeader.Machine}");
            foreach (var section in pe.PEHeaders.SectionHeaders) text.AppendLine($"节：{section.Name}，大小：{section.SizeOfRawData}");
            if (pe.HasMetadata)
            {
                var metadata = pe.GetMetadataReader();
                if (metadata.IsAssembly)
                {
                    var assembly = metadata.GetAssemblyDefinition();
                    text.AppendLine($".NET 程序集：{metadata.GetString(assembly.Name)} {assembly.Version}");
                }
                foreach (var handle in metadata.AssemblyReferences)
                {
                    var reference = metadata.GetAssemblyReference(handle);
                    text.AppendLine($"依赖：{metadata.GetString(reference.Name)} {reference.Version}");
                    if (text.Length > 40_000) return text.ToString();
                }
                foreach (var handle in metadata.TypeDefinitions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var type = metadata.GetTypeDefinition(handle);
                    text.AppendLine($"类型：{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}");
                    foreach (var methodHandle in type.GetMethods())
                    {
                        var method = metadata.GetMethodDefinition(methodHandle);
                        text.AppendLine($"  方法：{metadata.GetString(method.Name)} [{method.Attributes}]");
                        if (text.Length > 40_000) return text.ToString();
                    }
                    if (text.Length > 40_000) break;
                }
            }
            else text.AppendLine("原生 DLL：仅提供 PE 架构与节信息；未反编译函数实现。");
            return text.ToString();
        }
        catch (BadImageFormatException ex) { throw new InvalidOperationException("DLL 的 PE 或 .NET 元数据无效。", ex); }
    }
}
