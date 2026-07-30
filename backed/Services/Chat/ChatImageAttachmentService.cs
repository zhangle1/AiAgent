using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IChatImageAttachmentService
{
    Task<ChatImageAttachmentDto> SaveAsync(AuthenticatedUser user, IFormFile file, CancellationToken cancellationToken);
    Task<List<ResolvedChatImageAttachment>> ResolveLocalAttachmentsAsync(AuthenticatedUser user, string? sessionId, IReadOnlyCollection<string> attachmentIds, CancellationToken cancellationToken);
    Task<List<ResolvedChatImageAttachment>> PersistForSessionAsync(AuthenticatedUser user, string sessionId, IReadOnlyCollection<string> attachmentIds, CancellationToken cancellationToken);
    Task<ChatImageContent?> OpenPersistedImageAsync(AuthenticatedUser user, string sessionId, string attachmentId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(AuthenticatedUser user, string attachmentId, CancellationToken cancellationToken);
}

public sealed record ResolvedChatImageAttachment(ChatImageAttachmentDto Attachment, string LocalPath);
public sealed record ChatImageContent(string Path, string ContentType);

/// <summary>
/// Stores short-lived image uploads outside registered code workspaces. Clients only receive opaque IDs;
/// Codex receives an absolute path after ownership and image-signature validation.
/// </summary>
public sealed class ChatImageAttachmentService : IChatImageAttachmentService
{
    private const int HeaderLength = 16;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatImageAttachmentService> _logger;
    private readonly string _rootPath;
    private readonly ConcurrentDictionary<string, StoredAttachment> _attachments = new(StringComparer.Ordinal);

    public ChatImageAttachmentService(IConfiguration configuration, ILogger<ChatImageAttachmentService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _rootPath = ResolveRootPath(configuration);
    }

    public async Task<ChatImageAttachmentDto> SaveAsync(AuthenticatedUser user, IFormFile file, CancellationToken cancellationToken)
    {
        PruneExpired();
        if (file == null || file.Length <= 0) throw new InvalidOperationException("Image file is empty.");
        if (file.Length > MaxImageBytes) throw new InvalidOperationException($"Image exceeds the {MaxImageBytes / 1024 / 1024} MB limit.");

        await using var source = file.OpenReadStream();
        var header = new byte[HeaderLength];
        var headerCount = await source.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        var format = DetectFormat(header.AsSpan(0, headerCount))
            ?? throw new InvalidOperationException("Only PNG, JPEG, WebP, and GIF images are supported.");

        Directory.CreateDirectory(RootPath);
        var id = Guid.NewGuid().ToString("N");
        var targetPath = Path.Combine(RootPath, $"{id}{format.Extension}");
        var tempPath = $"{targetPath}.uploading";

        try
        {
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await target.WriteAsync(header.AsMemory(0, headerCount), cancellationToken);
                await source.CopyToAsync(target, cancellationToken);
                await target.FlushAsync(cancellationToken);
            }
            File.Move(tempPath, targetPath);
        }
        catch
        {
            TryDelete(tempPath);
            TryDelete(targetPath);
            throw;
        }

        var attachment = new StoredAttachment(user.Id, targetPath, SafeFileName(file.FileName), format.ContentType, file.Length, DateTime.UtcNow.AddMinutes(RetentionMinutes));
        _attachments[id] = attachment;
        _logger.LogInformation("Chat image uploaded. AttachmentId={AttachmentId}, UserId={UserId}, Bytes={Bytes}", id, user.Id, file.Length);
        return new ChatImageAttachmentDto { Id = id, FileName = attachment.FileName, ContentType = attachment.ContentType, SizeBytes = attachment.SizeBytes };
    }

    public Task<List<ResolvedChatImageAttachment>> ResolveLocalAttachmentsAsync(AuthenticatedUser user, string? sessionId, IReadOnlyCollection<string> attachmentIds, CancellationToken cancellationToken)
    {
        PruneExpired();
        var ids = attachmentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return Task.FromResult(new List<ResolvedChatImageAttachment>());
        if (ids.Count > MaxImagesPerTurn) throw new InvalidOperationException($"At most {MaxImagesPerTurn} images can be attached to one Codex turn.");

        var attachments = new List<ResolvedChatImageAttachment>(ids.Count);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_attachments.TryGetValue(id, out var attachment) && !string.IsNullOrWhiteSpace(sessionId))
            {
                attachment = LoadPersistedAttachment(user, sessionId, id);
                if (attachment != null) _attachments.TryAdd(id, attachment);
            }
            if (attachment == null || !string.Equals(attachment.UserId, user.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Image attachment was not found or is no longer available.");
            }
            if (!File.Exists(attachment.Path))
            {
                _attachments.TryRemove(id, out _);
                throw new InvalidOperationException("Image attachment has expired. Please upload it again.");
            }
            attachments.Add(new ResolvedChatImageAttachment(ToDto(id, attachment), attachment.Path));
        }
        return Task.FromResult(attachments);
    }

    public async Task<List<ResolvedChatImageAttachment>> PersistForSessionAsync(AuthenticatedUser user, string sessionId, IReadOnlyCollection<string> attachmentIds, CancellationToken cancellationToken)
    {
        var attachments = await ResolveLocalAttachmentsAsync(user, sessionId, attachmentIds, cancellationToken);
        if (attachments.Count == 0) return attachments;

        var historyPath = GetHistoryPath(user.Id, sessionId);
        Directory.CreateDirectory(historyPath);
        var persisted = new List<ResolvedChatImageAttachment>(attachments.Count);
        foreach (var item in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = item.Attachment.Id;
            var stored = _attachments[id];
            if (stored.PersistentSessionId != null)
            {
                if (!string.Equals(stored.PersistentSessionId, sessionId, StringComparison.Ordinal)) throw new InvalidOperationException("Image attachment belongs to a different chat session.");
                persisted.Add(new ResolvedChatImageAttachment(item.Attachment, stored.Path));
                continue;
            }

            var extension = Path.GetExtension(stored.Path);
            var targetPath = Path.Combine(historyPath, $"{id}{extension}");
            var manifestPath = Path.Combine(historyPath, $"{id}.json");
            File.Move(stored.Path, targetPath);
            var updated = stored with { Path = targetPath, PersistentSessionId = sessionId, ExpiresAt = DateTime.MaxValue };
            var manifest = new PersistedAttachmentManifest(user.Id, sessionId, updated.FileName, updated.ContentType, updated.SizeBytes, Path.GetFileName(targetPath));
            await WriteManifestAsync(manifestPath, manifest, cancellationToken);
            _attachments[id] = updated;
            persisted.Add(new ResolvedChatImageAttachment(ToDto(id, updated), targetPath));
        }
        return persisted;
    }

    public Task<ChatImageContent?> OpenPersistedImageAsync(AuthenticatedUser user, string sessionId, string attachmentId, CancellationToken cancellationToken)
    {
        var stored = LoadPersistedAttachment(user, sessionId, attachmentId);
        return Task.FromResult(stored == null || !File.Exists(stored.Path) ? null : new ChatImageContent(stored.Path, stored.ContentType));
    }

    public Task<bool> DeleteAsync(AuthenticatedUser user, string attachmentId, CancellationToken cancellationToken)
    {
        PruneExpired();
        if (string.IsNullOrWhiteSpace(attachmentId) || !_attachments.TryGetValue(attachmentId, out var attachment)) return Task.FromResult(false);
        if (!string.Equals(attachment.UserId, user.Id, StringComparison.Ordinal)) return Task.FromResult(false);
        if (attachment.PersistentSessionId != null) return Task.FromResult(false);
        if (_attachments.TryRemove(attachmentId, out var removed)) TryDelete(removed.Path);
        return Task.FromResult(true);
    }

    private string RootPath => _rootPath;

    private static string ResolveRootPath(IConfiguration configuration)
    {
        var configuredPath = configuration["ChatAttachments:RootPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath.Trim());
        }

        // Preserve the original storage location when RootPath is not configured.
        var dataPath = configuration["DataPath"];
        return Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(dataPath) ? "data" : dataPath, "chat_attachments"));
    }

    private string GetHistoryPath(string userId, string sessionId)
    {
        var normalizedSessionId = sessionId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSessionId) || normalizedSessionId.Length > 64 || normalizedSessionId.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException("Chat session identifier is invalid.");
        }
        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..24].ToLowerInvariant();
        return Path.Combine(RootPath, "history", userHash, normalizedSessionId);
    }

    private StoredAttachment? LoadPersistedAttachment(AuthenticatedUser user, string sessionId, string attachmentId)
    {
        if (string.IsNullOrWhiteSpace(attachmentId) || attachmentId.Length != 32 || attachmentId.Any(character => !Uri.IsHexDigit(character))) return null;
        var historyPath = GetHistoryPath(user.Id, sessionId);
        var manifestPath = Path.Combine(historyPath, $"{attachmentId}.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<PersistedAttachmentManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || !string.Equals(manifest.UserId, user.Id, StringComparison.Ordinal) || !string.Equals(manifest.SessionId, sessionId, StringComparison.Ordinal)) return null;
            var path = Path.GetFullPath(Path.Combine(historyPath, manifest.StoredFileName));
            if (!path.StartsWith(Path.GetFullPath(historyPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return new StoredAttachment(user.Id, path, manifest.FileName, manifest.ContentType, manifest.SizeBytes, DateTime.MaxValue, sessionId);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static async Task WriteManifestAsync(string manifestPath, PersistedAttachmentManifest manifest, CancellationToken cancellationToken)
    {
        var tempPath = $"{manifestPath}.writing";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(manifest), cancellationToken);
        File.Move(tempPath, manifestPath);
    }

    private long MaxImageBytes => Math.Clamp(_configuration.GetValue<long?>("ChatAttachments:MaxImageBytes") ?? 10L * 1024 * 1024, 1024 * 1024, 50L * 1024 * 1024);
    private int MaxImagesPerTurn => Math.Clamp(_configuration.GetValue<int?>("ChatAttachments:MaxImagesPerTurn") ?? 4, 1, 8);
    private int RetentionMinutes => Math.Clamp(_configuration.GetValue<int?>("ChatAttachments:RetentionMinutes") ?? 60, 5, 1440);

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _attachments)
        {
            if (pair.Value.ExpiresAt > now) continue;
            if (_attachments.TryRemove(pair.Key, out var attachment)) TryDelete(attachment.Path);
        }

        if (!Directory.Exists(RootPath)) return;
        var cutoff = now.AddMinutes(-RetentionMinutes);
        foreach (var path in Directory.EnumerateFiles(RootPath))
        {
            if (File.GetLastWriteTimeUtc(path) < cutoff) TryDelete(path);
        }
    }

    private static ImageFormat? DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return new ImageFormat(".png", "image/png");
        if (header.Length >= 3 && header[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) return new ImageFormat(".jpg", "image/jpeg");
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return new ImageFormat(".gif", "image/gif");
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)) return new ImageFormat(".webp", "image/webp");
        return null;
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        return string.IsNullOrWhiteSpace(name) ? "image" : name[..Math.Min(name.Length, 160)];
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private static ChatImageAttachmentDto ToDto(string id, StoredAttachment attachment) => new() { Id = id, FileName = attachment.FileName, ContentType = attachment.ContentType, SizeBytes = attachment.SizeBytes };
    private sealed record StoredAttachment(string UserId, string Path, string FileName, string ContentType, long SizeBytes, DateTime ExpiresAt, string? PersistentSessionId = null);
    private sealed record PersistedAttachmentManifest(string UserId, string SessionId, string FileName, string ContentType, long SizeBytes, string StoredFileName);
    private sealed record ImageFormat(string Extension, string ContentType);
}
