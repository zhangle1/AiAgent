using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Services.Chat;
using Microsoft.Extensions.Configuration;

namespace AiAgent.Backend.Tests;

public sealed class CodexQuotaTests
{
    [Fact]
    public async Task ConcurrentReadsShareThirtyMinuteCacheAndNeverStartTurns()
    {
        using var fixture = new Fixture("chatgpt");
        using var service = fixture.Service();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.ReadQuotaAsync(default)));
        Assert.All(results, item => { Assert.Equal("available", item.Status); Assert.Equal(42, item.Primary!.UsedPercent); Assert.Equal(30, (item.NextRefreshAt - item.UpdatedAt).TotalMinutes); });
        Assert.Equal(new[] { "initialize", "initialized", "account/read", "account/rateLimits/read" }, fixture.Calls());
        Assert.DoesNotContain("private@example.invalid", JsonSerializer.Serialize(results));
        // Expiry must query again, rather than perpetually serving the first result.
        typeof(CodexChatService).GetField("_quotaCache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, results[0] with { NextRefreshAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
        await service.ReadQuotaAsync(default);
        Assert.Equal(8, fixture.Calls().Length);
    }

    [Theory]
    [InlineData("none", "not_logged_in")]
    [InlineData("apiKey", "unsupported")]
    [InlineData("error", "unavailable")]
    public async Task MissingAndUnsupportedAccountsAreExplicitAndErrorsAreSanitized(string mode, string expected)
    {
        using var fixture = new Fixture(mode);
        using var service = fixture.Service();
        var result = await service.ReadQuotaAsync(default);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Primary);
        Assert.DoesNotContain("FAKE_SECRET", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("account/rateLimits/read", fixture.Calls());
    }

    [Fact]
    public async Task CancellationDoesNotCacheFailure()
    {
        using var fixture = new Fixture("hang");
        using var service = fixture.Service();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadQuotaAsync(cancel.Token));
        Assert.Null(typeof(CodexChatService).GetField("_quotaCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "aiagent-quota-" + Guid.NewGuid().ToString("N"));
        private readonly string command;
        public Fixture(string mode)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "mode"), mode);
            File.WriteAllText(Path.Combine(root, "server.cjs"), Server);
            command = Path.Combine(root, OperatingSystem.IsWindows() ? "server.cmd" : "server.sh");
            File.WriteAllText(command, OperatingSystem.IsWindows() ? "@echo off\r\nnode \"%~dp0server.cjs\"\r\n" : "#!/bin/sh\nexec node \"$(dirname \"$0\")/server.cjs\"\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public CodexChatService Service() => new(null!, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Codex:Command"] = command }).Build(), null!, null!);
        public string[] Calls() => File.Exists(Path.Combine(root, "calls")) ? File.ReadAllLines(Path.Combine(root, "calls")) : [];
        public void Dispose() => Directory.Delete(root, true);
        private const string Server = """
const fs = require('fs'), path = require('path'), readline = require('readline');
const mode = fs.readFileSync(path.join(__dirname, 'mode'), 'utf8');
readline.createInterface({input:process.stdin}).on('line', line => {
 const req = JSON.parse(line); fs.appendFileSync(path.join(__dirname, 'calls'), req.method + '\n');
 if (req.id == null || mode === 'hang') return;
 let result = {};
 if (req.method === 'account/read') {
   if (mode === 'error') { console.log(JSON.stringify({id:req.id,error:{message:'FAKE_SECRET'}})); return; }
   result = {account: mode === 'none' ? null : {type:mode,email:'private@example.invalid'}};
 }
 if (req.method === 'account/rateLimits/read') result = {rateLimits:{planType:'plus',primary:{usedPercent:42,windowDurationMins:300,resetsAt:1800000000},secondary:null}};
 console.log(JSON.stringify({id:req.id,result}));
});
""";
    }
}
