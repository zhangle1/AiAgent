using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Services.Admin;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Project names are labels; the registered project ID keeps knowledge URIs stable on rename.</summary>
public sealed class KnowledgeProjectDirectoryService
{
    public List<AiCodeProject> List(ISqlSugarClient db, IHttpContextAccessor accessor)
    {
        var user = KnowledgeUserScope.ResolveUser(db, accessor);
        if (user is null) return [];
        var ids = new ProjectAccessService(db).GetAccessibleProjectIds(user);
        return db.Queryable<AiCodeProject>().Where(p => !p.IsDeleted && ids.Contains(p.Id)).ToList();
    }

    public static string UriFor(long id) => $"viking://projects/project-{id}/";

    public static bool IsVisible(string uri, HashSet<long> ids)
    {
        const string prefix = "viking://projects/project-";
        if (!uri.StartsWith(prefix, StringComparison.Ordinal)) return true;
        var segment = uri[prefix.Length..].Split('/')[0];
        return !long.TryParse(segment, out var id) || ids.Contains(id);
    }
}
