using AiAgent.Backend.Dtos.DashboardApp;
using AiAgent.Backend.Services.Git;

namespace AiAgent.Backend.Services.DashboardApp;

public interface IDashboardGitService
{
    Task<object> StatusAsync(string applicationId, CancellationToken cancellationToken);
    Task<object> PullAsync(string applicationId, CancellationToken cancellationToken);
    Task<object> CommitAndPushAsync(string applicationId, DashboardGitPushRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Dashboard adapter for the shared Git workspace service. Dashboard path authorization remains in its workspace service.
/// </summary>
public sealed class DashboardGitService : IDashboardGitService
{
    private readonly IDashboardApplicationWorkspace _workspace;
    private readonly IGitWorkspaceService _git;

    public DashboardGitService(IDashboardApplicationWorkspace workspace, IGitWorkspaceService git)
    {
        _workspace = workspace;
        _git = git;
    }

    public async Task<object> StatusAsync(string applicationId, CancellationToken cancellationToken)
    {
        var app = await _workspace.GetAsync(applicationId, cancellationToken);
        return await _git.StatusAsync($"dashboard:{app.Id}", app.RootPath, cancellationToken);
    }

    public async Task<object> PullAsync(string applicationId, CancellationToken cancellationToken)
    {
        var app = await _workspace.GetAsync(applicationId, cancellationToken);
        return await _git.PullAsync($"dashboard:{app.Id}", app.RootPath, cancellationToken);
    }

    public async Task<object> CommitAndPushAsync(string applicationId, DashboardGitPushRequest request, CancellationToken cancellationToken)
    {
        var app = await _workspace.GetAsync(applicationId, cancellationToken);
        var message = string.IsNullOrWhiteSpace(request.Message) ? $"feat(dashboard): update {app.Name}" : request.Message;
        return await _git.CommitAndPushAsync($"dashboard:{app.Id}", app.RootPath, message, cancellationToken);
    }
}
