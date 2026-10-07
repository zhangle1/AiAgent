using AiAgent.Backend.Dtos.CodeRepository;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

/// <summary>One-shot, browser-authorized manifest inbox. Only this host owns process lifetimes.</summary>
public sealed class ChatRuntimeService(ICodeRepositoryManager repositories, ICodeRuntimeManager runtime, ILogger<ChatRuntimeService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly HttpClient _probe = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };

    public ChatRuntimeJobDto Prepare(long projectId, ChatRuntimePrepareRequest request)
    {
        if (request.Selections is null || request.Selections.Count is < 1 or > 12 || request.IdleMinutes is < 5 or > 240)
            throw new ArgumentException("Select 1–12 repositories and an idle timeout of 5–240 minutes.");
        if (request.Selections.Any(x => x is null || string.IsNullOrWhiteSpace(x.RepositoryName) || x.EntryPaths is null || x.EntryPaths.Count > 40))
            throw new ArgumentException("Invalid repository selection.");
        var selected = request.Selections.Select(x => repositories.Get(x.RepositoryName)).ToList();
        if (selected.Any(x => x.ProjectId != projectId)) throw new ArgumentException("A selected repository does not belong to this project.");
        for (var i = 0; i < selected.Count; i++)
            foreach (var path in request.Selections[i].EntryPaths) ChatRuntimePolicy.SafePath(selected[i].RootPath, path);
        var job = new Job
        {
            Id = Guid.NewGuid().ToString("N"), ProjectId = projectId, Repository = selected[0].Name,
            Root = selected[0].RootPath, AllowedRepositories = selected.Select(x => x.Name).ToHashSet(StringComparer.Ordinal),
            IdleMinutes = request.IdleMinutes
        };
        ChatRuntimePolicy.SafePath(job.Root, job.ManifestPath);
        lock (_jobs)
        {
            if (_jobs.Values.Count(x => x.ProjectId == projectId && x.Status is "waiting" or "starting" or "running") >= 8)
                throw new InvalidOperationException("This project already has eight pending or active runtime groups.");
            _jobs[job.Id] = job;
        }
        return Snapshot(job);
    }

    public List<ChatRuntimeJobDto> List(long projectId) => _jobs.Values.Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt).Select(Snapshot).ToList();

    public void Visit(long projectId, string id)
    {
        var job = Find(projectId, id);
        lock (job.Sync) { if (job.Status is "running" or "starting") job.LastVisit = DateTime.UtcNow; }
    }

    public void Stop(long projectId, string id)
    {
        var job = Find(projectId, id);
        lock (job.Sync)
        {
            job.Cancellation.Cancel();
            foreach (var run in job.Runs) runtime.Stop(projectId, run);
            job.Status = "stopped";
            job.Message = "整组服务已关闭。";
        }
    }

    private Job Find(long projectId, string id) => _jobs.TryGetValue(id, out var job) && job.ProjectId == projectId ? job : throw new KeyNotFoundException("Runtime group was not found.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var job in _jobs.Values)
                {
                    try
                    {
                        if (job.Status == "waiting")
                        {
                            if (DateTime.UtcNow - job.CreatedAt > TimeSpan.FromHours(1)) { job.Status = "expired"; job.Message = "等待启动清单超时，请重新创建运行请求。"; continue; }
                            var path = ChatRuntimePolicy.SafePath(job.Root, job.ManifestPath);
                            if (!File.Exists(path)) continue;
                            if (new FileInfo(path).Length > 65536) throw new ArgumentException("Runtime manifest exceeds 64 KiB.");
                            ChatRuntimeManifest? manifest;
                            // AI writes *.tmp then renames. Partial files from an interrupted write remain retryable.
                            try
                            {
                                using var stream = File.OpenRead(path);
                                var buffer = new byte[65537];
                                var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, stoppingToken);
                                if (length > 65536) throw new ArgumentException("Runtime manifest exceeds 64 KiB.");
                                manifest = JsonSerializer.Deserialize<ChatRuntimeManifest>(buffer.AsSpan(0, length));
                            }
                            catch (JsonException) { continue; }
                            if (manifest?.Targets is null || manifest.Targets.Count is < 1 or > 12) throw new ArgumentException("Manifest must contain 1–12 targets.");
                            foreach (var target in manifest.Targets)
                            {
                                if (target is null || !job.AllowedRepositories.Contains(target.RepositoryName)) throw new ArgumentException("Manifest repository was not selected.");
                                ChatRuntimePolicy.ValidateTarget(target);
                                var repository = repositories.Get(target.RepositoryName);
                                if (repository.ProjectId != job.ProjectId) throw new ArgumentException("Repository project changed.");
                                ChatRuntimePolicy.SafePath(repository.RootPath, target.EntryPath);
                            }
                            lock (job.Sync)
                            {
                                if (job.Status != "waiting") continue;
                                job.Targets = manifest.Targets;
                                job.Status = "starting";
                                job.Execution = LaunchAsync(job, stoppingToken);
                            }
                        }
                        else if (job.Status == "running")
                        {
                            lock (job.Sync)
                            {
                                if (job.Status != "running") continue;
                                if (job.Runs.Any(id => runtime.FindRun(id)?.Status is not ("running" or "starting")))
                                {
                                    Stop(job.ProjectId, job.Id);
                                    job.Status = "failed"; job.Message = "一项服务退出，已关闭关联服务，请查看终端日志。";
                                }
                                else if (DateTime.UtcNow - job.LastVisit > TimeSpan.FromMinutes(job.IdleMinutes))
                                {
                                    Stop(job.ProjectId, job.Id);
                                    job.Message = $"测试窗口已空闲 {job.IdleMinutes} 分钟，整组服务已自动关闭。";
                                }
                            }
                        }
                        if (job.Status is not ("waiting" or "starting" or "running") && DateTime.UtcNow - job.CreatedAt > TimeSpan.FromHours(24))
                            _jobs.TryRemove(job.Id, out _);
                    }
                    catch (Exception ex)
                    {
                        lock (job.Sync)
                        {
                            if (job.Status == "stopped") continue;
                            foreach (var id in job.Runs) runtime.Stop(job.ProjectId, id);
                            job.Status = "failed"; job.Message = ex.Message;
                        }
                        logger.LogWarning(ex, "Runtime manifest rejected for {RequestId}", job.Id);
                        try { WriteResult(job); } catch (Exception outputError) { logger.LogWarning(outputError, "Unable to write runtime rejection {RequestId}", job.Id); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var job in _jobs.Values) Stop(job.ProjectId, job.Id);
            await Task.WhenAll(_jobs.Values.Select(x => x.Execution ?? Task.CompletedTask));
        }
    }

    private async Task LaunchAsync(Job job, CancellationToken stoppingToken)
    {
        // Leave the manifest polling loop free to discover/stop other groups.
        await Task.Yield();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
        linked.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            // Stable ordering preserves AI's backend dependency ordering.
            foreach (var target in job.Targets.OrderBy(x => x.Role == "frontend"))
            {
                linked.Token.ThrowIfCancellationRequested();
                var run = await runtime.StartTargetAsync(job.ProjectId, target, linked.Token, starting =>
                {
                    // Register before dependency installation so cancellation, PID and failure logs stay visible.
                    lock (job.Sync)
                    {
                        job.Runs.Add(starting.RunId);
                        if (job.Cancellation.IsCancellationRequested) runtime.Stop(job.ProjectId, starting.RunId);
                    }
                });
                await WaitReadyAsync(run, target.HealthPath, linked.Token);
            }
            lock (job.Sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                job.Status = "running"; job.LastVisit = DateTime.UtcNow;
                job.Message = "服务就绪，可在新窗口测试。";
            }
            WriteResult(job);
        }
        catch (Exception ex)
        {
            lock (job.Sync)
            {
                foreach (var id in job.Runs) runtime.Stop(job.ProjectId, id);
                if (job.Status != "stopped") { job.Status = "failed"; job.Message = ex is OperationCanceledException ? "启动取消或超时，已回收进程。" : ex.Message; }
            }
            try { WriteResult(job); } catch (Exception outputError) { logger.LogWarning(outputError, "Unable to write runtime result {RequestId}", job.Id); }
        }
    }

    private async Task WaitReadyAsync(CodeRuntimeRunDto run, string healthPath, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (runtime.FindRun(run.RunId)?.Status is not ("starting" or "running")) throw new InvalidOperationException($"{run.RepositoryName} exited before becoming ready.");
            try
            {
                using var response = await _probe.GetAsync($"http://127.0.0.1:{run.Port}{healthPath}", HttpCompletionOption.ResponseHeadersRead, token);
                if (response.IsSuccessStatusCode || (healthPath == "/" && (int)response.StatusCode < 500)) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested) { }
            await Task.Delay(1000, token);
        }
        throw new InvalidOperationException($"{run.RepositoryName}:{run.Port} did not pass its HTTP readiness check in three minutes.");
    }

    private void WriteResult(Job job)
    {
        var path = ChatRuntimePolicy.SafePath(job.Root, job.ResultPath);
        var temp = path + ".tmp";
        ChatRuntimePolicy.SafePath(job.Root, job.ResultPath + ".tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(Snapshot(job)), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private ChatRuntimeJobDto Snapshot(Job job)
    {
        lock (job.Sync) return new ChatRuntimeJobDto
        {
            RequestId = job.Id, ProjectId = job.ProjectId, ManifestRepository = job.Repository, ManifestPath = job.ManifestPath,
            Status = job.Status, Message = job.Message, IdleMinutes = job.IdleMinutes, LastVisitAt = job.LastVisit,
            Runs = job.Runs.Select(runtime.FindRun).OfType<CodeRuntimeRunDto>().ToList(),
            Targets = job.Targets.Select(x => new ChatRuntimeTarget { RepositoryName = x.RepositoryName, EntryPath = x.EntryPath, Role = x.Role, PagePath = x.PagePath, HealthPath = x.HealthPath, PreferredPort = x.PreferredPort }).ToList()
        };
    }

    private sealed class Job
    {
        public object Sync { get; } = new();
        public required string Id { get; init; }
        public required long ProjectId { get; init; }
        public required string Root { get; init; }
        public required string Repository { get; init; }
        public required HashSet<string> AllowedRepositories { get; init; }
        public required int IdleMinutes { get; init; }
        public string ManifestPath => $"artifacts/aiagent-runs/{Id}.json";
        public string ResultPath => $"artifacts/aiagent-runs/{Id}.result.json";
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public DateTime LastVisit { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "waiting";
        public string? Message { get; set; }
        public List<string> Runs { get; } = [];
        public List<ChatRuntimeTarget> Targets { get; set; } = [];
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? Execution { get; set; }
    }
}
