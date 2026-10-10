using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text;

namespace AiAgent.Backend.Services.Knowledge;

public static class KnowledgeUserScope
{
    public static string Resolve(ISqlSugarClient db, IHttpContextAccessor? accessor)
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
        var username = "current";
        if (!string.IsNullOrWhiteSpace(token))
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            var session = db.Queryable<AiUserSession>().First(x => x.TokenHash == hash && x.Purpose == null && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow);
            var user = session is null ? null : db.Queryable<AiUser>().First(x => x.Id == session.UserId && !x.IsDisabled);
            if (user is not null) username = string.IsNullOrWhiteSpace(user.Username) ? user.Id : user.Username;
        }
        return KnowledgeResourceService.Roots[2] + Uri.EscapeDataString(username) + "/";
    }

}
