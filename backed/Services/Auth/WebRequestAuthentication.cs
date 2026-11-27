using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Auth;

// Capture the request once: a disconnected request and a connected anonymous user
// need different HTTP results, even when both have no current user.
internal sealed class WebRequestAuthentication(HttpContext context, CancellationToken token)
{
    internal bool Disconnected => context.RequestAborted.IsCancellationRequested;

    internal async Task<AuthenticatedUser?> LookupAsync(Func<HttpContext, CancellationToken, Task<AuthenticatedUser?>> lookup)
    {
        if (Disconnected) return null;
        try
        {
            var task = lookup(context, token);
            // AuthService returns an already-cancelled task if navigation won this race.
            if (task.IsCanceled && Disconnected) return null;
            return await task;
        }
        catch (OperationCanceledException) when (Disconnected) { return null; }
    }

    internal async Task<ActionResult<T>> ReadAsync<T>(IAuthService auth,
        Func<AuthenticatedUser, CancellationToken, Task<ActionResult<T>>> read)
    {
        var user = await LookupAsync(auth.TryGetCurrentUserAsync);
        if (Disconnected) return new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
        if (user == null) return new UnauthorizedResult();
        try
        {
            var task = read(user, token);
            if (task.IsCanceled && Disconnected) return new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
            var result = await task;
            if (Disconnected) return new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
            return result;
        }
        catch (OperationCanceledException) when (Disconnected)
        {
            return new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
        }
    }
}
