using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Push;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Push;

public sealed record DingTalkBotInboundMessage(long ChannelId, string SourceMessageId, string ConversationId, string? SenderId, string? SenderName, string SessionWebhook, string Content);

public interface IDingTalkGroupAgentService
{
    Task EnqueueAsync(DingTalkBotInboundMessage message, CancellationToken cancellationToken);
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}

/// <summary>Runs a project-scoped chat turn for an acknowledged DingTalk robot mention.</summary>
public sealed class DingTalkGroupAgentService : IDingTalkGroupAgentService
{
    private const string GitPushSucceeded = "git_push_succeeded";
    private const int MaxConcurrentSessions = 4;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(20);
    private readonly ISqlSugarClient _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IChatOrchestrator _chat;
    private readonly ILogger<DingTalkGroupAgentService> _logger;
    private readonly ConcurrentDictionary<long, Task> _runningSessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _conversationGates = new(StringComparer.Ordinal);

    public DingTalkGroupAgentService(ISqlSugarClient db, IDataProtectionProvider dataProtectionProvider, IHttpClientFactory httpClientFactory, IChatOrchestrator chat, ILogger<DingTalkGroupAgentService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.DingTalk.GroupAgent.v1");
        _httpClientFactory = httpClientFactory;
        _chat = chat;
        _logger = logger;
    }

    public Task EnqueueAsync(DingTalkBotInboundMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSessionWebhook(message.SessionWebhook) || string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 8000) return Task.CompletedTask;
        if (_db.Queryable<AiDingTalkGroupAgentSession>().Any(item => item.SourceMessageId == message.SourceMessageId)) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        try
        {
            _db.Insertable(new AiDingTalkGroupAgentSession
            {
                SourceMessageId = Clean(message.SourceMessageId, 128),
                PushChannelId = message.ChannelId,
                ConversationId = Clean(message.ConversationId, 256),
                SenderId = Clean(message.SenderId, 128),
                SenderName = Clean(message.SenderName, 128),
                SessionWebhookProtected = _protector.Protect(message.SessionWebhook),
                Question = Clean(message.Content, 8000),
                Status = "pending",
                CreatedAt = now
            }).ExecuteCommand();
        }
        catch (Exception ex)
        {
            // The unique database index is the final duplicate-delivery guard.
            _logger.LogDebug(ex, "DingTalk robot message {MessageId} was already recorded or could not be queued.", message.SourceMessageId);
        }
        return Task.CompletedTask;
    }

    public Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        RecoverStaleSessions();
        var availableSlots = MaxConcurrentSessions - _runningSessions.Count;
        if (availableSlots <= 0) return Task.CompletedTask;
        var sessions = _db.Queryable<AiDingTalkGroupAgentSession>().Where(item => item.Status == "pending").OrderBy(item => item.CreatedAt).Take(availableSlots).ToList();
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processingStartedAt = DateTime.UtcNow;
            var claimed = _db.Updateable<AiDingTalkGroupAgentSession>().SetColumns(item => item.Status == "processing").SetColumns(item => item.ProcessingStartedAt == processingStartedAt).Where(item => item.Id == session.Id && item.Status == "pending").ExecuteCommand();
            if (claimed != 1) continue;
            var worker = Task.Run(() => ProcessClaimedSessionAsync(session, cancellationToken), CancellationToken.None);
            if (!_runningSessions.TryAdd(session.Id, worker)) continue;
            _ = worker.ContinueWith(_ => { _runningSessions.TryRemove(session.Id, out var ignored); }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        return Task.CompletedTask;
    }

    private void RecoverStaleSessions()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        var stale = _db.Queryable<AiDingTalkGroupAgentSession>()
            .Where(item => item.Status == "processing" && ((item.ProcessingStartedAt.HasValue && item.ProcessingStartedAt < cutoff) || (!item.ProcessingStartedAt.HasValue && item.CreatedAt < cutoff)))
            .OrderBy(item => item.CreatedAt)
            .Take(MaxConcurrentSessions)
            .ToList();
        foreach (var session in stale)
        {
            if (_runningSessions.ContainsKey(session.Id)) continue;
            var recovered = _db.Updateable<AiDingTalkGroupAgentSession>()
                .SetColumns(item => item.Status == "pending")
                .SetColumns(item => item.LastErrorCode == "worker_recovered")
                .SetColumns(item => item.ProcessingStartedAt == null)
                .Where(item => item.Id == session.Id && item.Status == "processing")
                .ExecuteCommand();
            if (recovered > 0 && session.PushChannelId.HasValue)
                WriteAudit(session.PushChannelId.Value, "group_agent_worker_recovered", "retry_wait", session.CodeProjectId, new { session_id = session.Id, error_code = "worker_recovered" });
        }
    }

    private async Task ProcessClaimedSessionAsync(AiDingTalkGroupAgentSession session, CancellationToken cancellationToken)
    {
        var conversationKey = $"{session.PushChannelId?.ToString() ?? "none"}:{session.ConversationId ?? session.Id.ToString()}";
        var gate = _conversationGates.GetOrAdd(conversationKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session.PushChannelId.HasValue) WriteAudit(session.PushChannelId.Value, "group_agent_dequeued", "success", session.CodeProjectId, new { session_id = session.Id, conversation_key = conversationKey });
            await ProcessOneAsync(session.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DingTalk group-agent session {SessionId} escaped its worker.", session.Id);
            if (session.PushChannelId.HasValue) WriteAudit(session.PushChannelId.Value, "group_agent_worker_failed", "failed", session.CodeProjectId, new { session_id = session.Id, error_code = "worker_failed" });
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ProcessOneAsync(long sessionId, CancellationToken cancellationToken)
    {
        var session = _db.Queryable<AiDingTalkGroupAgentSession>().First(item => item.Id == sessionId && item.Status == "processing");
        if (session == null || !session.PushChannelId.HasValue) return;
        var answer = string.Empty;
        var errorCode = (string?)null;
        string? sessionWebhook = null;
        CancellationTokenSource? progressCancellation = null;
        Task? progressTask = null;
        var progress = new GroupAgentProgress();
        try
        {
            sessionWebhook = _protector.Unprotect(session.SessionWebhookProtected ?? string.Empty);
            await ReportProgressAsync(sessionWebhook, progress, "accepted", "已收到，开始分析。正在识别项目并读取上下文…", cancellationToken);
            progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            progressTask = SendPeriodicProgressAsync(sessionWebhook, progress, progressCancellation.Token);
            var channel = _db.Queryable<AiPushChannel>().First(item => item.Id == session.PushChannelId.Value && item.IsDeleted != true && item.IsEnabled == true && item.StreamEnabled == true);
            if (channel == null) throw new GroupAgentException("channel_unavailable", "该机器人通道当前未启用。请联系管理员检查推送模块配置。");
            var project = ResolveProject(channel.Id, session.Question ?? string.Empty, out var scopeFailure);
            if (project == null) throw new GroupAgentException("project_not_allowed", scopeFailure!);
            session.CodeProjectId = project.Id;
            _db.Updateable(session).UpdateColumns(item => new { item.CodeProjectId }).ExecuteCommand();

            var repositories = _db.Queryable<AiCodeRepository>().Where(item => item.ProjectId == project.Id && !item.IsDeleted).Select(item => item.Name).ToList();
            var repositorySummary = repositories.Count == 0 ? "未登记代码仓库" : string.Join("、", repositories);
            await ReportProgressAsync(sessionWebhook, progress, "project_resolved", $"已定位项目「{project.DisplayName}」，本次使用仓库：{repositorySummary}。正在读取仓库概览…", cancellationToken);
            var prompt = $"{session.Question}\n\n[系统上下文：此请求来自钉钉群机器人，当前已自动定位项目“{project.DisplayName}”。机器人使用服务器内部固定管理员执行身份，不依赖浏览器 Cookie 或登录 token。当前通道已授权该项目；你可以分析并在需要时修改该项目已登记仓库中的代码。完成时请明确说明分析结果、实际修改的文件，以及未完成项。]";
            var request = new ChatCompleteRequest
            {
                Message = session.Question ?? string.Empty,
                ServerPromptMessage = prompt,
                CodeProjectId = project.Id,
                CodeRepositoryNames = repositories,
                RuntimeUserId = ResolveAdministratorRuntimeUserId(),
                SessionId = $"dingtalk:{session.ConversationId}:{project.Id}",
                Mode = "chat"
            };
            WriteAudit(channel.Id, "group_agent_started", "success", project.Id, new { session_id = session.Id, repository_count = repositories.Count, execution_identity = "system_administrator" });
            var result = await _chat.CompleteStreamingAsync(request, (streamEvent, token) => RelayAgentProgressAsync(sessionWebhook, progress, streamEvent, token), cancellationToken);
            answer = Truncate(result.Content, 3500);
        }
        catch (GroupAgentException ex)
        {
            errorCode = ex.Code;
            answer = ex.UserMessage;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DingTalk group-agent session {SessionId} failed.", sessionId);
            errorCode = "agent_failed";
            answer = "项目处理暂时失败，请稍后重试。系统已记录失败阶段。";
        }
        finally
        {
            if (progressCancellation != null) progressCancellation.Cancel();
            if (progressTask != null)
            {
                try { await progressTask; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
            progressCancellation?.Dispose();
        }

        var delivered = false;
        try
        {
            var webhook = sessionWebhook ?? _protector.Unprotect(session.SessionWebhookProtected ?? string.Empty);
            await SendSessionReplyAsync(webhook, answer, cancellationToken);
            delivered = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DingTalk group-agent reply for session {SessionId} could not be sent.", sessionId);
            errorCode ??= "reply_failed";
            if (session.PushChannelId.HasValue) WriteAudit(session.PushChannelId.Value, "group_agent_reply_failed", "failed", session.CodeProjectId, new { session_id = sessionId, error_code = errorCode });
        }

        _db.Updateable<AiDingTalkGroupAgentSession>()
            .SetColumns(item => item.Status == (delivered ? "completed" : "reply_failed"))
            .SetColumns(item => item.LastErrorCode == errorCode)
            .SetColumns(item => item.Answer == Truncate(answer, 8000))
            .SetColumns(item => item.ProcessingStartedAt == null)
            .SetColumns(item => item.CompletedAt == DateTime.UtcNow)
            .Where(item => item.Id == sessionId)
            .ExecuteCommand();
        if (session.PushChannelId.HasValue)
        {
            var outcome = delivered && errorCode == null ? "success" : errorCode is "project_not_allowed" or "channel_unavailable" ? "rejected" : "failed";
            WriteAudit(session.PushChannelId.Value, "group_agent_completed", outcome, session.CodeProjectId, new { session_id = sessionId, status = delivered ? "completed" : "reply_failed", error_code = errorCode });
        }
    }

    private AiCodeProject? ResolveProject(long channelId, string question, out string? failure)
    {
        var allowedIds = _db.Queryable<AiProjectPushBinding>().Where(item => item.PushChannelId == channelId && item.TriggerType == GitPushSucceeded && item.IsEnabled == true && item.IsDeleted != true && item.ProjectId.HasValue).Select(item => item.ProjectId!.Value).ToList();
        var allProjects = _db.Queryable<AiCodeProject>().Where(item => !item.IsDeleted).ToList();
        var named = allProjects.Where(project => !string.IsNullOrWhiteSpace(project.DisplayName) && question.Contains(project.DisplayName, StringComparison.OrdinalIgnoreCase)).OrderByDescending(project => project.DisplayName.Length).ToList();
        if (named.Count == 0)
        {
            var allowedNames = allProjects.Where(project => allowedIds.Contains(project.Id)).Select(project => project.DisplayName).ToList();
            failure = allowedNames.Count == 0 ? "该机器人尚未勾选任何项目，请由管理员在推送通道中配置项目范围。" : $"请在问题中明确项目名称。当前通道可用项目：{string.Join("、", allowedNames)}。";
            return null;
        }
        var matched = named.First();
        if (!allowedIds.Contains(matched.Id))
        {
            failure = $"项目“{matched.DisplayName}”未在该机器人通道中勾选，无法读取或分析该项目。请联系管理员在推送通道的项目范围中启用它。";
            return null;
        }
        failure = null;
        return matched;
    }

    private string ResolveAdministratorRuntimeUserId()
    {
        var administrator = _db.Queryable<AiUser>().Where(user => user.Role == "admin" && !user.IsDisabled).OrderBy(user => user.Id).Select(user => user.Id).First();
        return string.IsNullOrWhiteSpace(administrator) ? "dingtalk-system-administrator" : administrator;
    }

    private async Task SendPeriodicProgressAsync(string webhook, GroupAgentProgress progress, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ProgressInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var fallback = progress.GetFallbackMessage();
                if (fallback != null) await TrySendProgressAsync(webhook, fallback, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RelayAgentProgressAsync(string webhook, GroupAgentProgress progress, AgentStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        if (string.Equals(streamEvent.Type, "provider_request_started", StringComparison.OrdinalIgnoreCase))
        {
            await ReportProgressAsync(webhook, progress, "model_started", "项目上下文已准备，正在规划检索步骤…", cancellationToken);
            return;
        }

        if (string.Equals(streamEvent.Type, "thinking", StringComparison.OrdinalIgnoreCase))
        {
            await ReportProgressAsync(webhook, progress, "analysis", "正在基于已获取的项目资料分析问题并确定下一步…", cancellationToken);
            return;
        }

        if (string.Equals(streamEvent.Type, "tool", StringComparison.OrdinalIgnoreCase))
        {
            var tools = ReadToolNames(streamEvent);
            await ReportProgressAsync(webhook, progress, $"tool:{string.Join(",", tools)}", DescribeToolProgress(tools), cancellationToken);
            return;
        }

        if (string.Equals(streamEvent.Type, "tool_result", StringComparison.OrdinalIgnoreCase))
        {
            var iteration = streamEvent.Metadata.TryGetValue("iteration", out var value) ? value?.ToString() : null;
            await ReportProgressAsync(webhook, progress, $"tool_result:{iteration ?? "current"}", $"第 {iteration ?? "当前"} 轮检索已完成，正在核对代码与上下文…", cancellationToken);
            return;
        }

        if (string.Equals(streamEvent.Type, "label", StringComparison.OrdinalIgnoreCase) && string.Equals(streamEvent.Label, AgentLabels.Finish, StringComparison.OrdinalIgnoreCase))
            await ReportProgressAsync(webhook, progress, "finishing", "资料核对完成，正在整理最终回答…", cancellationToken);
    }

    private async Task ReportProgressAsync(string webhook, GroupAgentProgress progress, string key, string message, CancellationToken cancellationToken)
    {
        if (!progress.TryReport(key, message)) return;
        await TrySendProgressAsync(webhook, message, cancellationToken);
    }

    private static IReadOnlyList<string> ReadToolNames(AgentStreamEvent streamEvent)
    {
        if (!streamEvent.Metadata.TryGetValue("tools", out var value) || value == null) return ["analysis"];
        return value switch
        {
            string[] values => values,
            IEnumerable<string> values => values.ToArray(),
            _ => [value.ToString() ?? "analysis"]
        };
    }

    private static string DescribeToolProgress(IReadOnlyList<string> tools)
    {
        if (tools.Contains("code_repository_overview", StringComparer.OrdinalIgnoreCase)) return "正在读取仓库目录、README 和入口配置…";
        if (tools.Contains("find_symbol", StringComparer.OrdinalIgnoreCase)) return "正在定位相关实体、类或字段定义…";
        if (tools.Contains("code_search", StringComparer.OrdinalIgnoreCase)) return "正在搜索相关代码、表名和字段线索…";
        if (tools.Contains("read_dashboard_file", StringComparer.OrdinalIgnoreCase)) return "正在读取已定位的源文件…";
        return "正在执行项目内检索并收集依据…";
    }

    private async Task TrySendProgressAsync(string webhook, string content, CancellationToken cancellationToken)
    {
        try { await SendSessionReplyAsync(webhook, content, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogWarning(ex, "DingTalk group-agent progress reply could not be sent."); }
    }

    private void WriteAudit(long channelId, string action, string outcome, long? projectId, object metadata)
        => _db.Insertable(new AiPushAuditLog { CorrelationId = Guid.NewGuid().ToString("N"), ActorType = "system", ActorId = "dingtalk-system-administrator", Action = action, Outcome = outcome, ProjectId = projectId, PushChannelId = channelId, MetadataJson = JsonSerializer.Serialize(metadata), OccurredAt = DateTime.UtcNow }).ExecuteCommand();

    private async Task SendSessionReplyAsync(string rawWebhook, string content, CancellationToken cancellationToken)
    {
        if (!IsSessionWebhook(rawWebhook)) throw new InvalidOperationException("Invalid session webhook.");
        using var request = new HttpRequestMessage(HttpMethod.Post, rawWebhook) { Content = JsonContent.Create(new { msgtype = "text", text = new { content } }) };
        using var response = await _httpClientFactory.CreateClient("DingTalkSessionWebhook").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"DingTalk session reply failed with HTTP {(int)response.StatusCode}.");
    }

    private static bool IsSessionWebhook(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && string.Equals(uri.Host, "oapi.dingtalk.com", StringComparison.OrdinalIgnoreCase)
           && uri.AbsolutePath == "/robot/sendBySession"
           && !string.IsNullOrWhiteSpace(uri.Query)
           && string.IsNullOrEmpty(uri.Fragment)
           && string.IsNullOrEmpty(uri.UserInfo);

    private static string? Clean(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : Truncate(new string(value.Where(character => !char.IsControl(character) || character is '\r' or '\n').ToArray()).Trim(), length);
    private static string Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value) ? "未获得可用回答。" : value.Trim().Length <= length ? value.Trim() : value.Trim()[..length] + "\n（回复已截断）";
    private sealed class GroupAgentProgress
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        private string _latestMessage = "正在处理项目请求…";
        private DateTime _lastReportedAt = DateTime.UtcNow;

        public bool TryReport(string key, string message)
        {
            lock (_gate)
            {
                _latestMessage = message;
                if (!_reported.Add(key)) return false;
                _lastReportedAt = DateTime.UtcNow;
                return true;
            }
        }

        public string? GetFallbackMessage()
        {
            lock (_gate)
            {
                if (DateTime.UtcNow - _lastReportedAt < ProgressInterval) return null;
                _lastReportedAt = DateTime.UtcNow;
                return $"仍在处理：{_latestMessage}";
            }
        }
    }
    private sealed class GroupAgentException : Exception { public GroupAgentException(string code, string userMessage) => (Code, UserMessage) = (code, userMessage); public string Code { get; } public string UserMessage { get; } }
}

/// <summary>Owns outbound Stream WebSocket connections so no public callback endpoint is required.</summary>
public sealed class DingTalkStreamHostedService : BackgroundService
{
    private const string BotTopic = "/v1.0/im/bot/messages/get";
    private const int MaximumReconnectDelaySeconds = 300;
    private readonly ISqlSugarClient _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDingTalkGroupAgentService _agents;
    private readonly ILogger<DingTalkStreamHostedService> _logger;
    private readonly ConcurrentDictionary<long, StreamConnectionLease> _connections = new();

    public DingTalkStreamHostedService(ISqlSugarClient db, IDataProtectionProvider dataProtectionProvider, IHttpClientFactory httpClientFactory, IDingTalkGroupAgentService agents, ILogger<DingTalkStreamHostedService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.Push.DingTalkWebhook.v1");
        _httpClientFactory = httpClientFactory;
        _agents = agents;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _agents.ProcessPendingAsync(stoppingToken);
                StartConfiguredConnections(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "DingTalk Stream worker loop failed."); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private void StartConfiguredConnections(CancellationToken cancellationToken)
    {
        var channels = _db.Queryable<AiPushChannel>().Where(channel => channel.IsDeleted != true && channel.IsEnabled == true && channel.StreamEnabled == true).ToList()
            .Where(channel => !string.IsNullOrWhiteSpace(channel.StreamClientId) && !string.IsNullOrWhiteSpace(channel.StreamClientSecretProtected) && !string.IsNullOrWhiteSpace(channel.DingTalkRobotCode))
            .ToList();
        foreach (var channel in channels)
        {
            var lease = _connections.GetOrAdd(channel.Id, _ => new StreamConnectionLease());
            lock (lease.Gate)
            {
                if (lease.RunningTask is { IsCompleted: false } || lease.NextAttemptAtUtc > DateTime.UtcNow) continue;
                lease.RunningTask = Task.Run(() => ConnectAndConsumeAsync(channel.Id, lease, cancellationToken), CancellationToken.None);
            }
        }
    }

    private async Task ConnectAndConsumeAsync(long channelId, StreamConnectionLease lease, CancellationToken cancellationToken)
    {
        try
        {
            var channel = _db.Queryable<AiPushChannel>().First(item => item.Id == channelId && item.IsDeleted != true && item.IsEnabled == true && item.StreamEnabled == true);
            if (channel == null || string.IsNullOrWhiteSpace(channel.StreamClientId) || string.IsNullOrWhiteSpace(channel.StreamClientSecretProtected)) return;
            var secret = _protector.Unprotect(channel.StreamClientSecretProtected);
            var endpoint = await OpenEndpointAsync(channel.StreamClientId, secret, cancellationToken);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(endpoint, cancellationToken);
            MarkConnected(channelId, lease);
            WriteAudit(channelId, "stream_connected", "success", new { });
            await ReceiveFramesAsync(socket, channelId, cancellationToken);
            ScheduleReconnect(channelId, lease, "connection_closed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DingTalk Stream channel {ChannelId} disconnected.", channelId);
            ScheduleReconnect(channelId, lease, "stream_connect_failed");
        }
    }

    private async Task<Uri> OpenEndpointAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dingtalk.com/v1.0/gateway/connections/open")
        {
            Content = JsonContent.Create(new
            {
                clientId,
                clientSecret,
                subscriptions = new[] { new { type = "CALLBACK", topic = BotTopic }, new { type = "EVENT", topic = "*" } },
                ua = "aiagent-dotnet/1.0"
            })
        };
        using var response = await _httpClientFactory.CreateClient("DingTalkStreamGateway").SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var endpoint = document.RootElement.GetProperty("endpoint").GetString();
        var ticket = document.RootElement.GetProperty("ticket").GetString();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(ticket) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "wss") throw new InvalidOperationException("DingTalk Stream gateway returned an invalid endpoint.");
        var connector = uri.Query.Length == 0 ? "?" : "&";
        return new Uri(uri.ToString() + connector + "ticket=" + Uri.EscapeDataString(ticket));
    }

    private async Task ReceiveFramesAsync(ClientWebSocket socket, long channelId, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var frame = await ReceiveTextAsync(socket, cancellationToken);
            if (frame == null) return;
            MarkFrameReceived(channelId);
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            var headers = root.TryGetProperty("headers", out var headersElement) ? headersElement : default;
            var messageId = headers.ValueKind == JsonValueKind.Object && headers.TryGetProperty("messageId", out var messageIdElement) ? messageIdElement.GetString() : null;
            var topic = headers.ValueKind == JsonValueKind.Object && headers.TryGetProperty("topic", out var topicElement) ? topicElement.GetString() : null;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (string.Equals(topic, "disconnect", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(type, "CALLBACK", StringComparison.OrdinalIgnoreCase) && string.Equals(topic, BotTopic, StringComparison.OrdinalIgnoreCase))
            {
                var callback = await HandleBotCallbackAsync(channelId, messageId, root, cancellationToken);
                if (callback.Accepted) MarkCallbackReceived(channelId);
                WriteAudit(channelId, "stream_bot_callback", callback.Accepted ? "accepted" : "ignored", new { topic = BotTopic, reason = callback.Reason, conversation_id = callback.ConversationId });
            }
            await SendAckAsync(socket, messageId, string.Equals(type, "EVENT", StringComparison.OrdinalIgnoreCase), cancellationToken);
        }
    }

    private async Task<BotCallbackOutcome> HandleBotCallbackAsync(long channelId, string? sourceMessageId, JsonElement envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceMessageId)) return new BotCallbackOutcome(false, "missing_message_id", null);
        if (!envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) return new BotCallbackOutcome(false, "missing_payload", null);
        using var payload = JsonDocument.Parse(data.GetString() ?? "{}");
        var root = payload.RootElement;
        var isAt = root.TryGetProperty("isInAtList", out var at) && at.ValueKind == JsonValueKind.True;
        var isText = root.TryGetProperty("msgtype", out var msgType) && string.Equals(msgType.GetString(), "text", StringComparison.OrdinalIgnoreCase);
        string? content = null;
        if (root.TryGetProperty("text", out var text) && text.TryGetProperty("content", out var contentElement)) content = contentElement.GetString();
        var webhook = root.TryGetProperty("sessionWebhook", out var webhookElement) ? webhookElement.GetString() : null;
        var conversationId = String(root, "conversationId");
        if (!isAt) return new BotCallbackOutcome(false, "not_at_robot", conversationId);
        if (!isText) return new BotCallbackOutcome(false, "unsupported_message_type", conversationId);
        if (string.IsNullOrWhiteSpace(content)) return new BotCallbackOutcome(false, "empty_message", conversationId);
        if (string.IsNullOrWhiteSpace(webhook)) return new BotCallbackOutcome(false, "missing_session_webhook", conversationId);
        await _agents.EnqueueAsync(new DingTalkBotInboundMessage(channelId, sourceMessageId, conversationId ?? string.Empty, String(root, "senderId"), String(root, "senderNick"), webhook, content), cancellationToken);
        return new BotCallbackOutcome(true, "queued", conversationId);
    }

    private static async Task SendAckAsync(ClientWebSocket socket, string? messageId, bool isEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return;
        var response = new { code = 200, message = "OK", headers = new { messageId, contentType = "application/json" }, data = isEvent ? "{\"status\":\"SUCCESS\",\"message\":\"success\"}" : "{\"response\":null}" };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(stream.ToArray()) : null;
    }

    private void MarkConnected(long channelId, StreamConnectionLease lease)
    {
        lock (lease.Gate)
        {
            lease.FailureCount = 0;
            lease.NextAttemptAtUtc = DateTime.MinValue;
        }
        var now = DateTime.UtcNow;
        _db.Updateable<AiPushChannel>().SetColumns(channel => channel.StreamStatus == "connected").SetColumns(channel => channel.StreamLastErrorCode == null).SetColumns(channel => channel.StreamConnectedAt == now).SetColumns(channel => channel.StreamLastFrameAt == now).SetColumns(channel => channel.StreamReconnectAttempt == 0).SetColumns(channel => channel.StreamNextReconnectAt == null).SetColumns(channel => channel.UpdatedAt == now).Where(channel => channel.Id == channelId).ExecuteCommand();
    }

    private void MarkFrameReceived(long channelId)
    {
        var now = DateTime.UtcNow;
        _db.Updateable<AiPushChannel>().SetColumns(channel => channel.StreamLastFrameAt == now).SetColumns(channel => channel.UpdatedAt == now).Where(channel => channel.Id == channelId).ExecuteCommand();
    }

    private void MarkCallbackReceived(long channelId)
    {
        var now = DateTime.UtcNow;
        _db.Updateable<AiPushChannel>().SetColumns(channel => channel.StreamLastCallbackAt == now).SetColumns(channel => channel.StreamLastFrameAt == now).SetColumns(channel => channel.UpdatedAt == now).Where(channel => channel.Id == channelId).ExecuteCommand();
    }

    private void ScheduleReconnect(long channelId, StreamConnectionLease lease, string errorCode)
    {
        int attempt;
        DateTime retryAt;
        lock (lease.Gate)
        {
            // 2, 4, 8 ... 256, then cap the delay at five minutes.
            lease.FailureCount = Math.Min(lease.FailureCount + 1, 9);
            attempt = lease.FailureCount;
            var delaySeconds = Math.Min(MaximumReconnectDelaySeconds, 1 << attempt);
            retryAt = DateTime.UtcNow.AddSeconds(delaySeconds);
            lease.NextAttemptAtUtc = retryAt;
        }
        _db.Updateable<AiPushChannel>().SetColumns(channel => channel.StreamStatus == "reconnecting").SetColumns(channel => channel.StreamLastErrorCode == errorCode).SetColumns(channel => channel.StreamReconnectAttempt == attempt).SetColumns(channel => channel.StreamNextReconnectAt == retryAt).SetColumns(channel => channel.UpdatedAt == DateTime.UtcNow).Where(channel => channel.Id == channelId).ExecuteCommand();
        WriteAudit(channelId, "stream_reconnect_scheduled", "retry_wait", new { error_code = errorCode, attempt, retry_at = retryAt });
    }

    private void WriteAudit(long channelId, string action, string outcome, object metadata)
        => _db.Insertable(new AiPushAuditLog { CorrelationId = Guid.NewGuid().ToString("N"), ActorType = "system", Action = action, Outcome = outcome, PushChannelId = channelId, MetadataJson = JsonSerializer.Serialize(metadata), OccurredAt = DateTime.UtcNow }).ExecuteCommand();

    private static string? String(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.GetString() : null;
    private sealed record BotCallbackOutcome(bool Accepted, string Reason, string? ConversationId);
    private sealed class StreamConnectionLease
    {
        public object Gate { get; } = new();
        public Task? RunningTask { get; set; }
        public int FailureCount { get; set; }
        public DateTime NextAttemptAtUtc { get; set; }
    }
}
