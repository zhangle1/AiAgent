using System.IO;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using Microsoft.Extensions.Configuration;

namespace AiAgent.Backend.Tests;

public sealed class AgentProviderRegistryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothChatEntrypointsUseTheDefaultInstance(bool streaming)
    {
        var configuration = new ConfigurationBuilder().Build();
        using var source = new CancellationTokenSource();
        var request = new ChatCompleteRequest { Agent = " CoDeX ", Message = "fixture", RuntimeUserId = "owner", SessionId = "session" };
        var response = new ChatCompleteResponse();
        var events = new List<string>();
        AgentStreamEventHandler receive = (item, _) => { events.Add(item.Content); return Task.CompletedTask; };
        var codex = new FakeCodex(async (actual, sink, token) =>
        {
            Assert.Same(request, actual);
            Assert.Equal(source.Token, token);
            Assert.Same(streaming ? receive : null, sink);
            token.ThrowIfCancellationRequested();
            if (sink != null) await sink(new AgentStreamEvent { Content = "event" }, token);
            return response;
        });
        var registry = new AgentProviderRegistry([new CodexAgentProviderDriver(configuration, () => codex)]);
        Assert.Same(registry.ResolveDefault("codex"), registry.ResolveDefault("CODEX"));
        var chat = new ChatOrchestrator(null!, registry, null!, null!, null!, configuration);
        Task<ChatCompleteResponse> Run() => streaming ? chat.CompleteStreamingAsync(request, receive, source.Token) : chat.CompleteAsync(request, source.Token);
        Assert.Same(response, await Run());
        Assert.Equal(streaming ? 1 : 0, events.Count);
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        request.Agent = "unknown-instance";
        await Assert.ThrowsAsync<InvalidOperationException>(Run);
    }

    [Theory]
    [InlineData("installed", true)]
    [InlineData("failed", false)]
    [InlineData("missing", false)]
    public async Task SnapshotUsesOnlyConfiguredLauncherWithoutOpeningChat(string mode, bool installed)
    {
        var root = Directory.CreateTempSubdirectory("aiagent-driver-");
        try
        {
            var windows = OperatingSystem.IsWindows();
            var command = Path.Combine(root.FullName, windows ? "probe.cmd" : "probe.sh");
            if (mode != "missing")
            {
                var exit = mode == "failed" ? 1 : 0;
                var script = windows ? $"@echo off\r\nif not \"%~1\"==\"--version\" exit /b 2\r\necho codex-cli 1.2.3\r\nexit /b {exit}\r\n"
                    : $"#!/bin/sh\n[ \"$1\" = '--version' ] || exit 2\necho 'codex-cli 1.2.3'\nexit {exit}\n";
                File.WriteAllText(command, script);
                if (!windows) File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Codex:Command"] = command, ["Dsh:Command"] = command, ["CodeBuddy:Command"] = command }).Build();
            var registry = new AgentProviderRegistry([new CodexAgentProviderDriver(config, () => throw new InvalidOperationException("Unexpected chat execution"))]);
            var snapshot = await registry.GetCodexSnapshotAsync(default);
            if (installed)
            {
                var environments = await new AgentProviderEnvironmentService(config, registry).GetEnvironmentsAsync(default);
                Assert.Equal(new[] { "codex", "deepseek-harness", "codebuddy" }, environments.Select(item => item.Id));
                Assert.Equal(snapshot.Version, environments[0].Version);
                Assert.True(environments[0].ChatSupported);
                Assert.False(environments[2].ChatSupported);
            }
            Assert.Equal("codex", snapshot.Id);
            Assert.Equal(installed, snapshot.ChatSupported);
            Assert.Equal(installed, snapshot.Installed);
            Assert.Equal(installed ? "1.2.3" : null, snapshot.Version);
            Assert.DoesNotContain(root.FullName, snapshot.Command + snapshot.Message);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task CreationFailureProducesPublicStatusAndCannotFallThroughToAnotherAgent()
    {
        var driver = new BrokenDriver();
        var registry = new AgentProviderRegistry([driver]);
        var snapshot = await registry.GetCodexSnapshotAsync(default);
        Assert.False(snapshot.ChatSupported);
        Assert.DoesNotContain("private-fixture", snapshot.Message);
        var chat = new ChatOrchestrator(null!, registry, null!, null!, null!, new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => chat.CompleteAsync(new() { Agent = "codex" }, default));
        Assert.DoesNotContain("private-fixture", error.Message);
        driver.Repaired = true;
        var instances = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => registry.ResolveDefault("codex"))));
        Assert.All(instances, item => Assert.Same(instances[0], item));
        Assert.Equal(3, driver.Attempts);
    }

    private sealed class BrokenDriver : IAgentProviderDriver
    {
        public string Kind => "codex";
        public bool Repaired { get; set; }
        public int Attempts { get; private set; }
        public IAgentProviderInstance Create(string instanceId)
        {
            Attempts++;
            if (!Repaired) throw new InvalidOperationException("private-fixture");
            return new CodexAgentProviderDriver(new ConfigurationBuilder().Build(), () => throw new NotSupportedException()).Create(instanceId);
        }
    }

    private sealed class FakeCodex(Func<ChatCompleteRequest, AgentStreamEventHandler?, CancellationToken, Task<ChatCompleteResponse>> execute) : ICodexChatService
    {
        public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken token) => execute(request, onEvent, token);
        public Task<CodexQuotaResponse> ReadQuotaAsync(CancellationToken token) => throw new NotSupportedException();
        public Task HeartbeatAsync(AuthenticatedUser user, CodexRuntimeHeartbeatRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
