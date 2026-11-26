using System;
using System.Reflection;
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
    public async Task DisconnectDuringAuthenticationReturns499WithoutEscapingAction(string endpoint)
    {
        using var cancelled = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token };
        var app = Create(context, Auth(() => {
            cancelled.Cancel();
            return Task.FromCanceled<AuthenticatedUser?>(cancelled.Token);
        }));

        var result = await Read(app, endpoint, cancelled.Token);

        Assert.Equal(499, Status(result));
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("quota")]
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
    public async Task ConnectedUnauthenticatedRequestIsStillRejected(string endpoint)
    {
        var app = Create(new DefaultHttpContext(), Auth(() => Task.FromResult<AuthenticatedUser?>(null)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(app, endpoint, default));
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

    private static async Task<object> Read(ChatAppService app, string endpoint, CancellationToken token) =>
        endpoint == "skills" ? await app.Skills(token) : (object)await app.GetCodexQuota(token);

    private static ChatAppService Create(HttpContext context, IAuthService auth) => new(
        new HttpContextAccessor { HttpContext = context }, auth, null!, null!, null!, null!, null!);

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
