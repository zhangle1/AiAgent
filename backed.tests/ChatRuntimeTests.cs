using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.CodeRepository;
using Microsoft.Extensions.Logging.Abstractions;

public class ChatRuntimeTests
{
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("/absolute.json")]
    [InlineData("a/../../secret.json")]
    [InlineData("a\\file.json")]
    [InlineData("a:stream")]
    public void RejectsEscapingPaths(string path)
        => Assert.Throws<ArgumentException>(() => ChatRuntimePolicy.SafePath(Path.GetTempPath(), path));

    [Theory]
    [InlineData("//outside/route")]
    [InlineData("/\\outside")]
    [InlineData("/hello\r\nHeader:value")]
    [InlineData("https://outside/")]
    public void RejectsExternalPagePaths(string path)
        => Assert.Throws<ArgumentException>(() => ChatRuntimePolicy.ValidatePagePath(path));

    [Fact]
    public void RequiresExplicitPortAndRestrictsEnvironment()
    {
        var target = new ChatRuntimeTarget();
        Assert.Throws<ArgumentException>(() => ChatRuntimePolicy.ValidateTarget(target));
        target.PreferredPort = 45100;
        target.Environment["NODE_OPTIONS"] = "--require arbitrary.js";
        Assert.Throws<ArgumentException>(() => ChatRuntimePolicy.ValidateTarget(target));
        target.Environment.Clear();
        target.Environment["NEXT_PUBLIC_API_BASE_URL"] = "http://example.test:45101";
        ChatRuntimePolicy.ValidateTarget(target);
    }

    [Fact]
    public async Task RealDotnetAndNpmStartInOrderVisitRenewsAndIdleStopsBoth()
    {
        using var fixture = new Fixture();
        var apiPort = FreePort();
        var webPort = FreePort();
        while (webPort == apiPort) webPort = FreePort();
        var job = fixture.Prepare();
        // Intentionally put the frontend first. It exits if the backend is not ready.
        fixture.Manifest(job, new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", Role = "frontend", PreferredPort = webPort,
            Environment = new() { ["API_BASE_URL"] = $"http://127.0.0.1:{apiPort}/health" } },
            new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "api/Fixture.csproj", Role = "backend", PreferredPort = apiPort, HealthPath = "/health" });
        await fixture.Service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => fixture.Job(job).Status is "running" or "failed", 90);
            Assert.Equal("running", fixture.Job(job).Status);
            var runs = fixture.Job(job).Runs;
            Assert.Equal(new[] { "backend", "frontend" }, runs.Select(x => x.Role));
            Assert.All(runs, run => Assert.True(run.ProcessId > 0));
            using var client = new HttpClient();
            Assert.Equal("frontend-ready", await client.GetStringAsync($"http://127.0.0.1:{webPort}/"));
            Assert.True(File.Exists(Path.Combine(fixture.Root, job.ManifestPath.Replace(".json", ".result.json"))));
            fixture.AgeVisit(job);
            fixture.Service.Visit(7, job.RequestId);
            await Task.Delay(3500);
            Assert.Equal("running", fixture.Job(job).Status);
            fixture.AgeVisit(job);
            await Until(() => fixture.Job(job).Status == "stopped");
            await Until(() => !Listening(apiPort) && !Listening(webPort));
        }
        finally { await fixture.Service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task OccupiedPortFailsWithoutFallbackAndManualStopKillsManagedTree()
    {
        using var fixture = new Fixture();
        var port = FreePort();
        var target = new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", Role = "backend", PreferredPort = port };
        var run = await fixture.Runtime.StartTargetAsync(7, target, CancellationToken.None);
        try
        {
            await Until(() => Listening(port));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime.StartTargetAsync(7, target, CancellationToken.None));
            Assert.False(fixture.Runtime.Stop(8, run.RunId));
            Assert.True(Listening(port));
            Assert.True(fixture.Runtime.Stop(7, run.RunId));
            await Until(() => !Listening(port));
        }
        finally { fixture.Runtime.Stop(7, run.RunId); }
    }

    [Fact]
    public async Task InvalidManifestWritesFailureAndNeverStartsUnselectedRepository()
    {
        using var fixture = new Fixture();
        var job = fixture.Prepare();
        fixture.Manifest(job, new ChatRuntimeTarget { RepositoryName = "other", EntryPath = "web/package.json", PreferredPort = FreePort() });
        await fixture.Service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => fixture.Job(job).Status == "failed");
            Assert.Empty(fixture.Job(job).Runs);
            Assert.Contains("not selected", fixture.Job(job).Message);
            Assert.True(File.Exists(Path.Combine(fixture.Root, job.ManifestPath.Replace(".json", ".result.json"))));
            Assert.Throws<KeyNotFoundException>(() => fixture.Service.Stop(8, job.RequestId));
        }
        finally { await fixture.Service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task StopDuringReadinessRetainsProcessRecordAndReleasesPort()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "web", "server.cjs"), "require('node:http').createServer((q,r)=>{r.statusCode=503;r.end('not-ready')}).listen(Number(process.env.PORT),process.env.HOST)");
        var port = FreePort();
        var job = fixture.Prepare();
        fixture.Manifest(job, new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", PreferredPort = port });
        await fixture.Service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => fixture.Job(job).Runs.Count == 1 && Listening(port));
            Assert.Equal("starting", fixture.Job(job).Status);
            Assert.True(fixture.Job(job).Runs[0].ProcessId > 0);
            fixture.Service.Stop(7, job.RequestId);
            await Until(() => !Listening(port));
            Assert.Equal("stopped", fixture.Job(job).Status);
            Assert.Single(fixture.Job(job).Runs);
        }
        finally { await fixture.Service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task FailedSiblingRollsBackAndCancelledRequestCannotBeReplayed()
    {
        using var fixture = new Fixture();
        var job = fixture.Prepare();
        var port = FreePort();
        fixture.Manifest(job,
            new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", Role = "backend", PreferredPort = port },
            new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", Role = "frontend", PreferredPort = port });
        await fixture.Service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => fixture.Job(job).Status == "failed");
            Assert.Single(fixture.Job(job).Runs);
            await Until(() => !Listening(port));
            var cancelled = fixture.Prepare();
            fixture.Service.Stop(7, cancelled.RequestId);
            fixture.Manifest(cancelled, new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", PreferredPort = port });
            await Task.Delay(3500);
            Assert.Equal("stopped", fixture.Job(cancelled).Status);
            Assert.Empty(fixture.Job(cancelled).Runs);
            Assert.False(Listening(port));
        }
        finally { await fixture.Service.StopAsync(CancellationToken.None); }
    }

    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAfterLauncherExitsStillKillsListeningChild(bool shutdown)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var web = Path.Combine(fixture.Root, "web");
        File.WriteAllText(Path.Combine(web, "launcher.cjs"), """
            const fs = require('node:fs');
            const child = require('node:child_process').spawn(process.execPath, ['server.cjs'], { detached: true, stdio: 'ignore' });
            fs.writeFileSync('child.pid', String(child.pid)); child.unref();
            const timer = setInterval(() => { if (fs.existsSync('exit-launcher')) { clearInterval(timer); process.exit(0); } }, 50);
            """);
        File.WriteAllText(Path.Combine(web, "package.json"), """{"scripts":{"dev":"node launcher.cjs"}}""");
        var port = FreePort();
        var run = await fixture.Runtime.StartTargetAsync(7, new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", PreferredPort = port }, CancellationToken.None);
        try
        {
            await Until(() => Listening(port));
            using var launcher = System.Diagnostics.Process.GetProcessById(run.ProcessId!.Value);
            File.WriteAllText(Path.Combine(web, "exit-launcher"), "exit");
            await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (shutdown) fixture.Runtime.Dispose();
            else Assert.True(fixture.Runtime.Stop(7, run.RunId));
            await Until(() => !Listening(port));
            if (!shutdown) Assert.Equal("stopped", fixture.Runtime.FindRun(run.RunId)!.Status);
        }
        finally
        {
            // Only clean up the child created by this isolated fixture on the old implementation.
            var pidFile = Path.Combine(web, "child.pid");
            if (File.Exists(pidFile))
            {
                try { using var child = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))); child.Kill(true); child.WaitForExit(5000); }
                catch (ArgumentException) { }
            }
        }
    }

    [Fact]
    public async Task PortReusedByUnrelatedListenerIsNotKilledOrReportedStopped()
    {
        using var fixture = new Fixture();
        var port = FreePort();
        var run = await fixture.Runtime.StartTargetAsync(7, new ChatRuntimeTarget { RepositoryName = "fixture", EntryPath = "web/package.json", PreferredPort = port }, CancellationToken.None);
        await Until(() => Listening(port));
        Assert.True(fixture.Runtime.Stop(7, run.RunId));
        using var unrelated = new TcpListener(IPAddress.Loopback, port);
        unrelated.Start();
        Assert.False(fixture.Runtime.Stop(7, run.RunId));
        Assert.Equal("stopping", fixture.Runtime.FindRun(run.RunId)!.Status);
        Assert.True(Listening(port));
        unrelated.Stop();
        Assert.True(fixture.Runtime.Stop(7, run.RunId));
        Assert.Equal("stopped", fixture.Runtime.FindRun(run.RunId)!.Status);
    }

    [Fact]
    public void GroupStopDoesNotClaimSuccessWhenRunRemainsActive()
    {
        var runtime = DispatchProxy.Create<ICodeRuntimeManager, IncompleteStopProxy>();
        using var fixture = new Fixture(runtime);
        var job = fixture.Prepare();
        var jobs = (IEnumerable)typeof(ChatRuntimeService).GetField("_jobs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Service)!;
        foreach (var pair in jobs)
        {
            var value = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
            ((List<string>)value.GetType().GetProperty("Runs")!.GetValue(value)!).Add("owned-run");
        }
        fixture.Service.Stop(7, job.RequestId);
        Assert.Equal("failed", fixture.Job(job).Status);
        Assert.Contains("关闭未完成", fixture.Job(job).Message);
        Assert.Equal("stopping", Assert.Single(fixture.Job(job).Runs).Status);
    }

    public class IncompleteStopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "Stop" => false,
            "FindRun" => new CodeRuntimeRunDto { RunId = "owned-run", ProjectId = 7, Status = "stopping" },
            _ => throw new NotSupportedException()
        };
    }

    private static bool Listening(int port) => System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);
    private static async Task Until(Func<bool> condition, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "Runtime condition timed out."); await Task.Delay(100); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "aiagent-runtime-test-" + Guid.NewGuid().ToString("N"));
        public CodeRuntimeManager Runtime { get; }
        public ChatRuntimeService Service { get; }
        public Fixture(ICodeRuntimeManager? serviceRuntime = null)
        {
            Directory.CreateDirectory(Path.Combine(Root, "web", "node_modules"));
            Directory.CreateDirectory(Path.Combine(Root, "api"));
            File.WriteAllText(Path.Combine(Root, "web", "package.json"), """{"scripts":{"dev":"node server.cjs"}}""");
            File.WriteAllText(Path.Combine(Root, "web", "server.cjs"), """
                async function start() {
                  if(process.env.API_BASE_URL) { const r=await fetch(process.env.API_BASE_URL); if(!r.ok) process.exit(12); }
                  require('node:http').createServer((q,r)=>r.end('frontend-ready')).listen(Number(process.env.PORT),process.env.HOST);
                } start().catch(()=>process.exit(13));
                """);
            File.WriteAllText(Path.Combine(Root, "api", "Fixture.csproj"), """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>""");
            File.WriteAllText(Path.Combine(Root, "api", "Program.cs"), """var app=WebApplication.CreateBuilder(args).Build(); app.MapGet("/health",()=>"ready"); app.Run();""");
            var repositories = DispatchProxy.Create<ICodeRepositoryManager, RepositoryProxy>();
            ((RepositoryProxy)(object)repositories).Repository = new CodeRepositoryDto { Id = 1, ProjectId = 7, Name = "fixture", RootPath = Root };
            Runtime = new CodeRuntimeManager(null!, repositories, NullLogger<CodeRuntimeManager>.Instance);
            Service = new ChatRuntimeService(repositories, serviceRuntime ?? Runtime, NullLogger<ChatRuntimeService>.Instance);
        }
        public ChatRuntimeJobDto Prepare() => Service.Prepare(7, new() { IdleMinutes = 5, Selections = [new() { RepositoryName = "fixture" }] });
        public ChatRuntimeJobDto Job(ChatRuntimeJobDto job) => Service.List(7).Single(x => x.RequestId == job.RequestId);
        public void Manifest(ChatRuntimeJobDto job, params ChatRuntimeTarget[] targets)
        {
            var path = Path.Combine(Root, job.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new ChatRuntimeManifest { Targets = targets.ToList() }));
        }
        public void AgeVisit(ChatRuntimeJobDto dto)
        {
            var jobs = (IEnumerable)typeof(ChatRuntimeService).GetField("_jobs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Service)!;
            foreach (var pair in jobs)
            {
                var job = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
                if ((string)job.GetType().GetProperty("Id")!.GetValue(job)! == dto.RequestId)
                    job.GetType().GetProperty("LastVisit")!.SetValue(job, DateTime.UtcNow.AddMinutes(-6));
            }
        }
        public void Dispose()
        {
            Service.Dispose(); Runtime.Dispose();
            // This fixture owns exactly this generated temporary directory.
            Directory.Delete(Root, true);
        }
    }

    public class RepositoryProxy : DispatchProxy
    {
        public CodeRepositoryDto Repository { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "Get" when (string)args![0]! == Repository.Name => Repository,
            "List" => new List<CodeRepositoryDto> { Repository },
            _ => throw new KeyNotFoundException("Repository was not found.")
        };
    }
}
