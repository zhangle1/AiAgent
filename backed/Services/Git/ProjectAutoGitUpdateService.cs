using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Entities.CodeRepository;
using SqlSugar;

namespace AiAgent.Backend.Services.Git;

public interface IProjectAutoGitUpdateService
{
    CodeProjectAutoGitUpdateDto Get(long projectId);
    CodeProjectAutoGitUpdateDto Save(long projectId, CodeProjectAutoGitUpdateRequest request);
    Task RunDueProjectsAsync(CancellationToken cancellationToken);
}

/// <summary>Runs administrator-configured project Git updates on a conservative schedule.</summary>
public sealed class ProjectAutoGitUpdateService : IProjectAutoGitUpdateService
{
    private readonly ISqlSugarClient _db;
    private readonly ICodeRepositoryGitService _git;
    private readonly ILogger<ProjectAutoGitUpdateService> _logger;

    public ProjectAutoGitUpdateService(ISqlSugarClient db, ICodeRepositoryGitService git, ILogger<ProjectAutoGitUpdateService> logger)
        => (_db, _git, _logger) = (db, git, logger);

    public CodeProjectAutoGitUpdateDto Get(long projectId) => ToDto(Find(projectId));

    public CodeProjectAutoGitUpdateDto Save(long projectId, CodeProjectAutoGitUpdateRequest request)
    {
        var project = Find(projectId);
        if (request.IntervalHours is < 1 or > 168) throw new ArgumentException("自动更新间隔必须在 1 到 168 小时之间。");
        project.AutoGitUpdateEnabled = request.Enabled;
        project.AutoGitUpdateIntervalHours = request.IntervalHours;
        project.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(project).ExecuteCommand();
        return ToDto(project);
    }

    public async Task RunDueProjectsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var projects = _db.Queryable<AiCodeProject>().Where(project => !project.IsDeleted && project.AutoGitUpdateEnabled).ToList();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interval = TimeSpan.FromHours(Math.Clamp(project.AutoGitUpdateIntervalHours, 1, 168));
            if (project.AutoGitUpdateLastAttemptedAt.HasValue && now - project.AutoGitUpdateLastAttemptedAt.Value < interval) continue;

            _db.Updateable<AiCodeProject>()
                .SetColumns(item => item.AutoGitUpdateLastAttemptedAt == now)
                .SetColumns(item => item.AutoGitUpdateLastResult == "正在执行自动重置更新。")
                .SetColumns(item => item.UpdatedAt == now)
                .Where(item => item.Id == project.Id && !item.IsDeleted && item.AutoGitUpdateEnabled)
                .ExecuteCommand();
            try
            {
                var result = await _git.ProjectAutomaticDiscardChangesAndPullAsync(project.Id, cancellationToken);
                var completedAt = DateTime.UtcNow;
                var completed = result.Repositories.Count > 0 && result.Repositories.All(row => row.Outcome == "succeeded");
                var summary = Summarize(result);
                if (completed)
                {
                    _db.Updateable<AiCodeProject>()
                        .SetColumns(item => item.AutoGitUpdateLastResult == summary)
                        .SetColumns(item => item.AutoGitUpdateLastSucceededAt == completedAt)
                        .SetColumns(item => item.UpdatedAt == completedAt)
                        .Where(item => item.Id == project.Id && !item.IsDeleted)
                        .ExecuteCommand();
                }
                else
                {
                    _db.Updateable<AiCodeProject>()
                        .SetColumns(item => item.AutoGitUpdateLastResult == summary)
                        .SetColumns(item => item.UpdatedAt == completedAt)
                        .Where(item => item.Id == project.Id && !item.IsDeleted)
                        .ExecuteCommand();
                }
                _logger.LogInformation("Project automatic Git update finished. ProjectId:{ProjectId}, Succeeded:{Succeeded}, Skipped:{Skipped}, Failed:{Failed}", project.Id, result.Repositories.Count(row => row.Outcome == "succeeded"), result.Repositories.Count(row => row.Outcome == "skipped"), result.Repositories.Count(row => row.Outcome == "failed"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = Shorten(ex.Message, "自动重置更新失败，需要人工处理。");
                _db.Updateable<AiCodeProject>()
                    .SetColumns(item => item.AutoGitUpdateLastResult == message)
                    .SetColumns(item => item.UpdatedAt == DateTime.UtcNow)
                    .Where(item => item.Id == project.Id && !item.IsDeleted)
                    .ExecuteCommand();
                _logger.LogError(ex, "Project automatic Git update failed. ProjectId:{ProjectId}", project.Id);
            }
        }
    }

    private AiCodeProject Find(long projectId) => _db.Queryable<AiCodeProject>().First(project => project.Id == projectId && !project.IsDeleted)
        ?? throw new InvalidOperationException("所选项目不存在或已被删除。");

    private static CodeProjectAutoGitUpdateDto ToDto(AiCodeProject project) => new()
    {
        ProjectId = project.Id,
        Enabled = project.AutoGitUpdateEnabled,
        IntervalHours = Math.Clamp(project.AutoGitUpdateIntervalHours, 1, 168),
        LastAttemptedAt = project.AutoGitUpdateLastAttemptedAt,
        LastSucceededAt = project.AutoGitUpdateLastSucceededAt,
        LastResult = project.AutoGitUpdateLastResult
    };

    private static string Summarize(ProjectGitBatchOperationResult result)
    {
        if (result.Repositories.Count == 0) return "项目没有可自动更新的 Git 代码库。";
        var succeeded = result.Repositories.Count(row => row.Outcome == "succeeded");
        var skipped = result.Repositories.Count(row => row.Outcome == "skipped");
        var failed = result.Repositories.Count(row => row.Outcome == "failed");
        var firstIssue = result.Repositories.FirstOrDefault(row => row.Outcome != "succeeded")?.Message;
        return Shorten($"自动更新：成功 {succeeded}，跳过 {skipped}，失败 {failed}。{firstIssue}", "自动更新完成。");
    }

    private static string Shorten(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length > 1000 ? text[..1000] : text;
    }
}

public sealed class ProjectAutoGitUpdateHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private readonly IProjectAutoGitUpdateService _updates;
    private readonly ILogger<ProjectAutoGitUpdateHostedService> _logger;

    public ProjectAutoGitUpdateHostedService(IProjectAutoGitUpdateService updates, ILogger<ProjectAutoGitUpdateHostedService> logger)
        => (_updates, _logger) = (updates, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _updates.RunDueProjectsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Project automatic Git update scheduler failed."); }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
