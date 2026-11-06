using System.Reflection;
using System.Runtime.ExceptionServices;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AiAgent.Backend.Tests;

[Collection("Swagger metadata")]
public sealed class KnowledgeCleanupCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedCleanupRequestReturns499WithoutAwaitingCancelledAuthentication(bool write)
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Request.Headers.Authorization = "Bearer fixture-session";
        var auth = new AuthService(null!, new ConfigurationBuilder().Build(), null!);
        var controller = Controller(context, auth);
        cancellation.Cancel();
        var exceptions = 0;
        EventHandler<FirstChanceExceptionEventArgs> observer = (_, args) => { if (args.Exception is TaskCanceledException) exceptions++; };
        AppDomain.CurrentDomain.FirstChanceException += observer;
        try
        {
            var result = await Invoke(controller, write, cancellation.Token);
            Assert.Equal(499, Assert.IsType<StatusCodeResult>(result).StatusCode);
            Assert.Equal(0, exceptions);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= observer; }
    }

    [Fact]
    public async Task DisconnectDuringAuthenticationDoesNotAwaitAnAlreadyCancelledLookup()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var auth = DispatchProxy.Create<IAuthService, CancellationAuth>();
        var calls = 0;
        ((CancellationAuth)(object)auth).Lookup = _ => {
            calls++; cancellation.Cancel();
            return Task.FromCanceled<AuthenticatedUser?>(cancellation.Token);
        };

        var result = await Invoke(Controller(context, auth), false, cancellation.Token);

        Assert.Equal(499, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisconnectAfterAuthenticationStartedReturns499()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var auth = DispatchProxy.Create<IAuthService, CancellationAuth>();
        var lookup = new TaskCompletionSource<AuthenticatedUser?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((CancellationAuth)(object)auth).Lookup = _ => lookup.Task;
        var pending = Invoke(Controller(context, auth), false, cancellation.Token);
        Assert.False(pending.IsCompleted);

        cancellation.Cancel();
        lookup.SetCanceled(cancellation.Token);

        Assert.Equal(499, Assert.IsType<StatusCodeResult>(await pending).StatusCode);
    }

    [Fact]
    public async Task DisconnectStillRecognizedWhenHttpContextAccessorIsCleared()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var accessor = new HttpContextAccessor { HttpContext = context };
        var auth = DispatchProxy.Create<IAuthService, CancellationAuth>();
        ((CancellationAuth)(object)auth).Lookup = _ => {
            accessor.HttpContext = null;
            cancellation.Cancel();
            return Task.FromCanceled<AuthenticatedUser?>(cancellation.Token);
        };
        var controller = new KnowledgeResourceCleanupAppService(new(null!, null!, auth, accessor));

        Assert.Equal(499, Assert.IsType<StatusCodeResult>(await Invoke(controller, false, cancellation.Token)).StatusCode);
    }

    [Fact]
    public async Task DisconnectWhileWaitingForCleanupGateReturns499WithoutDatabaseAccess()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var auth = DispatchProxy.Create<IAuthService, CancellationAuth>();
        var service = new KnowledgeResourceCleanupService(null!, null!, auth, new HttpContextAccessor { HttpContext = context });
        var gate = (SemaphoreSlim)typeof(KnowledgeResourceCleanupService).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        await gate.WaitAsync();
        try
        {
            var pending = Invoke(new(service), true, cancellation.Token);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            var result = await pending;
            Assert.Equal(499, Assert.IsType<StatusCodeResult>(result).StatusCode);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellationWithoutHttpDisconnectStillPropagates()
    {
        var auth = new AuthService(null!, new ConfigurationBuilder().Build(), null!);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Invoke(Controller(new DefaultHttpContext(), auth), false, new CancellationToken(true)));
    }

    [Fact]
    public async Task MissingLoginStillFailsAuthorization()
    {
        var auth = new AuthService(null!, new ConfigurationBuilder().Build(), null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Invoke(Controller(new DefaultHttpContext(), auth), false, default));
    }

    [Fact]
    public async Task RealAuthenticationFailureIsNotHiddenByDisconnect()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var auth = DispatchProxy.Create<IAuthService, CancellationAuth>();
        var failure = new InvalidOperationException("fixture authentication database failure");
        ((CancellationAuth)(object)auth).Lookup = _ => { cancellation.Cancel(); throw failure; };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(Controller(context, auth), false, cancellation.Token));
        Assert.Same(failure, error);
    }

    private static KnowledgeResourceCleanupAppService Controller(HttpContext context, IAuthService auth)
        => new(new(null!, null!, auth, new HttpContextAccessor { HttpContext = context }));

    private static async Task<object> Invoke(KnowledgeResourceCleanupAppService controller, bool write, CancellationToken cancellation)
        => write ? await controller.Cleanup(new() { DocumentIds = [1], SnapshotSha256 = "fixture" }, cancellation)
            : await controller.Preview(cancellation);

    public class CancellationAuth : DispatchProxy
    {
        public Func<CancellationToken, Task<AuthenticatedUser?>> Lookup = _ => Task.FromResult<AuthenticatedUser?>(new("fixture", "fixture"));
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IAuthService.TryGetCurrentUserAsync) ? Lookup((CancellationToken)args![1]!) : throw new NotSupportedException();
    }
}
