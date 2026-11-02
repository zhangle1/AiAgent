using AiAgent.Backend.Dtos.KunBuddy;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.Auth;
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
            var source = OneLine(string.IsNullOrWhiteSpace(input.Source) ? "KunBuddy 坤伴订阅" : input.Source);
            var markdown = $"# {title}\n\n- 来源：{source}\n- 用户：{OneLine(user.Username)}\n- 会话：{sessionId}\n\n> 原始会话记录；未调用 LLM、未自动解析或编译。\n\n";
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

    public Task<PlatformSessionImportOptionsDto> ListPlatformSessionsAsync(AuthenticatedUser user, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var db = database.CopyNew();
        var query = db.Queryable<AiChatSession>().Where(item => !item.IsDeleted);
        if (!user.IsAdministrator) query = query.Where(item => item.UserId == user.Id);
        var sessions = query.OrderByDescending(item => item.UpdatedAt).Take(Math.Clamp(limit, 1, 200)).ToList();
        return Task.FromResult(new PlatformSessionImportOptionsDto
        {
            CanImportAll = user.IsAdministrator,
            Sessions = ToPlatformSummaries(db, sessions)
        });
    }

    public async Task<PlatformSessionImportResultDto> ImportPlatformSessionsAsync(AuthenticatedUser requester, IReadOnlyCollection<string> sessionIds, CancellationToken ct)
    {
        if (sessionIds is null) throw new ArgumentException("请选择要导入的平台会话。");
        var normalized = sessionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (normalized.Count == 0) throw new ArgumentException("请至少选择一个平台会话。");
        if (normalized.Count > 100) throw new ArgumentException("每次最多导入 100 个平台会话，请分批选择。");
        ct.ThrowIfCancellationRequested();
        using var db = database.CopyNew();
        var query = db.Queryable<AiChatSession>().Where(item => normalized.Contains(item.Id) && !item.IsDeleted);
        if (!requester.IsAdministrator) query = query.Where(item => item.UserId == requester.Id);
        var sessions = query.ToList();
        if (sessions.Count != normalized.Count) throw new UnauthorizedAccessException("只能导入当前账号有权限查看的平台会话。");
        var userIds = sessions.Select(session => session.UserId).Distinct(StringComparer.Ordinal).ToList();
        var users = db.Queryable<AiUser>().Where(item => userIds.Contains(item.Id)).ToList().ToDictionary(item => item.Id, StringComparer.Ordinal);
        var messages = LoadMessages(db, sessions);
        var result = new PlatformSessionImportResultDto();
        foreach (var session in sessions.OrderBy(item => normalized.IndexOf(item.Id)))
        {
            if (!users.TryGetValue(session.UserId, out var owner)) throw new UnauthorizedAccessException("平台会话所属账号不可用。");
            var ownerUser = new AuthenticatedUser(owner.Id, owner.Username, owner.Role, owner.CanCommitCode, owner.Alias);
            result.AppendedMessages += await SyncPlatformSessionAsync(requester, ownerUser, session, ct);
            result.ImportedSessions++;
            result.Sessions.Add(ToPlatformSummary(session, users, messages.GetValueOrDefault(session.Id) ?? []));
        }
        return result;
    }

    /// <summary>Archives the persisted platform chat into the owner's raw sessions directory.</summary>
    public async Task<int> SyncPlatformSessionAsync(AuthenticatedUser user, string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var db = database.CopyNew();
        var session = db.Queryable<AiChatSession>().First(item => item.Id == sessionId && item.UserId == user.Id && !item.IsDeleted);
        if (session is null) return 0;
        return await SyncPlatformSessionAsync(user, user, session, ct);
    }

    private async Task<int> SyncPlatformSessionAsync(AuthenticatedUser requester, AuthenticatedUser owner, AiChatSession session, CancellationToken ct)
    {
        if (!requester.IsAdministrator && requester.Id != owner.Id) throw new UnauthorizedAccessException("只能导入当前账号有权限查看的平台会话。");
        using var db = database.CopyNew();
        var messages = db.Queryable<AiChatMessage>().Where(item => item.SessionId == session.Id).OrderBy(item => item.Id).ToList();
        var entries = messages.SelectMany(ToPlatformEntries).ToList();
        var appended = 0;
        foreach (var batch in Batch(entries))
        {
            var receipt = await UploadAsync(owner, session.Id, new KunBuddySessionUpload
            {
                Account = owner.Username,
                ClientId = "aiagent-platform",
                Title = session.Title,
                Source = "AiAgent 平台聊天",
                Entries = batch
            }, ct);
            appended += receipt.Appended;
        }
        return appended;
    }

    private static List<PlatformChatSessionSummaryDto> ToPlatformSummaries(ISqlSugarClient db, IReadOnlyCollection<AiChatSession> sessions)
    {
        var userIds = sessions.Select(session => session.UserId).Distinct(StringComparer.Ordinal).ToList();
        var users = db.Queryable<AiUser>().Where(item => userIds.Contains(item.Id)).ToList().ToDictionary(item => item.Id, StringComparer.Ordinal);
        var messages = LoadMessages(db, sessions);
        return sessions.Select(session => ToPlatformSummary(session, users, messages.GetValueOrDefault(session.Id) ?? [])).ToList();
    }

    private static Dictionary<string, List<AiChatMessage>> LoadMessages(ISqlSugarClient db, IReadOnlyCollection<AiChatSession> sessions)
    {
        var sessionIds = sessions.Select(session => session.Id).ToList();
        return (sessionIds.Count == 0 ? [] : db.Queryable<AiChatMessage>().Where(item => sessionIds.Contains(item.SessionId)).OrderByDescending(item => item.Id).ToList())
            .GroupBy(item => item.SessionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    private static PlatformChatSessionSummaryDto ToPlatformSummary(AiChatSession session, IReadOnlyDictionary<string, AiUser> users, IReadOnlyCollection<AiChatMessage> messages)
    {
        users.TryGetValue(session.UserId, out var owner);
        return new PlatformChatSessionSummaryDto
        {
            Id = session.Id,
            Title = session.Title,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            MessageCount = messages.Count,
            LastMessage = messages.FirstOrDefault()?.Content ?? string.Empty,
            ProjectId = session.CodeProjectId,
            SortOrder = session.SortOrder ?? 0,
            Priority = session.Priority ?? "normal",
            IsPinned = session.IsPinned ?? false,
            UserId = session.UserId,
            Username = owner?.Username ?? "未知用户",
            UserAlias = owner?.Alias
        };
    }

    private static IEnumerable<KunBuddySessionEntry> ToPlatformEntries(AiChatMessage message)
    {
        var role = message.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "用户" : message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "AI" : message.Role;
        var text = $"## {role}\n\n{message.Content}\n\n";
        const int maxBytes = 512 * 1024;
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            yield return new KunBuddySessionEntry { Id = $"chat-{message.Id}", Markdown = text };
            yield break;
        }
        var offset = 0;
        var chunk = 0;
        while (offset < text.Length)
        {
            var end = Math.Min(text.Length, offset + maxBytes);
            while (end > offset && Encoding.UTF8.GetByteCount(text[offset..end]) > maxBytes) end--;
            while (end < text.Length && end > offset && char.IsLowSurrogate(text[end])) end--;
            if (end <= offset) throw new ArgumentException("平台会话消息无法按 UTF-8 安全分片。");
            yield return new KunBuddySessionEntry { Id = $"chat-{message.Id}-{chunk++}", Markdown = text[offset..end] };
            offset = end;
        }
    }

    private static IEnumerable<List<KunBuddySessionEntry>> Batch(IReadOnlyList<KunBuddySessionEntry> entries)
    {
        var batch = new List<KunBuddySessionEntry>();
        var bytes = 0;
        foreach (var entry in entries)
        {
            var size = Encoding.UTF8.GetByteCount(entry.Markdown);
            if (batch.Count > 0 && (batch.Count == 64 || bytes + size > 2 * 1024 * 1024))
            {
                yield return batch; batch = []; bytes = 0;
            }
            batch.Add(entry); bytes += size;
        }
        if (batch.Count > 0) yield return batch;
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
