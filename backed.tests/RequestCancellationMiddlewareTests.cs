using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Middleware;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgent.Backend.Tests;

public sealed class RequestCancellationMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/knowledge-resources/tree")]
    [InlineData("/api/v1/knowledge-agent/options")]
    public async Task DisconnectedKnowledgeRequestStopsBeforeAuthentication(string path)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = Context(path, cancellation.Token);
        var entered = false;

        await Invoke(context, _ => { entered = true; throw new InvalidOperationException("Must not authenticate a disconnected request."); });

        Assert.False(entered);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task DisconnectBetweenRequestEntryAndAuthDoesNotEscapeOrReturnUnauthorized()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context("/api/v1/knowledge-resources/tree", cancellation.Token);
        var auth = new AuthService(null!, new ConfigurationBuilder().Build(), null!);
        var reachedEndpoint = false;

        await Invoke(context, async request =>
        {
            cancellation.Cancel();
            await auth.TryGetCurrentUserAsync(request, request.RequestAborted);
            reachedEndpoint = true;
        });

        Assert.False(reachedEndpoint);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task DisconnectDuringAsyncEndpointIsHandled()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context("/api/v1/knowledge-resources/read", cancellation.Token);

        await Invoke(context, async request =>
        {
            await Task.Yield();
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, request.RequestAborted);
        });

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task UnrelatedTimeoutStillPropagates()
    {
        var context = Context("/api/v1/knowledge-resources/read");
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Invoke(context, _ => Task.FromCanceled(timeout.Token)));

        Assert.Equal(timeout.Token, error.CancellationToken);
    }

    [Fact]
    public async Task RealFailureStillPropagatesEvenIfClientDisconnected()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context("/api/v1/knowledge-resources/read", cancellation.Token);
        var failure = new InvalidOperationException("fixture database failure");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(context, _ =>
        {
            cancellation.Cancel();
            throw failure;
        }));

        Assert.Same(failure, error);
    }

    [Fact]
    public async Task DisconnectAfterResponseStartedPreservesStatusAndBody()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context("/api/v1/knowledge-agent/chat", cancellation.Token);
        await context.Response.WriteAsync("data: ready\n\n");
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Invoke(context, _ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });

        Assert.Equal(200, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal("data: ready\n\n", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task RapidKnowledgePageReentryDoesNotCancelTheNextRequest()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        var entered = 0;
        app.UseMiddleware<RequestCancellationMiddleware>();
        app.Run(async request => { entered++; await request.Response.WriteAsync("fixture resource"); });
        var pipeline = app.Build();

        for (var index = 0; index < 20; index++)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var abandoned = Context("/api/v1/knowledge-resources/tree", cancellation.Token);
            await pipeline(abandoned);
            Assert.Equal(499, abandoned.Response.StatusCode);

            var current = Context("/api/v1/knowledge-resources/tree");
            await pipeline(current);
            Assert.Equal(200, current.Response.StatusCode);
            current.Response.Body.Position = 0;
            Assert.Equal("fixture resource", await new StreamReader(current.Response.Body).ReadToEndAsync());
        }

        Assert.Equal(20, entered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectedRequestKeepsSuccessfulAndUnauthorizedResponses(bool authenticated)
    {
        var context = Context("/api/v1/knowledge-resources/tree");
        await Invoke(context, async request =>
        {
            request.Response.StatusCode = authenticated ? 200 : 401;
            await request.Response.WriteAsync(authenticated ? "fixture resource" : "login required");
        });

        Assert.Equal(authenticated ? 200 : 401, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal(authenticated ? "fixture resource" : "login required", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private static DefaultHttpContext Context(string path, CancellationToken cancellation = default)
    {
        var context = new DefaultHttpContext { RequestAborted = cancellation };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task Invoke(HttpContext context, RequestDelegate endpoint)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseMiddleware<RequestCancellationMiddleware>();
        app.Run(endpoint);
        await app.Build()(context);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
