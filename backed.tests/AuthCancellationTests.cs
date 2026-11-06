using System;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AiAgent.Backend.Tests;

public sealed class AuthCancellationTests
{
    [Theory]
    [InlineData("web")]
    [InlineData("plugin")]
    [InlineData("kunbuddy")]
    public async Task DisconnectedRequestReturnsCancelledLookupWithoutSynchronousException(string purpose)
    {
        // No database is needed: a disconnected request must stop before resolving its session.
        var auth = new AuthService(null!, new ConfigurationBuilder().Build(), null!);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Request.Headers.Authorization = "Bearer fixture-session";
        Task<AuthenticatedUser?>? lookup = null;

        var error = Record.Exception(() =>
        {
            lookup = purpose switch
            {
                "plugin" => auth.TryGetPluginUserAsync(context, cancellation.Token),
                "kunbuddy" => auth.TryGetKunBuddyUserAsync(context, cancellation.Token),
                _ => auth.TryGetCurrentUserAsync(context, cancellation.Token)
            };
        });

        Assert.Null(error);
        Assert.NotNull(lookup);
        Assert.True(lookup.IsCanceled);
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }
}
