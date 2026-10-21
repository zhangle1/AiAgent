using AiAgent.Backend.Dtos.KunBuddy;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.KunBuddy;

/// <summary>Append-only original conversation material. No parser, model or task queue is involved.</summary>
public sealed class KunBuddySessionService(ISqlSugarClient database, IKnowledgePathService paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private const string StoreName = KnowledgeInternalStores.Sessions;
    private const long MaxDocumentBytes = 128L * 1024 * 1024;

    public async Task<KunBuddySessionReceipt> UploadAsync(AuthenticatedUser user, string sessionId, KunBuddySessionUpload input, CancellationToken ct)
    {
        ValidateId(sessionId); ValidateId(input.ClientId);
        if (input.Title is null || input.Title.Length > 512 || input.Entries is null || input.Entries.Count is < 1 or > 64) throw new ArgumentException("Invalid conversation batch.");
        long total = 0;
        foreach (var entry in input.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 256 || entry.Id.Any(char.IsControl) || entry.Markdown is null) throw new ArgumentException("Invalid message identifier.");
            total += Encoding.UTF8.GetByteCount(entry.Markdown);
            if (Encoding.UTF8.GetByteCount(entry.Markdown) > 512 * 1024 || total > 2 * 1024 * 1024) throw new ArgumentException("Conversation batch is too large.");
        }
        var owner = KnowledgeUserScope.RootFor(user.Username, user.Id);
        var identity = Hash(Encoding.UTF8.GetBytes(input.ClientId + "\n" + sessionId));
        var uri = owner + "sessions/" + identity + ".md";
        await _gate.WaitAsync(ct);
        try
        {
            using var db = database.CopyNew();
            var kb = KnowledgeInternalStores.EnsureSessions(db, paths);
            var document = db.Queryable<AiKnowledgeDocument>().First(row => row.ResourceUri == uri && !row.IsDeleted);
            var state = new SessionState();
            var title = OneLine(input.Title);
            var markdown = $"# {title}\n\n- 来源：KunBuddy 坤伴订阅\n- 用户：{OneLine(user.Username)}\n- 会话：{sessionId}\n\n> 原始会话记录；未调用 LLM、未自动解析或编译。\n\n";
            if (document is not null)
            {
                EnsureSafe(document.StoragePath);
                EnsureSafe(document.StoragePath + ".json");
                markdown = await File.ReadAllTextAsync(document.StoragePath, Encoding.UTF8, ct);
                state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(document.StoragePath + ".json", ct))
                    ?? throw new InvalidDataException("Conversation state is unavailable.");
            }
            var addition = new StringBuilder();
            var appended = 0;
            foreach (var entry in input.Entries)
            {
                var messageId = Hash(Encoding.UTF8.GetBytes(entry.Id));
                var contentHash = Hash(Encoding.UTF8.GetBytes(entry.Markdown));
                if (!state.Messages.TryGetValue(messageId, out var versions)) state.Messages[messageId] = versions = [];
                if (versions.Contains(contentHash)) continue;
                if (versions.Count > 0) addition.Append("\n> 原消息修订，保留前一版本。\n\n");
                addition.Append(entry.Markdown);
                versions.Add(contentHash); appended++;
            }
            if (appended == 0 && document is not null) return Receipt(document, uri, 0);
            markdown += addition.ToString();
            var bytes = Encoding.UTF8.GetBytes(markdown);
            if (bytes.LongLength > MaxDocumentBytes) throw new ArgumentException("Conversation exceeds the 128 MiB raw-material limit.");
            var destination = Path.Combine(paths.GetRawPath(kb.Name), "kunbuddy-sessions", Hash(Encoding.UTF8.GetBytes(user.Id)), identity, Guid.NewGuid().ToString("N"));
            EnsureSafe(destination);
            var temporary = destination + ".tmp";
            Directory.CreateDirectory(temporary);
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(temporary, "conversation.md"), bytes, ct);
                await File.WriteAllTextAsync(Path.Combine(temporary, "conversation.md.json"), JsonSerializer.Serialize(state), new UTF8Encoding(false), ct);
                ct.ThrowIfCancellationRequested();
                Directory.Move(temporary, destination);
            }
            catch
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                throw;
            }
            // Publish only a complete immutable Markdown/state pair. Failed DB writes retain the previous version.
            document ??= new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = uri, CreatedAt = DateTime.UtcNow };
            document.OriginalFileName = SafeTitle(title) + "-" + identity[..12] + ".md";
            document.FileName = identity + ".md"; document.Extension = ".md"; document.ContentType = "text/markdown";
            document.StoragePath = Path.Combine(destination, "conversation.md");
            document.FileSize = bytes.LongLength; document.FileHash = Hash(bytes);
            document.Status = "uploaded"; document.ParserType = "auto"; document.ErrorMessage = null; document.UpdatedAt = DateTime.UtcNow;
            if (document.Id == 0) document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
            else db.Updateable(document).ExecuteCommand();
            return Receipt(document, uri, appended);
        }
        finally { _gate.Release(); }
    }

    private void EnsureSafe(string path)
    {
        var root = Path.GetFullPath(paths.GetKnowledgeBasePath(StoreName)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new UnauthorizedAccessException("Conversation path is outside storage.");
        for (var current = new DirectoryInfo(Path.GetDirectoryName(full)!) as FileSystemInfo; current is not null; current = (current as DirectoryInfo)?.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Conversation storage cannot use symbolic links.");
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Conversation file cannot use symbolic links.");
    }
    private static void ValidateId(string value)
    {
        if (value is null || value.Length is < 1 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid conversation identity.");
    }
    private static string OneLine(string value) => string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim() is { Length: > 0 } title ? title : "未命名会话";
    private static string SafeTitle(string value) => string.Concat(value.Take(96).Select(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' or '?' or '#' or '%' ? '_' : c));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static KunBuddySessionReceipt Receipt(AiKnowledgeDocument doc, string uri, int appended) => new() { Uri = uri, DocumentId = doc.Id, Appended = appended, Sha256 = doc.FileHash ?? "" };
    public sealed class SessionState { public Dictionary<string, HashSet<string>> Messages { get; set; } = []; }
}
