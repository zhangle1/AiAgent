using System.Security.Cryptography;
using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Git;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Shared projection plus explicitly published session copies; personal sources are never exported implicitly.</summary>
public sealed class KnowledgeTeamGitService(IKnowledgePathService paths, IConfiguration configuration,
    KnowledgeResourceService resources, IGitWorkspaceService git, IAuthService auth,
    IHttpContextAccessor context, ISqlSugarClient database, IDataProtectionProvider protection)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private string BasePath => string.IsNullOrWhiteSpace(configuration["KnowledgeStorage:RootPath"])
        ? Path.Combine(Path.GetDirectoryName(paths.RootPath)!, "knowledge-workspaces")
        : Path.GetFullPath(configuration["KnowledgeStorage:RootPath"]!);
    private string Workspace => Path.Combine(BasePath, "workspaces", "team-knowledge");
    private string ConfigPath => Path.Combine(BasePath, "team-git.json");

    public async Task<KnowledgeGitState> StateAsync(CancellationToken ct) => await Locked(async () =>
    {
        var user = await RequireAdmin(ct);
        CheckPaths();
        var config = await Config(ct);
        return new KnowledgeGitState { RootPath = paths.RootPath, WorkspacePath = Workspace, Config = config,
            SessionOptions = OwnSessions(user, resources.Tree()).ToList(),
            Status = Directory.Exists(Path.Combine(Workspace, ".git"))
                ? await git.StatusAsync("team-knowledge", Workspace, ct, await Credential(ct)) : null };
    }, ct);

    public async Task<KnowledgeGitConfig> ConfigureAsync(KnowledgeGitConfig config, CancellationToken ct) => await Locked(async () =>
    {
        await RequireAdmin(ct);
        CheckPaths();
        config.RemoteUrl = config.RemoteUrl.Trim(); config.Branch = config.Branch.Trim();
        await git.ConfigureTeamAsync(Workspace, config.RemoteUrl, config.Branch, ct);
        await AtomicWrite(ConfigPath, JsonSerializer.SerializeToUtf8Bytes(config), ct);
        return config;
    }, ct);

    public Task<GitDeliveryValidation> RefreshAsync(CancellationToken ct) => RefreshAsync(new(), ct);

    public async Task<GitDeliveryValidation> RefreshAsync(KnowledgeGitRefresh request, CancellationToken ct) => await Locked(async () =>
    {
        var user = await RequireAdmin(ct); CheckPaths(); await RequireConfig(ct);
        var tree = resources.Tree();
        var resourceUris = request.ResourceUris?.Distinct(StringComparer.Ordinal).ToList();
        if (resourceUris?.Count > 500) throw new ArgumentException("每次最多选择 500 个资料或项目入口，请分批操作。");
        var shared = tree.Where(n => IsShared(n.Uri)).ToDictionary(n => n.Uri, StringComparer.Ordinal);
        if (resourceUris?.Any(uri => !shared.ContainsKey(uri)) == true)
            throw new UnauthorizedAccessException("所选资料或项目已不可访问，请刷新目录后重选。");
        bool SelectedShared(string uri) => resourceUris is null || resourceUris.Any(scope => uri == scope || scope.EndsWith('/') && uri.StartsWith(scope, StringComparison.Ordinal));
        if (request.SessionUris is null) throw new ArgumentException("请选择有效的会话列表。");
        var selected = request.SessionUris.Distinct(StringComparer.Ordinal).ToList();
        if (selected.Count > 100) throw new ArgumentException("每次最多发布 100 个会话，请分批选择。");
        var available = OwnSessions(user, tree).ToDictionary(n => n.Uri, StringComparer.Ordinal);
        if (selected.Any(uri => !available.ContainsKey(uri)))
            throw new UnauthorizedAccessException("只能明确选择本人会话原文发布；不能发布他人的会话或个人产物。");
        // Build a complete projection before publishing. Unrelated files and runtime data cannot be staged.
        var stage = Path.Combine(BasePath, ".team-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var manifest = new List<KnowledgeGitExportEntry>();
            var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Already published copies remain fixed until their owner explicitly selects them again.
            // Refreshing team resources must not silently publish subsequent private conversation turns.
            var previousManifest = Path.Combine(Workspace, "manifest.json");
            if (System.IO.File.Exists(previousManifest))
            {
                var previous = JsonSerializer.Deserialize<List<KnowledgeGitExportEntry>>(await System.IO.File.ReadAllBytesAsync(previousManifest, ct))
                    ?? throw new InvalidOperationException("导出清单不可用，请管理员检查工作区。");
                foreach (var entry in previous.Where(e => e.Path.StartsWith("sessions/", StringComparison.Ordinal) && !selected.Contains(e.Uri)
                    || IsShared(e.Uri) && !SelectedShared(e.Uri)))
                {
                    var relative = IsShared(entry.Uri) ? ExportPath(entry.Uri) : KnowledgeGitSessionPublication.ExportPath(entry.Uri);
                    if (entry.Path != relative.Replace('\\', '/') || !exported.Add(relative))
                        throw new InvalidOperationException("发布清单不匹配，请管理员检查工作区。");
                    var source = Path.Combine(Workspace, relative);
                    var destination = Path.Combine(stage, relative);
                    if (entry.Kind == "directory") { Directory.CreateDirectory(destination); manifest.Add(entry); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    System.IO.File.Copy(source, destination);
                    await using var content = System.IO.File.OpenRead(destination);
                    if (entry.Sha256 != Convert.ToHexString(await SHA256.HashDataAsync(content, ct)))
                        throw new InvalidOperationException("已发布副本已变化，请重新选择该资料后刷新。");
                    manifest.Add(entry);
                }
            }
            foreach (var node in shared.Values.Where(n => SelectedShared(n.Uri))
                         .Concat(selected.Select(uri => available[uri])))
            {
                ct.ThrowIfCancellationRequested();
                var relative = node.Uri.StartsWith("viking://user/", StringComparison.Ordinal)
                    ? KnowledgeGitSessionPublication.ExportPath(node.Uri) : ExportPath(node.Uri);
                if (!exported.Add(relative)) throw new InvalidOperationException("导出路径存在大小写冲突，请调整知识目录名称。");
                var destination = Path.Combine(stage, relative);
                if (node.Kind == "directory")
                {
                    Directory.CreateDirectory(destination);
                    manifest.Add(new() { Uri = node.Uri, Path = relative.Replace('\\', '/'), Name = node.Name, Kind = "directory" });
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var source = resources.File(node.Uri);
                await using (var input = System.IO.File.OpenRead(source.Path))
                await using (var output = System.IO.File.Create(destination)) await input.CopyToAsync(output, ct);
                await using var content = System.IO.File.OpenRead(destination);
                manifest.Add(new() { Uri = node.Uri, Path = relative.Replace('\\', '/'), Name = node.Name, Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)) });
            }
            manifest = manifest.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
            await System.IO.File.WriteAllBytesAsync(Path.Combine(stage, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest), ct);
            // Publish only managed roots. Roll back all renames if publication fails.
            var moved = new List<(string Target, string? Backup)>();
            try
            {
                foreach (var name in new[] { "resources", "projects", "sessions", "manifest.json" })
                {
                    var target = Path.Combine(Workspace, name); var incoming = Path.Combine(stage, name);
                    var backup = Path.Combine(stage, "backup-" + name);
                    if (Directory.Exists(target)) Directory.Move(target, backup);
                    else if (System.IO.File.Exists(target)) System.IO.File.Move(target, backup);
                    else backup = null;
                    moved.Add((target, backup));
                    if (Directory.Exists(incoming)) Directory.Move(incoming, target);
                    else if (System.IO.File.Exists(incoming)) System.IO.File.Move(incoming, target);
                }
            }
            catch
            {
                foreach (var item in moved.AsEnumerable().Reverse())
                {
                    if (Directory.Exists(item.Target)) Directory.Delete(item.Target, true);
                    else if (System.IO.File.Exists(item.Target)) System.IO.File.Delete(item.Target);
                    if (item.Backup is not null)
                    { if (Directory.Exists(item.Backup)) Directory.Move(item.Backup, item.Target); else System.IO.File.Move(item.Backup, item.Target); }
                }
                throw;
            }
            return await git.ValidateTeamAsync(Workspace, ct, await Credential(ct), request.Force);
        }
        finally { Directory.Delete(stage, true); }
    }, ct);

    public async Task<GitOperationResult> PushAsync(KnowledgeGitPush request, CancellationToken ct) => await Locked(async () =>
    {
        var user = await RequireAdmin(ct); CheckPaths(); await RequireConfig(ct);
        if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("请输入提交说明。");
        var credential = await Credential(ct);
        return await git.PushTeamAsync(Workspace, request.Message, request.Snapshot, request.Force, ct, credential, user.Username);
    }, ct);

    public static string ExportPath(string uri)
    {
        if (!uri.StartsWith("viking://resources/", StringComparison.Ordinal) && !uri.StartsWith("viking://projects/", StringComparison.Ordinal))
            throw new ArgumentException("仅允许团队资料与项目目录。");
        uri = KnowledgeResourceService.NormalizeUri(uri, uri.EndsWith('/'));
        return Path.Combine(uri[9..].TrimEnd('/').Split('/').Select(part =>
        {
            var name = Uri.UnescapeDataString(part);
            var stem = name.Split('.')[0];
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (name.StartsWith('.') || name.StartsWith("~", StringComparison.Ordinal) || name.EndsWith('.') || name.Any(c => c is '<' or '>' or '"' or '|' or '*' || c < 32) || reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
                return "~" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(name)).ToLowerInvariant();
            return name;
        }).ToArray());
    }

    private void CheckPaths()
    {
        _ = paths.RootPath; // validates the configured absolute root
        var root = new DirectoryInfo(Workspace);
        for (var ancestor = root; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("知识根目录不能通过符号链接访问。");
        if (Directory.Exists(Workspace)) Check(new DirectoryInfo(Workspace));
        void Check(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("团队工作区不能包含符号链接。");
                if (entry is DirectoryInfo child) Check(child);
            }
        }
        if (Directory.Exists(Workspace))
            foreach (var entry in Directory.EnumerateFileSystemEntries(Workspace))
                if (Path.GetFileName(entry) is not (".git" or "resources" or "projects" or "sessions" or "manifest.json"))
                    throw new InvalidOperationException("团队工作区存在非导出文件，请移除后重试。");
    }
    private async Task<AuthenticatedUser> RequireAdmin(CancellationToken ct)
    {
        var user = await auth.TryGetCurrentUserAsync(context.HttpContext!, ct);
        if (user?.IsAdministrator != true) throw new UnauthorizedAccessException("团队 Git 配置和推送仅限管理员。");
        return user;
    }
    private static IEnumerable<KnowledgeResourceNodeDto> OwnSessions(AuthenticatedUser user, IEnumerable<KnowledgeResourceNodeDto> tree)
    {
        var prefix = KnowledgeUserScope.RootFor(user.Username, user.Id) + "sessions/";
        return tree.Where(n => n.Kind == "file" && n.DocumentId.HasValue && n.Uri.StartsWith(prefix, StringComparison.Ordinal)
            && !KnowledgeResourceService.IsGeneratedUri(n.Uri) && string.Equals(n.Extension, ".md", StringComparison.OrdinalIgnoreCase));
    }
    private static bool IsShared(string uri) => uri.StartsWith("viking://resources/", StringComparison.Ordinal) || uri.StartsWith("viking://projects/", StringComparison.Ordinal);
    private async Task<GitWorkspaceCredential?> Credential(CancellationToken ct)
    {
        using var db = database.CopyNew();
        var user = await RequireAdmin(ct);
        var account = db.Queryable<AiGitAccount>().First(x => x.UserId == user!.Id && x.IsActive && !x.IsDeleted);
        if (account?.AccessTokenProtected is not { Length: > 0 } token) return null;
        return new(account.Username, protection.CreateProtector("AiAgent.GitAccounts.AccessToken.v1").Unprotect(token));
    }
    private async Task<KnowledgeGitConfig> Config(CancellationToken ct) => System.IO.File.Exists(ConfigPath)
        ? JsonSerializer.Deserialize<KnowledgeGitConfig>(await System.IO.File.ReadAllBytesAsync(ConfigPath, ct)) ?? new() : new();
    private async Task RequireConfig(CancellationToken ct)
    { if (string.IsNullOrWhiteSpace((await Config(ct)).RemoteUrl)) throw new InvalidOperationException("请先保存团队 Git 配置。"); }
    private static async Task AtomicWrite(string path, byte[] bytes, CancellationToken ct)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N");
        try { await System.IO.File.WriteAllBytesAsync(temp, bytes, ct); System.IO.File.Move(temp, path, true); }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
    }
    private static async Task<T> Locked<T>(Func<Task<T>> action, CancellationToken ct)
    { await Gate.WaitAsync(ct); try { return await action(); } finally { Gate.Release(); } }
}
