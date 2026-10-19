using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class CodexDiagnosticsTests
{
    private const string Secret = "sk-FAKE_CREDENTIAL_DO_NOT_LOG";
    private const string Question = "PRIVATE_QUESTION_DO_NOT_LOG";
    private static readonly AuthenticatedUser Owner = new("fixture", "fixture");

    [Fact]
    public async Task MissingExecutableIncludesStageIdAndNativeErrorWithoutRawMessages()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service(Path.Combine(fixture.Root, "missing.exe"));
        var failure = await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(fixture.Request(), fixture.OnEvent, default));
        Assert.Equal("codex_process_start", failure.Stage);
        Assert.Equal("codex_cli_missing", failure.Code);
        Assert.Contains("诊断编号", failure.Message);
        Assert.Equal(32, failure.DiagnosticId.Length);
        var last = fixture.Events.Last().DebugTrace!;
        Assert.Equal("failed", last.Status);
        Assert.Equal(failure.DiagnosticId, last.DiagnosticId);
        Assert.True(last.Details!.ContainsKey("native_error"));
        fixture.AssertPrivateDataNotLogged();
    }

    [Fact]
    public async Task BadSessionJsonIsLocatedBeforeCliStartup()
    {
        using var fixture = new Fixture();
        fixture.Db.Insertable(new AiChatSession { Id = "fixture", UserId = "fixture", PreferencesJson = "{broken" }).ExecuteCommand();
        using var service = fixture.Service("missing.exe");
        var failure = await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(fixture.Request(), fixture.OnEvent, default));
        Assert.Equal("codex_session_resume", failure.Stage);
        Assert.Equal("codex_session_metadata", failure.Code);
        Assert.DoesNotContain(fixture.Events, item => item.DebugTrace?.Stage == "codex_process_start");
    }

    [Fact]
    public async Task MetadataWarningsAndSensitiveStderrDoNotTurnSuccessfulReplyIntoFailure()
    {
        using var fixture = new Fixture();
        var command = fixture.Cli([
            new { type = "thread.started", thread_id = "fixture-thread" },
            new { type = "item.completed", item = new { type = "error", message = $"Model metadata for `fixture` not found. Defaulting to fallback metadata; {Secret} {Question}" } },
            new { type = "item.completed", item = new { type = "agent_message", text = "OK" } },
            new { type = "turn.completed" }
        ], $"Authorization: Bearer {Secret}\nuser content: {Question}", 0);
        using var service = fixture.Service(command);
        var response = await service.CompleteAsync(fixture.Request(), fixture.OnEvent, default);
        Assert.Equal("OK", response.Answer);
        var warning = Assert.Single(fixture.Events, item => item.DebugTrace?.Status == "warning");
        Assert.Equal("codex_model_metadata", warning.DebugTrace!.ErrorCode);
        Assert.Contains("不代表请求失败", warning.DebugTrace.Message);
        Assert.DoesNotContain(fixture.Events, item => item.DebugTrace?.Status == "failed");
        Assert.Contains(fixture.Events, item => item.DebugTrace?.Stage == "codex_cli_exit" && item.DebugTrace.Status == "completed" && item.DebugTrace.Details!["exit_code"] == "0");
        fixture.AssertPrivateDataNotLogged();
    }

    [Fact]
    public async Task NonzeroCliExitGetsClassifiedAndPersistableDiagnostics()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service(fixture.Cli([], $"HTTP 401 Unauthorized\nAuthorization: Bearer {Secret}\n{Question}", 3));
        var request = fixture.Request();
        var trace = ChatDebugTrace.Create(request)!;
        var failure = await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(request, (ev, ct) => {
            fixture.Events.Add(ev); trace.RecordProviderDiagnostic(ev.DebugTrace); return Task.CompletedTask;
        }, default));
        Assert.Equal("codex_cli_exit", failure.Stage);
        Assert.Equal("codex_authentication", failure.Code);
        Assert.Equal("3", trace.Events.Last().Details!["exit_code"]);
        Assert.Contains(fixture.Logger.Lines, line => line.Contains(failure.DiagnosticId));
        Assert.Null(failure.InnerException);
        fixture.AssertPrivateDataNotLogged();
        var store = new ChatDebugTraceStore(fixture.Db, new ConfigurationBuilder().Build(), fixture.Logger);
        fixture.Db.Insertable(new AiChatSession { Id = "fixture", UserId = Owner.Id }).ExecuteCommand();
        await store.SaveAsync(Owner, "fixture", trace, default);
        var saved = Assert.Single(await store.ListAsync(Owner, "fixture", default));
        Assert.Equal(failure.DiagnosticId, saved.Events.Last().DiagnosticId);
        Assert.Empty(await store.ListAsync(new("other", "other"), "fixture", default));
    }

    [Fact]
    public async Task FailedTurnWithZeroExitDoesNotReportSuccess()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service(fixture.Cli([new { type = "turn.failed", error = new { message = $"No stored rollout found with id; {Secret}" } }], "", 0));
        var failure = await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(fixture.Request(), fixture.OnEvent, default));
        Assert.Equal("codex_resume_missing", failure.Code);
        Assert.DoesNotContain(Secret, failure.Message);
    }

    [Fact]
    public async Task ExitWithoutTerminalEventIsReportedAsIncompleteProtocol()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service(fixture.Cli([new { type = "thread.started" }], "", 0));
        var failure = await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(fixture.Request(), fixture.OnEvent, default));
        Assert.Equal("codex_protocol_incomplete", failure.Code);
    }

    [Fact]
    public async Task UnknownCliErrorsCannotEchoSecretsOrPromptIntoEventsExceptionsOrLogs()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service(fixture.Cli([new { type = "turn.failed", error = new { message = $"Unknown {Secret} user content {Question}" } }], Question + Secret, 2));
        var failure = await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(fixture.Request(), fixture.OnEvent, default));
        Assert.Equal("codex_cli_failed", failure.Code);
        Assert.DoesNotContain(Secret, failure.ToString());
        Assert.DoesNotContain(Question, failure.ToString());
        fixture.AssertPrivateDataNotLogged();
    }

    [Fact]
    public async Task CancelledRequestKeepsCancellationSemantics()
    {
        using var fixture = new Fixture();
        using var service = fixture.Service("missing.exe");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteAsync(fixture.Request(), fixture.OnEvent, cancellation.Token));
        Assert.DoesNotContain(fixture.Events, item => item.DebugTrace?.Status == "failed");
    }

    [Theory]
    [InlineData("configuration profile deepseek not found", "codex_profile_missing")]
    [InlineData("unexpected argument --profile", "codex_cli_arguments")]
    [InlineData("Error loading config: TOML parse error", "codex_config_invalid")]
    [InlineData("HTTP 429 too many requests", "codex_rate_limit")]
    [InlineData("error sending request to endpoint", "codex_network")]
    [InlineData("maximum context length exceeded", "codex_context_limit")]
    public void CliClassificationUsesFixedMessages(string raw, string code)
    {
        Assert.Equal(code, CodexDiagnosticException.ClassifyCli(raw + " " + Secret));
        Assert.DoesNotContain(Secret, CodexDiagnosticException.Describe(code));
    }

    [Fact]
    public void DiagnosticDefaultsAndBothStreamClientMappingsAreSafe()
    {
        var request = JsonSerializer.Deserialize<ChatCompleteRequest>("{\"agent\":\"codex\",\"codex_model_id\":\"profile:fixture\",\"CodexDiagnosticId\":\"forged\"}")!;
        var trace = ChatDebugTrace.Create(request);
        Assert.NotNull(trace);
        Assert.Equal(32, request.CodexDiagnosticId!.Length);
        var failure = CodexDiagnosticException.ForRequest(new InvalidOperationException(Question + Secret), request);
        foreach (var type in new[] { typeof(ChatAppService), typeof(ChatWebSocketHandler) })
        {
            var message = (string)type.GetMethod("ToClientError", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [failure, request])!;
            Assert.Contains(failure.DiagnosticId, message);
            Assert.DoesNotContain(Secret, message);
            Assert.DoesNotContain(Question, message);
        }
        trace!.ReportCodexFailure(failure);
        trace.ReportCodexFailure(failure);
        Assert.Single(trace.Events);
        Assert.Equal("codex_exec", trace.Events[0].Transport);
        Assert.Contains("diagnostic_id", JsonSerializer.Serialize(trace.Events));
        Assert.Null(ChatDebugTrace.Create(new ChatCompleteRequest { Agent = "none" }));
    }

    [Fact]
    public async Task StderrReaderBoundsAndClassifiesWithoutReturningRawText()
    {
        var raw = new string('x', 100000) + Secret + Question + " HTTP 401 Unauthorized";
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(raw)));
        var summary = await CodexRequestDiagnostics.ReadStderrAsync(reader, default);
        Assert.Equal(raw.Length, summary.Characters);
        Assert.Equal("codex_authentication", summary.Code);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(summary));
        Assert.DoesNotContain(Question, JsonSerializer.Serialize(summary));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "aiagent-diagnostic-fixture-" + Guid.NewGuid().ToString("N"));
        public SqlSugarScope Db { get; }
        public CaptureLogger Logger { get; } = new();
        public List<AgentStreamEvent> Events { get; } = [];
        public AgentStreamEventHandler OnEvent => (ev, ct) => { Events.Add(ev); return Task.CompletedTask; };
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Db = new(new ConnectionConfig { DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new() { EntityService = (_, column) => {
                    if (column.DataType == "nvarchar(max)") column.DataType = "text";
                    if (column.IsIdentity) column.DataType = "INTEGER";
                } }
            });
            Db.CodeFirst.InitTables<AiChatSession, AiChatDebugTrace>();
        }
        public ChatCompleteRequest Request() => new() { Agent = "codex", Message = Question, RuntimeUserId = Owner.Id, SessionId = "fixture", MaintenanceWorkspacePath = Root };
        public CodexChatService Service(string command) => new(Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Codex:Command"] = command }).Build(), new Policy(), new NoOcr(), Logger);
        public string Cli(object[] events, string stderr, int exit)
        {
            File.WriteAllText(Path.Combine(Root, "events.jsonl"), string.Join("\n", events.Select(item => JsonSerializer.Serialize(item))) + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Root, "stderr.txt"), stderr, new UTF8Encoding(false));
            var script = Path.Combine(Root, OperatingSystem.IsWindows() ? "fixture.cmd" : "fixture.sh");
            var contents = OperatingSystem.IsWindows()
                ? $"@echo off\r\nmore >nul\r\ntype \"%~dp0events.jsonl\"\r\ntype \"%~dp0stderr.txt\" 1>&2\r\nexit /b {exit}\r\n"
                : $"#!/bin/sh\ncat >/dev/null\ncat \"$(dirname \"$0\")/events.jsonl\"\ncat \"$(dirname \"$0\")/stderr.txt\" >&2\nexit {exit}\n";
            File.WriteAllText(script, contents, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return script;
        }
        public void AssertPrivateDataNotLogged()
        {
            var logs = string.Join("\n", Logger.Lines);
            var stream = JsonSerializer.Serialize(Events.Where(item => item.Type == "debug_trace"));
            foreach (var value in new[] { Secret, Question }) { Assert.DoesNotContain(value, logs); Assert.DoesNotContain(value, stream); }
            Assert.DoesNotContain(Root.Replace("\\", "\\\\"), stream);
        }
        public void Dispose() { Db.Dispose(); Directory.Delete(Root, true); }
    }

    private sealed class Policy : ICodexModelPolicyService
    {
        public CodexResolvedModel ResolveModel(string? id, string? effort) => new(new("profile:fixture", "fixture", "", null, "fixture", false, false, SupportsImageInput: true), null);
        public CodexModelPolicyDto GetPolicy() => throw new NotSupportedException();
        public CodexModelPolicyDto UpdatePolicy(AuthenticatedUser user, CodexModelPolicyUpdateRequest request) => throw new NotSupportedException();
    }
    private sealed class NoOcr : IImageOcrService
    {
        public Task<List<ChatImageOcrResult>> ExtractAsync(IEnumerable<string> paths, AgentStreamEventHandler? onEvent, CancellationToken ct) => throw new InvalidOperationException("Native profiles must not call OCR.");
        public Task<ImageOcrDiagnosticDto> DiagnoseAsync(string? path, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class CaptureLogger : ILogger<CodexChatService>, ILogger<ChatDebugTraceStore>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
