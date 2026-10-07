using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AiAgent.Backend.Services.CodeRepository;

public sealed class CodeRuntimeAccessFilter(IAuthService auth, IProjectAccessService access, ICodeRuntimeManager runtime) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = await auth.TryGetCurrentUserAsync(context.HttpContext, context.HttpContext.RequestAborted);
        if (user is null) { context.Result = new UnauthorizedResult(); return; }
        long? projectId = context.ActionArguments.TryGetValue("projectId", out var value) && value is long id ? id : null;
        if (!projectId.HasValue && context.ActionArguments.TryGetValue("runId", out var runId) && runId is string run)
            projectId = runtime.FindRun(run)?.ProjectId;
        if (!projectId.HasValue) { context.Result = new NotFoundResult(); return; }
        if (!access.CanAccess(user, projectId.Value)) { context.Result = new StatusCodeResult(403); return; }
        await next();
    }
}
