using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace AiAgent.Backend.Tests;

public sealed class ChatBootstrapCancellationTests
{
    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
    [InlineData("active-sessions")]
    [InlineData("deliveries")]
    [InlineData("heartbeat")]
    [InlineData("session-list")]
    [InlineData("session-detail")]
    [InlineData("auth-status")]
    public async Task AbandonedBootstrapRequestDoesNotEnterAuthOrThrow(string endpoint)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token };
        var calls = 0;
        var app = Create(context, Auth(() => { calls++; return Task.FromCanceled<AuthenticatedUser?>(cancelled.Token); }));

        var result = await Read(app, endpoint, cancelled.Token);

        Assert.Equal(499, Status(result));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
    [InlineData("active-sessions")]
    [InlineData("deliveries")]
    [InlineData("heartbeat")]
    [InlineData("session-list")]
    [InlineData("session-detail")]
    [InlineData("auth-status")]
    public async Task DisconnectDuringAuthenticationReturns499WithoutEscapingAction(string endpoint)
    {
        using var cancelled = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token };
        var app = Create(context, Auth(() => {
            cancelled.Cancel();
            return Task.FromCanceled<AuthenticatedUser?>(cancelled.Token);
        }));

        // AuthService returns Task.FromCanceled when navigation cancels before its
        // synchronous database lookup. Awaiting it would throw on the screenshot line.
        var exceptions = 0;
        void Observe(object? _, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is TaskCanceledException error && error.CancellationToken == cancelled.Token)
                Interlocked.Increment(ref exceptions);
        }
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        try { Assert.Equal(499, Status(await Read(app, endpoint, cancelled.Token))); }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Observe; }
        Assert.Equal(0, exceptions);
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
    [InlineData("active-sessions")]
    [InlineData("deliveries")]
    [InlineData("heartbeat")]
    [InlineData("session-list")]
    [InlineData("session-detail")]
    [InlineData("auth-status")]
    public async Task DisconnectAfterLookupSkipsUnauthorizedAndRead(string endpoint)
    {
        using var cancelled = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token };
        var app = Create(context, Auth(() => {
            cancelled.Cancel();
            return Task.FromResult<AuthenticatedUser?>(null);
        }));

        Assert.Equal(499, Status(await Read(app, endpoint, default)));
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
    [InlineData("active-sessions")]
    [InlineData("deliveries")]
    [InlineData("heartbeat")]
    [InlineData("session-list")]
    [InlineData("session-detail")]
    [InlineData("auth-status")]
    public async Task RealAuthFailureIsNotHiddenByDisconnect(string endpoint)
    {
        using var cancelled = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token };
        var failure = new InvalidOperationException("synthetic auth store failure");
        var app = Create(context, Auth(() => {
            cancelled.Cancel();
            return Task.FromException<AuthenticatedUser?>(failure);
        }));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Read(app, endpoint, default)));
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
    [InlineData("active-sessions")]
    [InlineData("deliveries")]
    [InlineData("heartbeat")]
    [InlineData("session-list")]
    [InlineData("session-detail")]
    [InlineData("auth-status")]
    public async Task ConnectedRequestDoesNotHideUnrelatedCancellation(string endpoint)
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        var app = Create(new DefaultHttpContext(), Auth(() => Task.FromCanceled<AuthenticatedUser?>(timeout.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(app, endpoint, default));
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
    [InlineData("active-sessions")]
    [InlineData("deliveries")]
    [InlineData("heartbeat")]
    [InlineData("session-list")]
    [InlineData("session-detail")]
    public async Task ConnectedUnauthenticatedRequestIsStillRejected(string endpoint)
    {
        var app = Create(new DefaultHttpContext(), Auth(() => Task.FromResult<AuthenticatedUser?>(null)));
        Assert.Equal(401, Status(await Read(app, endpoint, default)));
    }

    [Fact]
    public async Task AuthenticatedSkillsResponsePreservesCatalogShape()
    {
        var app = Create(new DefaultHttpContext(), Auth(() => Task.FromResult<AuthenticatedUser?>(new("fixture", "fixture"))));
        var response = await Read(app, "skills", default);
        if (response is IConvertToActionResult convert) response = convert.Convert();
        Assert.Same(ChatSkillCatalog.All, response is ObjectResult result ? result.Value : response);
    }

    private static int? Status(object response)
    {
        if (response is IConvertToActionResult convert) response = convert.Convert();
        return (response as IStatusCodeActionResult)?.StatusCode;
    }

    [Fact]
    public async Task DisconnectedSseRecoveryDoesNotDispatchOrWriteAnError()
    {
        using var abort = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = abort.Token };
        using var body = new System.IO.MemoryStream();
        context.Response.Body = body;
        var app = Create(context, Auth(() => {
            abort.Cancel();
            return Task.FromCanceled<AuthenticatedUser?>(abort.Token);
        }));
        await app.Chat.CompleteStream(new() { SessionId = "old", CommandId = "previous", RecoverOnly = true }, abort.Token);
        Assert.Equal(0, body.Length);
    }

    [Fact]
    public async Task AnonymousLoginStatusIsAConnectedSuccess()
    {
        var endpoints = Create(new DefaultHttpContext(), Auth(() => Task.FromResult<AuthenticatedUser?>(null)));
        var result = await endpoints.Auth.Status(default);
        Assert.Null(result.Result);
        Assert.False(result.Value!.Authenticated);
    }

    [Fact]
    public async Task SessionSwitchWhileAuthenticationIsPendingEndsOnlyThatRequest()
    {
        using var abort = new CancellationTokenSource();
        var pending = new TaskCompletionSource<AuthenticatedUser?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoints = Create(new DefaultHttpContext { RequestAborted = abort.Token }, Auth(() => pending.Task));
        var first = endpoints.Chat.Deliveries("old", abort.Token);
        Assert.False(first.IsCompleted);
        abort.Cancel();
        pending.SetCanceled(abort.Token);
        Assert.Equal(499, Status(await first));

        var next = Create(new DefaultHttpContext(), Auth(() => Task.FromResult<AuthenticatedUser?>(new("fixture", "fixture"))));
        Assert.True((await next.Auth.Status(default)).Value!.Authenticated);
    }

    private sealed record Endpoints(ChatAppService Chat, ChatSessionAppService Sessions, AuthAppService Auth);

    private static async Task<object> Read(Endpoints app, string endpoint, CancellationToken token)
    {
        switch (endpoint)
        {
            case "auth-status": return await app.Auth.Status(token);
            case "session-list": return await app.Sessions.List(cancellationToken: token);
            case "session-detail": return await app.Sessions.Get("session", token);
            case "skills": return await app.Chat.Skills(token);
            case "quota": return await app.Chat.GetCodexQuota(token);
            case "active-sessions": return await app.Chat.ActiveSessions(token);
            case "deliveries": return await app.Chat.Deliveries("session", token);
            case "heartbeat": return await app.Chat.CodexHeartbeat(new(), token);
            default: throw new ArgumentOutOfRangeException(nameof(endpoint));
        }
    }

    private static Endpoints Create(HttpContext context, IAuthService auth)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new(new(accessor, auth, null!, null!, null!, null!, null!), new(accessor, auth, null!, null!), new(accessor, auth));
    }

    private static IAuthService Auth(Func<Task<AuthenticatedUser?>> lookup)
    {
        var auth = DispatchProxy.Create<IAuthService, AuthProxy>();
        ((AuthProxy)(object)auth).Lookup = lookup;
        return auth;
    }

    public class AuthProxy : DispatchProxy
    {
        public Func<Task<AuthenticatedUser?>> Lookup { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IAuthService.TryGetCurrentUserAsync) ? Lookup() : throw new NotSupportedException();
    }
}
