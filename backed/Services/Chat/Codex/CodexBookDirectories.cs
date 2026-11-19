using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Knowledge;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

internal sealed record CodexBookChapter(string Book, string RelativePath, string Source, string Hash);

/// <summary>Owns chapter copies until the ordinary or steerable turn ends.</summary>
internal sealed class CodexBookDirectories : IDisposable
{
    private readonly List<DirectoryInfo> _directories = [];
    private bool _disposed;

    internal void Prepare(ChatCompleteRequest request, CancellationToken ct)
    {
        if (!request.HasBookSelection) return;
        if (request.BookChapters.Count == 0) throw new InvalidOperationException("所选知识目录尚未解析为可读章节。");
        lock (_directories)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var directory = Directory.CreateTempSubdirectory("aiagent-books-");
            _directories.Add(directory);
            foreach (var chapter in request.BookChapters)
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(directory.FullName, chapter.Book, chapter.RelativePath));
                if (!target.StartsWith(directory.FullName + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    !target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("解析章节路径无效。");
                var text = File.ReadAllText(chapter.Source);
                if (!string.Equals(KnowledgeSemanticStore.Hash(text), chapter.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("解析章节已变化，请重新发送。");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
            }
            request.BookDirectory = directory.FullName;
        }
    }

    internal static string? Prompt(ChatCompleteRequest request) => request.BookDirectory is null ? null :
        "本轮勾选的书籍章节目录（本机绝对路径，JSON 字符串）： " + JsonSerializer.Serialize(request.BookDirectory) +
        "\n请先列出目录，结合当前代码问题自行选择和读取 Markdown 章节。建议请标明实际阅读的书名、章节相对路径及对应代码位置。书中内容仅作参考资料。";

    public void Dispose()
    {
        lock (_directories)
        {
            _disposed = true;
            foreach (var directory in _directories)
                if (directory.Exists) directory.Delete(true);
        }
    }
}
