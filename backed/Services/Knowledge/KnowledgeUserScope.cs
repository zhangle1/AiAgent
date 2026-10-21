using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text;

namespace AiAgent.Backend.Services.Knowledge;

public static class KnowledgeUserScope
{
    public static string Resolve(ISqlSugarClient db, IHttpContextAccessor? accessor)
    {
        var user = ResolveUser(db, accessor);
        return RootFor(user?.Username ?? "current", user?.Id ?? "current");
    }

    public static string RootFor(string username, string id)
    {
        try { username = KnowledgeResourceService.ValidateName(username); }
        catch (ArgumentException) { username = KnowledgeResourceService.ValidateName(id); }
        return KnowledgeResourceService.Roots[2] + Uri.EscapeDataString(username) + "/";
    }

    public static bool CanReadAllSessions(ISqlSugarClient db, IHttpContextAccessor? accessor)
        => ResolveUser(db, accessor)?.IsAdministrator == true;

    public static bool IsSessionScope(string uri, bool includeWorkspaceRoot = false)
    {
        if (!uri.StartsWith(KnowledgeResourceService.Roots[2], StringComparison.Ordinal)) return false;
        var parts = uri[KnowledgeResourceService.Roots[2].Length..].TrimEnd('/').Split('/');
        return parts.Length >= 2 && parts[1] == "sessions" || includeWorkspaceRoot && parts.Length == 1 && parts[0].Length > 0;
    }

    private static AuthenticatedUser? ResolveUser(ISqlSugarClient db, IHttpContextAccessor? accessor)
    {
        var context = accessor?.HttpContext;
        string? token = null;
        if (context is not null)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = authorization["Bearer ".Length..].Trim();
            else
                context.Request.Cookies.TryGetValue(AuthService.CookieName, out token);
        }
        if (!string.IsNullOrWhiteSpace(token))
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            var session = db.Queryable<AiUserSession>().First(x => x.TokenHash == hash && x.Purpose == null && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow);
            var user = session is null ? null : db.Queryable<AiUser>().First(x => x.Id == session.UserId && !x.IsDisabled);
            if (user is not null) return new(user.Id, user.Username, user.Role, user.CanCommitCode, user.Alias);
        }
        return null;
    }

}
