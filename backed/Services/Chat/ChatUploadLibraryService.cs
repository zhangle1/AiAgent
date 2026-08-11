using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IChatUploadLibraryService
{
    Task<List<ChatUploadFileDto>> ListAsync(AuthenticatedUser requester, string? userId, string? keyword, string? kind, string? sessionId, int limit, CancellationToken cancellationToken);
    Task<ChatUploadContent?> OpenAsync(AuthenticatedUser requester, string? userId, string attachmentId, CancellationToken cancellationToken);
}

public sealed record ChatUploadContent(string Path, string ContentType, string Kind);

/// <summary>Indexes only server-created attachment manifests. Paths stay inside this service.</summary>
public sealed class ChatUploadLibraryService : IChatUploadLibraryService
{
    private readonly IConfiguration _configuration;
    public ChatUploadLibraryService(IConfiguration configuration) => _configuration = configuration;

    public Task<List<ChatUploadFileDto>> ListAsync(AuthenticatedUser requester, string? userId, string? keyword, string? kind, string? sessionId, int limit, CancellationToken cancellationToken)
    {
        var targetUserId = ResolveTargetUser(requester, userId);
        var normalizedKeyword = keyword?.Trim();
        var normalizedKind = kind?.Trim().ToLowerInvariant();
        var normalizedSession = sessionId?.Trim();
        var files = ReadEntries(cancellationToken)
            .Where(item => targetUserId == null || string.Equals(item.UploaderId, targetUserId, StringComparison.Ordinal))
            .Where(item => string.IsNullOrWhiteSpace(normalizedKind) || string.Equals(item.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(normalizedSession) || string.Equals(item.SessionId, normalizedSession, StringComparison.Ordinal))
            .Where(item => string.IsNullOrWhiteSpace(normalizedKeyword) || item.FileName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(item => item.Item)
            .ToList();
        return Task.FromResult(files);
    }

    public Task<ChatUploadContent?> OpenAsync(AuthenticatedUser requester, string? userId, string attachmentId, CancellationToken cancellationToken)
    {
        var targetUserId = ResolveTargetUser(requester, userId);
        var entry = ReadEntries(cancellationToken).FirstOrDefault(item => item.Id == attachmentId && (targetUserId == null || item.UploaderId == targetUserId));
        return Task.FromResult(entry == null || !File.Exists(entry.StoredPath) ? null : new ChatUploadContent(entry.StoredPath, entry.ContentType, entry.Kind));
    }

    private IEnumerable<StoredUpload> ReadEntries(CancellationToken cancellationToken)
    {
        var root = RootPath;
        if (!Directory.Exists(root)) return [];
        var files = new List<StoredUpload>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var value = document.RootElement;
                var userId = GetString(value, "UserId") ?? GetString(value, "userId");
                var sessionId = GetString(value, "SessionId") ?? GetString(value, "sessionId");
                var storedFileName = GetString(value, "StoredFileName") ?? GetString(value, "storedFileName");
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(storedFileName)) continue;
                var directory = Path.GetDirectoryName(manifestPath)!;
                var storedPath = Path.GetFullPath(Path.Combine(directory, storedFileName));
                if (!IsPathInsideRoot(storedPath, root)) continue;
                var id = Path.GetFileNameWithoutExtension(manifestPath);
                var kind = directory.Contains($"{Path.DirectorySeparatorChar}extracted{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "extracted_text" : directory.Contains($"{Path.DirectorySeparatorChar}files{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "document" : "image";
                files.Add(new StoredUpload(new ChatUploadFileDto { Id = id, FileName = GetString(value, "FileName") ?? GetString(value, "fileName") ?? storedFileName, ContentType = GetString(value, "ContentType") ?? GetString(value, "contentType") ?? "application/octet-stream", SizeBytes = GetLong(value, "SizeBytes") ?? GetLong(value, "sizeBytes") ?? 0, Kind = kind, ExtractionStatus = GetString(value, "ExtractionStatus") ?? GetString(value, "extractionStatus"), SessionId = sessionId, SourceAttachmentId = GetString(value, "SourceAttachmentId") ?? GetString(value, "sourceAttachmentId"), UploaderId = userId, CreatedAt = File.GetLastWriteTimeUtc(manifestPath) }, storedPath));
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        return files;
    }

    private string RootPath
    {
        get
        {
            var configured = _configuration["ChatAttachments:RootPath"];
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim());
            var dataPath = _configuration["DataPath"];
            return Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(dataPath) ? "data" : dataPath, "chat_attachments"));
        }
    }
    private static string? ResolveTargetUser(AuthenticatedUser requester, string? userId)
    {
        if (requester.IsAdministrator) return string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        return requester.Id;
    }
    private static bool IsPathInsideRoot(string path, string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string? GetString(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static long? GetLong(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var result) ? result : null;
    private sealed record StoredUpload(ChatUploadFileDto Item, string StoredPath)
    {
        public string Id => Item.Id; public string FileName => Item.FileName; public string ContentType => Item.ContentType; public string Kind => Item.Kind; public DateTime CreatedAt => Item.CreatedAt; public string? SessionId => Item.SessionId; public string? UploaderId => Item.UploaderId;
    }
}
