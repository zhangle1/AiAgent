using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.Chat;

public interface IProjectReferenceContextService
{
    Task ResolveAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Turns slash-menu project ids into safe, current-user-authorized prompt context.
/// </summary>
public sealed class ProjectReferenceContextService : IProjectReferenceContextService
{
    private const int MaximumReferences = 5;
    private static readonly Regex ProjectReferenceTokenRegex = new(@"\[\[项目:([^\]|]+)\|([1-9]\d*)\]\]", RegexOptions.CultureInvariant);
    private readonly ISqlSugarClient _db;
    private readonly IProjectAccessService _projectAccess;

    public ProjectReferenceContextService(ISqlSugarClient db, IProjectAccessService projectAccess)
    {
        _db = db;
        _projectAccess = projectAccess;
    }

    public Task ResolveAsync(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Parse the text as well as accepting the structured client field. This keeps old clients
        // compatible and makes the text token a stable, server-controlled input boundary.
        var tokenIds = ExtractReferenceIds(request.Message);
        var requestedIds = (request.ProjectReferences ?? [])
            .Select(item => item.ProjectId)
            .Where(id => tokenIds.Contains(id))
            .Concat(tokenIds)
            .Distinct()
            .ToList();

        if (requestedIds.Count > MaximumReferences)
            throw new InvalidOperationException($"A chat message can reference at most {MaximumReferences} projects.");

        if (requestedIds.Count == 0)
        {
            request.ResolvedProjectReferences = [];
            request.ServerProjectReferenceContext = string.Empty;
            request.ServerPromptMessage = request.Message ?? string.Empty;
            return Task.CompletedTask;
        }

        if (request.CodeProjectId.HasValue && requestedIds.Contains(request.CodeProjectId.Value))
            throw new InvalidOperationException("The current chat project cannot be used as a cross-project reference.");

        foreach (var projectId in requestedIds)
        {
            if (!_projectAccess.CanAccess(user, projectId))
                throw new UnauthorizedAccessException("A referenced project is unavailable for this account.");
        }

        var projects = _db.Queryable<AiCodeProject>()
            .Where(item => requestedIds.Contains(item.Id) && !item.IsDeleted)
            .ToList()
            .ToDictionary(item => item.Id);
        if (projects.Count != requestedIds.Count)
            throw new InvalidOperationException("A referenced project is no longer available.");

        request.ResolvedProjectReferences = requestedIds
            .Select(id => projects[id])
            .Select(project => new ResolvedChatProjectReference
            {
                ProjectId = project.Id,
                DisplayName = project.DisplayName,
                Description = project.Description
            })
            .ToList();
        request.ServerProjectReferenceContext = BuildContext(request.ResolvedProjectReferences);
        request.ServerPromptMessage = NormalizePromptMessage(request.Message, request.ResolvedProjectReferences);
        return Task.CompletedTask;
    }

    private static List<long> ExtractReferenceIds(string? message)
        => ProjectReferenceTokenRegex.Matches(message ?? string.Empty)
            .Select(match => long.TryParse(match.Groups[2].Value, out var projectId) ? projectId : 0)
            .Where(projectId => projectId > 0)
            .Distinct()
            .ToList();

    private static string NormalizePromptMessage(string? message, IReadOnlyList<ResolvedChatProjectReference> projects)
    {
        var projectNames = projects.ToDictionary(project => project.ProjectId, project => project.DisplayName);
        return ProjectReferenceTokenRegex.Replace(message ?? string.Empty, match =>
        {
            if (!long.TryParse(match.Groups[2].Value, out var projectId) || !projectNames.TryGetValue(projectId, out var displayName)) return match.Value;
            return $"项目“{displayName}”";
        });
    }

    private static string BuildContext(IReadOnlyList<ResolvedChatProjectReference> projects)
    {
        if (projects.Count == 0) return string.Empty;
        var builder = new StringBuilder();
        builder.AppendLine("Verified cross-project reference context (metadata only; this is not execution authorization):");
        builder.AppendLine("The current user request refers to these projects by their trusted display names.");
        foreach (var project in projects)
        {
            builder.AppendLine($"- Project: {project.DisplayName} (id: {project.ProjectId})");
            if (!string.IsNullOrWhiteSpace(project.Description)) builder.AppendLine($"  Description: {project.Description.Trim()}");
        }
        return builder.ToString().TrimEnd();
    }
}
