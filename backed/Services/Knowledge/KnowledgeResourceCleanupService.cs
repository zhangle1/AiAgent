using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Soft-removes reviewed stale source records; never deletes or opens stored file content.</summary>
public sealed class KnowledgeResourceCleanupService(ISqlSugarClient database, IKnowledgePathService paths,
    IAuthService auth, IHttpContextAccessor accessor)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record Candidate(AiKnowledgeDocument Document, KnowledgeCleanupItem Item);

    public async Task<KnowledgeCleanupPreview?> PreviewAsync(CancellationToken ct)
    {
        var context = accessor.HttpContext;
        try
        {
            var user = await RequireUser(context, ct);
            if (user is null) return null;
            using var db = database.CopyNew();
            return Preview(user, Candidates(db, user, ct));
        }
        catch (OperationCanceledException) when (ClientCancelled(context, ct)) { return null; }
    }

    public async Task<KnowledgeCleanupResult?> CleanupAsync(KnowledgeCleanupRequest request, CancellationToken ct)
    {
        var context = accessor.HttpContext;
        var user = await RequireUser(context, ct);
        if (user is null) return null;
        if (request.DocumentIds is null || request.DocumentIds.Count is 0 or > 500 || request.DocumentIds.Any(id => id <= 0))
            throw new ArgumentException("请先预览并选择 1–500 条失效记录。");
        var ids = request.DocumentIds.Distinct().ToList();
        try { await _gate.WaitAsync(ct); }
        catch (OperationCanceledException) when (ClientCancelled(context, ct)) { return null; }
        try
        {
            if (ClientCancelled(context, ct)) return null;
            using var db = database.CopyNew();
            db.Ado.BeginTran();
            try
            {
                var candidates = Candidates(db, user, ct);
                if (string.IsNullOrWhiteSpace(request.SnapshotSha256) || Preview(user, candidates).SnapshotSha256 != request.SnapshotSha256)
                    throw new InvalidOperationException("资料或任务状态已变化，请重新扫描并确认清理列表。");
                var selected = candidates.Where(row => ids.Contains(row.Document.Id)).ToList();
                if (selected.Count != ids.Count || selected.Any(row => !row.Item.CanCleanup))
                    throw new InvalidOperationException("只能清理当前账号可管理且没有活动任务的失效记录，请重新扫描。");
                var now = DateTime.UtcNow;
                foreach (var row in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    var document = row.Document;
                    var changed = db.Updateable<AiKnowledgeDocument>()
                        .SetColumns(d => new AiKnowledgeDocument { IsDeleted = true, DeletedAt = now, UpdatedAt = now })
                        .Where(d => d.Id == document.Id && !d.IsDeleted && d.StoragePath == document.StoragePath &&
                            d.FileHash == document.FileHash && d.ResourceUri == document.ResourceUri && d.UpdatedAt == document.UpdatedAt)
                        .ExecuteCommand();
                    if (changed != 1) throw new InvalidOperationException("资料在清理期间发生变化，请重新扫描。");
                }
                ct.ThrowIfCancellationRequested();
                db.Ado.CommitTran();
                return new() { RemovedCount = selected.Count };
            }
            catch (OperationCanceledException) when (ClientCancelled(context, ct))
            {
                db.Ado.RollbackTran();
                return null;
            }
            catch { db.Ado.RollbackTran(); throw; }
        }
        finally { _gate.Release(); }
    }

    private List<Candidate> Candidates(ISqlSugarClient db, AuthenticatedUser user, CancellationToken ct)
    {
        var ownRoot = KnowledgeUserScope.RootFor(user.Username, user.Id);
        var bases = db.Queryable<AiKnowledgeBase>().Where(k => !k.IsDeleted).ToList().ToDictionary(k => k.Id);
        var documents = db.Queryable<AiKnowledgeDocument>().Where(d => !d.IsDeleted).ToList();
        var active = db.Queryable<AiKnowledgeJob>()
            .Where(j => j.DocumentId != null && (j.Status == "queued" || j.Status == "pending" || j.Status == "processing" || j.Status == "cancelling"))
            .Select(j => j.DocumentId!.Value).ToList().ToHashSet();
        var result = new List<Candidate>();
        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();
            if (!bases.TryGetValue(document.KnowledgeBaseId, out var kb)) continue;
            var uri = KnowledgeResourceService.DocumentUri(kb, document);
            // An admin's read access to other people's sessions is not cleanup permission.
            if (document.ResourceUri?.StartsWith("viking://user/", StringComparison.Ordinal) == true && !uri.StartsWith(ownRoot, StringComparison.Ordinal)) continue;
            if (!uri.StartsWith(ownRoot, StringComparison.Ordinal) &&
                !(user.IsAdministrator && (uri.StartsWith("viking://resources/", StringComparison.Ordinal) || uri.StartsWith("viking://projects/", StringComparison.Ordinal)))) continue;
            if (KnowledgeResourceService.IsGeneratedUri(uri)) continue;
            var source = KnowledgeSourceFile.Inspect(paths, kb, document);
            if (source.Available) continue;
            var busy = active.Contains(document.Id) || document.Status is "parsing" or "processing" or "indexing";
            result.Add(new(document, new() { DocumentId = document.Id, Uri = uri, Name = document.OriginalFileName,
                Reason = source.Status, Message = source.Message!, CanCleanup = source.CanClean && !busy,
                BlockedReason = busy ? "存在活动处理任务，请先取消任务并等待结束。" : !source.CanClean ? "请先恢复目录访问权限或存储连接，再重新扫描。" : null }));
        }
        return result.OrderBy(row => row.Document.Id).ToList();
    }

    private KnowledgeCleanupPreview Preview(AuthenticatedUser user, List<Candidate> candidates)
    {
        var fingerprint = JsonSerializer.Serialize(new { user.Id, root = paths.RootPath,
            rows = candidates.Select(row => new { row.Document, row.Item }) });
        return new() { Items = candidates.Select(row => row.Item).ToList(), IncludesShared = user.IsAdministrator,
            SnapshotSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))).ToLowerInvariant() };
    }

    private static bool ClientCancelled(HttpContext? context, CancellationToken ct)
        => ct.IsCancellationRequested && context?.RequestAborted.IsCancellationRequested == true;

    private async Task<AuthenticatedUser?> RequireUser(HttpContext? context, CancellationToken ct)
    {
        if (context is null) throw new UnauthorizedAccessException("请先登录。");
        if (ClientCancelled(context, ct)) return null;
        try
        {
            var lookup = auth.TryGetCurrentUserAsync(context, ct);
            // A cancelled lookup must not be awaited merely to raise TaskCanceledException in the debugger.
            if (lookup.IsCanceled && ClientCancelled(context, ct)) return null;
            var user = await lookup;
            if (ClientCancelled(context, ct)) return null;
            return user ?? throw new UnauthorizedAccessException("请先登录。");
        }
        catch (OperationCanceledException) when (ClientCancelled(context, ct)) { return null; }
    }
}
