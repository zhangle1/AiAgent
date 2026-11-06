namespace AiAgent.Backend.Middleware;

/// <summary>Ends disconnected HTTP requests without treating client cancellation as an application failure.</summary>
public sealed class RequestCancellationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            CompleteDisconnectedRequest(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            CompleteDisconnectedRequest(context);
        }
    }

    private static void CompleteDisconnectedRequest(HttpContext context)
    {
        // The client is gone: do not write an error body or alter an already-started stream.
        if (!context.Response.HasStarted)
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
    }
}
