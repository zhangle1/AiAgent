using AiAgent.Backend.Dtos.ChangeAtlas;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Task;
using AiAgent.Backend.Services.Auth;

namespace AiAgent.Backend.Services.ChangeAtlas;

public sealed partial class ChangeAtlasService
{
    public async Task<AtlasCheckpoint> CheckpointAsync(AuthenticatedUser user, long projectId, string snapshotId, string commitId, CancellationToken token, AtlasCheckpointRequest? update = null)
    {
        var snapshot = await GetAsync(user, projectId, snapshotId, token);
        var commit = snapshot.Commits.FirstOrDefault(item => item.Id == commitId)
            ?? throw new ArgumentException("提交不在当前地图中。");
        using var db = database.CopyNew();
        var owned = db.Queryable<AiChatSession>().Where(item => item.UserId == user.Id && item.CodeProjectId == projectId && !item.IsDeleted);
        var sessionId = update == null
            ? await store.ReadCheckpointAsync(user.Id, projectId, commit.RepositoryId, commit.Sha, token)
            : update.SessionId;
        if (sessionId?.Length > 64) throw new ArgumentException("无效的会话标识。");
        var selected = sessionId == null ? null : owned.Clone().Where(item => item.Id == sessionId)
            .Select(item => new { item.Id, item.Title }).First();
        if (update != null && sessionId != null && selected == null) throw new ArgumentException("只能关联本项目下自己的会话。");

        var sessions = owned.Where(item => !item.IsArchived).OrderByDescending(item => item.UpdatedAt).Take(50)
            .Select(item => new { item.Id, item.Title }).ToList().Select(item => new AtlasCheckpointSession(item.Id, item.Title)).ToList();
        var linked = selected == null ? null : new AtlasCheckpointSession(selected.Id, selected.Title);
        if (linked != null && sessions.All(item => item.Id != linked.Id)) sessions.Insert(0, linked);

        var delivery = db.Queryable<AiCodeChangeSetRepository>()
            .InnerJoin<AiCodeChangeSet>((repo, change) => repo.ChangeSetId == change.Id)
            .LeftJoin<AiProjectTask>((repo, change, task) => change.TaskId == task.Id && task.CodeProjectId == projectId && task.UserId == user.Id && !task.IsDeleted)
            .Where((repo, change, task) => repo.RepositoryId == commit.RepositoryId && repo.CommitSha == commit.Sha &&
                repo.DeliveryStatus == "delivered" && change.ProjectId == projectId && (change.IsDeleted == null || change.IsDeleted == false))
            .OrderByDescending((repo, change, task) => repo.Id)
            .Select((repo, change, task) => new { TaskTitle = task.Title, repo.ValidationStatus }).First();
        token.ThrowIfCancellationRequested();
        RequireAccess(user, projectId);
        if (update != null) await store.SaveCheckpointAsync(user.Id, projectId, commit.RepositoryId, commit.Sha, selected?.Id, token);
        return new(linked, sessions, delivery == null ? null : new(delivery.TaskTitle, delivery.ValidationStatus));
    }
}
