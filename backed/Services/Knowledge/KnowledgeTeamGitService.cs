using System.Security.Cryptography;
using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Git;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>One administrator-managed shared projection; personal sources never enter this repository.</summary>
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
        await RequireAdmin(ct);
        CheckPaths();
        var config = await Config(ct);
        return new KnowledgeGitState { RootPath = paths.RootPath, WorkspacePath = Workspace, Config = config,
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

    public async Task<GitDeliveryValidation> RefreshAsync(CancellationToken ct) => await Locked(async () =>
    {
        await RequireAdmin(ct); CheckPaths(); await RequireConfig(ct);
        // Build a complete projection before publishing. Unrelated files and runtime data cannot be staged.
        var stage = Path.Combine(BasePath, ".team-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var manifest = new List<object>();
            var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in resources.Tree().Where(n => n.Uri.StartsWith("viking://resources/", StringComparison.Ordinal) || n.Uri.StartsWith("viking://projects/", StringComparison.Ordinal)))
            {
                ct.ThrowIfCancellationRequested();
                var relative = ExportPath(node.Uri);
                if (!exported.Add(relative)) throw new InvalidOperationException("导出路径存在大小写冲突，请调整知识目录名称。");
                var destination = Path.Combine(stage, relative);
                if (node.Kind == "directory") { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var source = resources.File(node.Uri);
                await using (var input = System.IO.File.OpenRead(source.Path))
                await using (var output = System.IO.File.Create(destination)) await input.CopyToAsync(output, ct);
                await using var content = System.IO.File.OpenRead(destination);
                manifest.Add(new { uri = node.Uri, path = relative.Replace('\\', '/'), sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)) });
            }
            await System.IO.File.WriteAllBytesAsync(Path.Combine(stage, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest), ct);
            // Publish only managed roots. Roll back all renames if publication fails.
            var moved = new List<(string Target, string? Backup)>();
            try
            {
                foreach (var name in new[] { "resources", "projects", "manifest.json" })
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
            return await git.ValidateTeamAsync(Workspace, ct, await Credential(ct));
        }
        finally { Directory.Delete(stage, true); }
    }, ct);

    public async Task<GitOperationResult> PushAsync(KnowledgeGitPush request, CancellationToken ct) => await Locked(async () =>
    {
        var user = await RequireAdmin(ct); CheckPaths(); await RequireConfig(ct);
        if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("请输入提交说明。");
        var credential = await Credential(ct);
        var validation = await git.ValidateTeamAsync(Workspace, ct, credential);
        if (!validation.IsValid || validation.SnapshotSha256 != request.Snapshot)
            throw new InvalidOperationException("变更已变化或检查未通过，请重新刷新并确认文件列表。");
        var status = await git.StatusAsync("team-knowledge", Workspace, ct, credential);
        if (status.Behind > 0 || !string.IsNullOrEmpty(status.RemoteRefreshError))
            throw new InvalidOperationException("远端领先或检查失败，请管理员处理同步后重试。");
        return await git.CommitAndPushAsync("team-knowledge", Workspace, request.Message, ct, credential, user.Username);
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
                if (Path.GetFileName(entry) is not (".git" or "resources" or "projects" or "manifest.json"))
                    throw new InvalidOperationException("团队工作区存在非导出文件，请移除后重试。");
    }
    private async Task<AuthenticatedUser> RequireAdmin(CancellationToken ct)
    {
        var user = await auth.TryGetCurrentUserAsync(context.HttpContext!, ct);
        if (user?.IsAdministrator != true) throw new UnauthorizedAccessException("团队 Git 配置和推送仅限管理员。");
        return user;
    }
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
