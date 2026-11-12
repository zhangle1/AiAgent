using System.Globalization;
using System.Text.RegularExpressions;
using AiAgent.Backend.Dtos.ChangeAtlas;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Git;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;

namespace AiAgent.Backend.Services.ChangeAtlas;

public sealed partial class ChangeAtlasService(ISqlSugarClient database, IProjectAccessService access, IChangeAtlasGit git, ChangeAtlasStore store, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("AiAgent.GitAccounts.AccessToken.v1");

    public async Task<AtlasSnapshot> CreateAsync(AuthenticatedUser user, long projectId, AtlasCreateRequest request, CancellationToken token)
    {
        RequireAccess(user, projectId);
        if (request.Days is < 1 or > 30 || request.Ref == null || request.Ref.Length > 256 || request.Ref != "HEAD" && !request.Ref.StartsWith("refs/", StringComparison.Ordinal))
            throw new ArgumentException("请选择 1–30 天范围和有效分支。");
        using var db = database.CopyNew();
        var repositories = db.Queryable<AiCodeRepository>().Where(x => x.ProjectId == projectId && !x.IsDeleted).ToList();
        if (request.RepositoryId.HasValue)
        {
            var selected = repositories.FirstOrDefault(x => x.Id == request.RepositoryId.Value) ?? throw new UnauthorizedAccessException("代码库不属于当前项目。");
            repositories = [selected];
        }
        if (repositories.Count > 12) throw new ArgumentException("项目代码库较多，请选择单个代码库生成地图。");
        var snapshot = new AtlasSnapshot { Id = Guid.NewGuid().ToString("N"), ProjectId = projectId, Days = request.Days, CreatedAt = DateTime.UtcNow, StartAt = DateTime.UtcNow.Date.AddDays(1 - request.Days) };
        var files = new Dictionary<string, AtlasFile>(StringComparer.Ordinal);
        var modules = new Dictionary<string, AtlasModule>(StringComparer.Ordinal);
        foreach (var repository in repositories.OrderBy(x => x.Id))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var read = await git.CaptureAsync(repository.RootPath, store.Cache(user.Id, repository.Id), snapshot.Id, request.Ref, request.RefreshRemote, snapshot.StartAt,
                    request.RefreshRemote ? Credential(db, user, repository) : null, token);
                var incomingPaths = read.Paths.Concat(read.Commits.SelectMany(x => x.Changes).Select(x => x.Path)).Distinct(StringComparer.Ordinal).Count();
                if (files.Count + incomingPaths > 20000 || snapshot.Commits.Sum(x => x.Files.Count) + read.Commits.Sum(x => x.Changes.Count) > 30000)
                {
                    snapshot.Warnings.Add(repository.DisplayName + "：超出本轮文件读取预算，未载入；请单独选择该仓库或缩小时间范围。");
                    continue;
                }
                snapshot.Repositories.Add(new(repository.Id, repository.DisplayName, read.Head, read.Ref, read.Source, read.Refs));
                snapshot.Warnings.AddRange(read.Warnings.Select(x => repository.DisplayName + "：" + x));
                var atHead = read.Paths.ToHashSet(StringComparer.Ordinal);
                AtlasFile AddFile(string path)
                {
                    var fileId = ChangeAtlasGitReader.Hash(repository.Id + ":" + path);
                    if (files.TryGetValue(fileId, out var known)) return known;
                    var directory = ModuleDirectory(path);
                    var moduleId = ChangeAtlasGitReader.Hash(repository.Id + ":module:" + directory);
                    if (!modules.ContainsKey(moduleId)) modules[moduleId] = new(moduleId, repository.Id, directory == "." ? "根目录" : directory.Split('/').Last(), directory);
                    return files[fileId] = new(fileId, repository.Id, moduleId, path, atHead.Contains(path));
                }
                foreach (var path in read.Paths.Where(ChangeAtlasGitReader.ValidPath)) AddFile(path);
                foreach (var commit in read.Commits.Where(x => x.Date >= snapshot.StartAt && x.Date <= snapshot.CreatedAt))
                {
                    var changes = commit.Changes.Select(x => new AtlasFileChange(AddFile(x.Path).Id, x.Added, x.Deleted)).ToList();
                    snapshot.Commits.Add(new(repository.Id + ":" + commit.Sha, repository.Id, commit.Sha, commit.Parent, commit.IsMerge, commit.AuthorId, commit.Author, commit.Date, commit.Subject, changes, commit.Truncated));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                // Never expose Git output, credentials, registered paths or remote URLs to the browser.
                snapshot.Warnings.Add(repository.DisplayName + "：" + (ex is ArgumentException ? "所选分支在该仓库不可读取，请选择当前分支或单独选择仓库。" : "读取失败，请检查 Git 仓库、本人凭据和网络后重试。"));
            }
        }
        if (repositories.Count > 0 && snapshot.Repositories.Count == 0) throw new InvalidOperationException("未能读取任何代码库。请检查 Git 根目录、分支和本人凭据，或选择单个仓库、缩小时间范围后重试。");
        snapshot.Files = files.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
        snapshot.Modules = modules.Values.OrderBy(x => x.Directory, StringComparer.Ordinal).ToList();
        snapshot.Commits = snapshot.Commits.OrderByDescending(x => x.CommittedAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        RequireAccess(user, projectId);
        await store.SaveAsync(user.Id, snapshot, token);
        return snapshot;
    }

    public async Task<AtlasSnapshot> GetAsync(AuthenticatedUser user, long projectId, string snapshotId, CancellationToken token)
    {
        RequireAccess(user, projectId);
        var snapshot = await store.ReadAsync(user.Id, projectId, snapshotId, token);
        using var db = database.CopyNew();
        var allowed = db.Queryable<AiCodeRepository>().Where(x => x.ProjectId == projectId && !x.IsDeleted).Select(x => x.Id).ToList().ToHashSet();
        if (snapshot.Repositories.Any(x => !allowed.Contains(x.Id))) throw new UnauthorizedAccessException("代码库归属已变化，请重新生成地图。");
        return snapshot;
    }
    public async Task<AtlasEvidence> EvidenceAsync(AuthenticatedUser user, long projectId, string snapshotId, string commitId, string fileId, CancellationToken token)
    {
        var snapshot = await GetAsync(user, projectId, snapshotId, token);
        var commit = snapshot.Commits.FirstOrDefault(x => x.Id == commitId) ?? throw new ArgumentException("提交不在当前地图中。");
        var file = snapshot.Files.FirstOrDefault(x => x.Id == fileId && x.RepositoryId == commit.RepositoryId) ?? throw new ArgumentException("文件不在当前地图中。");
        if (!commit.Files.Any(x => x.FileId == fileId)) throw new ArgumentException("该提交没有修改所选文件。");
        var id = ChangeAtlasGitReader.Hash(commit.Id + ":" + file.Id);
        if (Sensitive(file.Path)) return new(id, file.Id, commit.Id, file.Path, commit.Sha, "该文件可能包含凭据或本地配置，变化地图不读取其正文。", false, true);
        var patch = await git.PatchAsync(store.Cache(user.Id, commit.RepositoryId), commit.Sha, commit.ParentSha, file.Path, token);
        return new(id, file.Id, commit.Id, file.Path, commit.Sha, patch.Patch, patch.Truncated, false);
    }
    public static bool Sensitive(string path)
    {
        var name = path.Split('/').Last();
        return name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || new[] { ".npmrc", ".pypirc", ".netrc", ".dockercfg", "nuget.config" }.Contains(name, StringComparer.OrdinalIgnoreCase)
            || Regex.IsMatch(path, @"(?i)(?:^|/)(?:\.ssh|secrets|credentials)(?:/|$)")
            || Regex.IsMatch(name, @"(?i)(?:^id_(?:rsa|dsa|ecdsa|ed25519)|\.(?:pem|pfx|p12|key|keystore)$|credentials|secrets)");
    }
    public static string ModuleDirectory(string path)
    {
        var parts = path.Split('/');
        if (parts.Length == 1) return ".";
        var layers = new HashSet<string>(["src", "backed", "backend", "front", "frontend", "services", "controllers", "components", "pages", "app", "entities", "dtos", "lib", "tests", "test"], StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < parts.Length - 2 && layers.Contains(parts[index])) index++;
        return string.Join('/', parts.Take(index + 1));
    }
    public static List<AtlasCommit> SelectCommits(AtlasSnapshot snapshot, AtlasAnalysisRequest request)
    {
        var module = snapshot.Modules.FirstOrDefault(x => x.Id == request.ModuleId) ?? throw new ArgumentException("请选择当前地图中的模块。");
        var files = snapshot.Files.Where(x => x.ModuleId == module.Id).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var day = request.Until == null ? snapshot.CreatedAt.Date : DateTime.TryParseExact(request.Until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : throw new ArgumentException("日期格式无效。");
        if (day < snapshot.StartAt.Date || day > snapshot.CreatedAt.Date) throw new ArgumentException("日期超出地图范围。");
        if (request.AuthorId != null && !snapshot.Commits.Any(x => x.AuthorId == request.AuthorId)) throw new ArgumentException("人员不在当前地图中。");
        return snapshot.Commits.Where(x => x.CommittedAt.Date <= day && (!request.Daily || x.CommittedAt.Date == day) && (request.AuthorId == null || x.AuthorId == request.AuthorId) && x.Files.Any(f => files.Contains(f.FileId))).ToList();
    }
    private void RequireAccess(AuthenticatedUser user, long id) { if (!access.CanAccess(user, id)) throw new UnauthorizedAccessException("无权访问该项目。"); }
    private GitWorkspaceCredential? Credential(ISqlSugarClient db, AuthenticatedUser user, AiCodeRepository repository)
    {
        var account = repository.GitAccountId.HasValue ? db.Queryable<AiGitAccount>().First(x => x.Id == repository.GitAccountId && x.UserId == user.Id && x.IsActive && !x.IsDeleted) : null;
        account ??= db.Queryable<AiGitAccount>().Where(x => x.UserId == user.Id && x.IsActive && !x.IsDeleted).OrderByDescending(x => x.UpdatedAt).First();
        if (string.IsNullOrWhiteSpace(account?.AccessTokenProtected)) return null;
        try { return new(account.Username, _protector.Unprotect(account.AccessTokenProtected)); }
        catch { throw new InvalidOperationException("本人 Git 凭据无法读取。"); }
    }
}
