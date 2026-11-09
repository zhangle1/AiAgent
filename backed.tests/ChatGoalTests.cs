using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class ChatGoalTests
{
    [Theory]
    [InlineData("blocked")]
    [InlineData("budgetLimited")]
    [InlineData("usageLimited")]
    public async Task NativeStopStatesNeverBecomeGoalCompleteOrStartExtraTurns(string status)
    {
        using var fixture = new Fixture(status);
        using var service = fixture.Service();
        await service.CompleteAsync(fixture.Request(new() { Objective = "outcome" }), null, default);
        Assert.Equal(status, fixture.Goal().Status);
        Assert.Single(fixture.Calls(), x => x.GetProperty("method").GetString() == "turn/start");
    }

    [Fact]
    public async Task OrdinaryChatKeepsEphemeralThreadAndNeverCallsGoalMethods()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service();
        var result = await service.CompleteAsync(fixture.Request(), null, default);
        Assert.Equal("first", result.Answer);
        Assert.Null(service.GetGoal("owner", "session"));
        var calls = fixture.Calls();
        Assert.DoesNotContain(calls, x => x.GetProperty("method").GetString()!.StartsWith("thread/goal/"));
        Assert.True(calls.Single(x => x.GetProperty("method").GetString() == "thread/start").GetProperty("params").GetProperty("ephemeral").GetBoolean());
    }

    [Fact]
    public async Task NativeContinuationWaitsForFinalTurnWithoutStartingDuplicateTurns()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service();
        var request = fixture.Request(new() { Objective = "verified outcome", TokenBudget = 1000 });
        var result = await service.CompleteAsync(request, null, default);
        Assert.Contains("first", result.Answer);
        Assert.Contains("second", result.Answer);
        Assert.Contains("tail", result.Answer); // Goal-complete can arrive before turn-completed.
        var goal = fixture.Goal();
        Assert.Equal("complete", goal.Status);
        Assert.Equal(23, goal.TokensUsed);
        Assert.Single(fixture.Calls(), x => x.GetProperty("method").GetString() == "turn/start");
        Assert.False(fixture.Calls().Single(x => x.GetProperty("method").GetString() == "thread/start").GetProperty("params").GetProperty("ephemeral").GetBoolean());
        Assert.Throws<UnauthorizedAccessException>(() => service.GetGoal("other", "session"));
        Assert.DoesNotContain("threadId", JsonSerializer.Serialize(service.GetGoal("owner", "session")));
        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(fixture.Request(new() { Objective = "verified outcome", ExpectedVersion = 0 }), null, default));
        Assert.Single(fixture.Calls(), x => x.GetProperty("method").GetString() == "turn/start");
    }

    [Fact]
    public async Task PauseSavesPartialOutputAndResumeReusesThreadAndUsage()
    {
        using var fixture = new Fixture("hang");
        using var service = fixture.Service();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = service.CompleteAsync(fixture.Request(new() { Objective = "outcome" }), (ev, ct) => {
            if (ev.Type == "content") observed.TrySetResult();
            return Task.CompletedTask;
        }, default);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Throws<InvalidOperationException>(() => service.ControlGoal("owner", "session", "pause", -1));
        service.ControlGoal("owner", "session", "pause", fixture.Goal().Version);
        await Assert.ThrowsAnyAsync<Exception>(() => running);
        Assert.Equal("paused", fixture.Goal().Status);
        Assert.Equal("first", fixture.Db.Queryable<AiChatMessage>().Single().Content);
        Assert.Contains(fixture.Calls(), x => x.GetProperty("method").GetString() == "turn/interrupt");
        var paused = fixture.Goal();
        var changedPermissions = fixture.Request(new() { Action = "resume", ExpectedVersion = paused.Version });
        changedPermissions.CodexSandboxMode = "read-only";
        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(changedPermissions, null, default));
        Assert.Equal(paused.Version, fixture.Goal().Version);
        fixture.Mode("complete");
        await service.CompleteAsync(fixture.Request(new() { Action = "resume", ExpectedVersion = paused.Version }), null, default);
        var calls = fixture.Calls();
        Assert.Single(calls, x => x.GetProperty("method").GetString() == "thread/start");
        Assert.Single(calls, x => x.GetProperty("method").GetString() == "thread/resume");
        var resume = calls.Last(x => x.GetProperty("method").GetString() == "thread/goal/set").GetProperty("params");
        Assert.False(resume.TryGetProperty("objective", out _));
        Assert.False(resume.TryGetProperty("tokenBudget", out _));
    }

    [Fact]
    public async Task UnsupportedCliDoesNotFallBackAndStaleResumeCannotRun()
    {
        using var fixture = new Fixture("unsupported");
        using var service = fixture.Service();
        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(fixture.Request(new() { Objective = "outcome" }), null, default));
        Assert.DoesNotContain(fixture.Calls(), x => x.GetProperty("method").GetString() == "turn/start");
        Assert.Equal("paused", fixture.Goal().Status);
        var count = fixture.Calls().Length;
        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(fixture.Request(new() { Action = "resume", ExpectedVersion = -1 }), null, default));
        Assert.Equal(count, fixture.Calls().Length);
        service.ControlGoal("owner", "session", "end", fixture.Goal().Version);
        Assert.Equal("ended", fixture.Goal().Status);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "aiagent-goal-" + Guid.NewGuid().ToString("N"));
        public SqlSugarScope Db { get; }
        private string Command { get; }
        public Fixture(string mode = "complete")
        {
            Directory.CreateDirectory(Root);
            Db = new(new ConnectionConfig { DbType = DbType.Sqlite, ConnectionString = $"Data Source={Path.Combine(Root, "test.db")};Pooling=False", IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new() { EntityService = (_, column) => {
                    if (column.DataType == "nvarchar(max)") column.DataType = "text";
                    if (column.IsIdentity) column.DataType = "INTEGER";
                } } });
            Db.CodeFirst.InitTables<AiChatSession, AiChatMessage>();
            Db.Insertable(new AiChatSession { Id = "session", UserId = "owner" }).ExecuteCommand();
            Mode(mode);
            File.WriteAllText(Path.Combine(Root, "server.cjs"), Server);
            Command = Path.Combine(Root, OperatingSystem.IsWindows() ? "server.cmd" : "server.sh");
            File.WriteAllText(Command, OperatingSystem.IsWindows() ? "@echo off\r\nnode \"%~dp0server.cjs\"\r\n" : "#!/bin/sh\nexec node \"$(dirname \"$0\")/server.cjs\"\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public void Mode(string mode) => File.WriteAllText(Path.Combine(Root, "mode"), mode);
        public ChatCompleteRequest Request(ChatGoalRequest? goal = null) => new() { Goal = goal, Agent = "codex", Message = "work", RuntimeUserId = "owner", SessionId = "session", ClientRuntimeId = "fixture", MaintenanceWorkspacePath = Root };
        public CodexChatService Service() => new(Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Codex:Command"] = Command }).Build(), new Policy(), new NoOcr());
        public ChatGoalState Goal() => JsonSerializer.Deserialize<ChatGoalState>(Db.Queryable<AiChatSession>().Single().GoalJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        public JsonElement[] Calls() => File.ReadAllLines(Path.Combine(Root, "calls.jsonl")).Where(x => x.Length > 0).Select(x => JsonDocument.Parse(x).RootElement.Clone()).ToArray();
        public void Dispose()
        {
            Db.Dispose();
            // Windows process-tree termination may finish just after the lease is returned.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(Root, true); return; }
                catch (IOException) when (attempt < 10) { Thread.Sleep(50); }
            }
        }
    }

    private sealed class Policy : ICodexModelPolicyService
    {
        public CodexResolvedModel ResolveModel(string? id, string? effort) => new(new("fixture", "Fixture", "", "fixture", null, false, true), null);
        public CodexModelPolicyDto GetPolicy() => throw new NotSupportedException();
        public CodexModelPolicyDto UpdatePolicy(AuthenticatedUser user, CodexModelPolicyUpdateRequest request) => throw new NotSupportedException();
    }
    private sealed class NoOcr : IImageOcrService
    {
        public Task<List<ChatImageOcrResult>> ExtractAsync(IEnumerable<string> paths, AgentStreamEventHandler? onEvent, CancellationToken ct) => throw new NotSupportedException();
        public Task<ImageOcrDiagnosticDto> DiagnoseAsync(string? path, CancellationToken ct) => throw new NotSupportedException();
    }

    private const string Server = """
const fs = require('fs'), path = require('path');
const dir = __dirname;
let goal = null, turn = 0, scheduled = false;
const mode = () => fs.readFileSync(path.join(dir, 'mode'), 'utf8');
const send = x => process.stdout.write(JSON.stringify(x) + '\n');
const event = (method, params) => send({method, params});
const started = id => event('turn/started', {turn:{id}});
const done = () => event('turn/completed', {turn:{status:'completed'}});
const content = delta => event('item/agentMessage/delta', {delta});
require('readline').createInterface({input:process.stdin}).on('line', line => {
 const r = JSON.parse(line), p = r.params || {};
 fs.appendFileSync(path.join(dir, 'calls.jsonl'), JSON.stringify(r) + '\n');
 const result = value => send({id:r.id, result:value});
 switch(r.method) {
 case 'initialize': result({}); break;
 case 'thread/start': case 'thread/resume': result({thread:{id:'persistent-thread'}}); break;
 case 'thread/goal/set':
   if(mode()==='unsupported') {send({id:r.id,error:{message:'Method not found'}}); break;}
   goal = {objective:p.objective || 'outcome',status:p.status,tokensUsed:11,timeUsedSeconds:1}; result({goal}); break;
 case 'turn/start':
   turn++; result({turn:{id:'t1'}}); started('t1'); content('first');
   if(mode()!=='hang') done();
   break;
 case 'thread/goal/get':
   result({goal});
   if(goal.status==='active' && !scheduled) {
     scheduled=true;
     setTimeout(() => { started('t2'); content('second'); goal.status=['blocked','budgetLimited','usageLimited'].includes(mode())?mode():'complete'; goal.tokensUsed=23; event('thread/goal/updated',{goal}); content('tail'); done(); },20);
   }
   break;
 case 'turn/interrupt': result({}); event('turn/completed',{turn:{status:'interrupted'}}); break;
 }
});
""";
}
