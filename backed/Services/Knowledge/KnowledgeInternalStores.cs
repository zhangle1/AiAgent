using AiAgent.Backend.Entities.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SqlSugar;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Personal raw conversations never enter the legacy shared-knowledge-base surface.</summary>
public static class KnowledgeInternalStores
{
    public const string Sessions = "kunbuddy-session-store";
    private static readonly object CreationGate = new();
    public static void RequirePublic(string normalizedName)
    {
        if (string.Equals(normalizedName, Sessions, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Personal conversations are available only through the scoped workspace.");
    }
    public static AiKnowledgeBase EnsureSessions(ISqlSugarClient db, IKnowledgePathService paths)
    {
        lock (CreationGate)
        {
            var existing = db.Queryable<AiKnowledgeBase>().First(row => row.Name == Sessions && !row.IsDeleted);
            if (existing is not null) return existing;
            Directory.CreateDirectory(paths.GetRawPath(Sessions));
            var row = new AiKnowledgeBase { Name = Sessions, DisplayName = "个人会话原文", EngineType = "llamaindex", Status = "ready", IsDefault = false, UpdatedAt = DateTime.UtcNow };
            row.Id = db.Insertable(row).ExecuteReturnBigIdentity();
            return row;
        }
    }
}

/// <summary>Runs before legacy controller construction, covering read, download, search and mutation routes.</summary>
public sealed class KnowledgeInternalStoreFilter(IKnowledgePathService paths) : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var name = context.RouteData.Values["kbName"]?.ToString();
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                if (paths.NormalizeName(name) == KnowledgeInternalStores.Sessions) context.Result = new NotFoundResult();
            }
            catch (ArgumentException) { /* The normal action validates malformed public names. */ }
        }
        return Task.CompletedTask;
    }
}
