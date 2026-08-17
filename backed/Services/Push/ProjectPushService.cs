using AiAgent.Backend.Dtos.Push;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Push;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Push;

public sealed record ProjectGitPushSucceededEvent(
    long ProjectId,
    long RepositoryId,
    string RepositoryName,
    string Branch,
    string? ShortSha,
    string CommitSummary,
    string OperatorId,
    string OperationId);

public interface IProjectPushService
{
    Task<List<PushChannelDto>> ListChannelsAsync(AuthenticatedUser administrator, CancellationToken cancellationToken);
    Task<PushChannelDto> CreateChannelAsync(AuthenticatedUser administrator, PushChannelUpsertRequest request, CancellationToken cancellationToken);
    Task<PushChannelDto?> UpdateChannelAsync(AuthenticatedUser administrator, long channelId, PushChannelUpsertRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteChannelAsync(AuthenticatedUser administrator, long channelId, CancellationToken cancellationToken);
    Task<PushChannelTestResultDto?> TestChannelAsync(AuthenticatedUser administrator, long channelId, CancellationToken cancellationToken);
    Task<PushChannelTestResultDto?> TestStreamChannelAsync(AuthenticatedUser administrator, long channelId, CancellationToken cancellationToken);
    Task<List<ProjectPushBindingDto>> ListBindingsAsync(AuthenticatedUser administrator, CancellationToken cancellationToken);
    Task<ProjectPushBindingDto> CreateBindingAsync(AuthenticatedUser administrator, ProjectPushBindingUpsertRequest request, CancellationToken cancellationToken);
    Task<ProjectPushBindingDto?> UpdateBindingAsync(AuthenticatedUser administrator, long bindingId, ProjectPushBindingUpsertRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteBindingAsync(AuthenticatedUser administrator, long bindingId, CancellationToken cancellationToken);
    Task<List<PushAuditDto>> ListAuditAsync(AuthenticatedUser administrator, int limit, CancellationToken cancellationToken);
    Task QueueGitPushSucceededAsync(ProjectGitPushSucceededEvent pushEvent, CancellationToken cancellationToken);
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}

/// <summary>Administrator-configured outbound push service. It never accepts an arbitrary destination URL at send time.</summary>
public sealed class ProjectPushService : IProjectPushService
{
    private const string ProviderType = "dingtalk_custom_webhook";
    private const string GitPushSucceeded = "git_push_succeeded";
    private const int MaxAttempts = 5;
    private static readonly TimeZoneInfo ChinaStandardTime = ResolveChinaStandardTime();
    private readonly ISqlSugarClient _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProjectPushService> _logger;

    public ProjectPushService(ISqlSugarClient db, IDataProtectionProvider dataProtectionProvider, IHttpClientFactory httpClientFactory, ILogger<ProjectPushService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.Push.DingTalkWebhook.v1");
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<List<PushChannelDto>> ListChannelsAsync(AuthenticatedUser administrator, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var channels = _db.Queryable<AiPushChannel>().Where(item => item.IsDeleted != true).OrderByDescending(item => item.IsEnabled).OrderBy(item => item.Name).ToList();
        return Task.FromResult(ToChannelDtos(channels));
    }

    public Task<PushChannelDto> CreateChannelAsync(AuthenticatedUser administrator, PushChannelUpsertRequest request, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var name = NormalizeName(request.Name);
        var token = ExtractAccessToken(request.WebhookUrl, required: true);
        var secret = NormalizeSecret(request.SignSecret);
        var streamClientId = NormalizeDingTalkIdentifier(request.StreamClientId, "Stream Client ID");
        var streamSecret = NormalizeSecret(request.StreamClientSecret);
        var robotCode = NormalizeDingTalkIdentifier(request.RobotCode, "机器人 robotCode");
        ValidateStreamConfiguration(request.StreamEnabled, streamClientId, streamSecret, robotCode, existingSecret: false);
        EnsureUniqueStreamIdentity(null, request.Enabled && request.StreamEnabled, streamClientId, robotCode);
        if (_db.Queryable<AiPushChannel>().Any(item => item.Name == name && item.IsDeleted != true)) throw new InvalidOperationException("推送通道名称已存在。");
        var now = DateTime.UtcNow;
        var channel = new AiPushChannel
        {
            Name = name,
            ProviderType = ProviderType,
            AccessTokenProtected = _protector.Protect(token!),
            SignSecretProtected = secret == null ? null : _protector.Protect(secret),
            DingTalkRobotCode = robotCode,
            StreamClientId = streamClientId,
            StreamClientSecretProtected = streamSecret == null ? null : _protector.Protect(streamSecret),
            StreamEnabled = request.StreamEnabled,
            StreamStatus = request.StreamEnabled ? "connecting" : "disabled",
            IsEnabled = request.Enabled,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Insertable(channel).ExecuteCommand();
        SyncChannelProjects(channel.Id, request.ProjectIds, administrator.Id);
        WriteAudit("administrator", administrator.Id, "channel_created", "success", null, channel.Id, new { channel.Name, channel.ProviderType, project_count = request.ProjectIds.Distinct().Count(), stream_enabled = request.StreamEnabled });
        return Task.FromResult(ToChannelDtos(new[] { channel }).Single());
    }

    public Task<PushChannelDto?> UpdateChannelAsync(AuthenticatedUser administrator, long channelId, PushChannelUpsertRequest request, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var channel = FindChannel(channelId);
        if (channel == null) return Task.FromResult<PushChannelDto?>(null);
        var name = NormalizeName(request.Name);
        if (_db.Queryable<AiPushChannel>().Any(item => item.Id != channelId && item.Name == name && item.IsDeleted != true)) throw new InvalidOperationException("推送通道名称已存在。");
        var token = ExtractAccessToken(request.WebhookUrl, required: false);
        var secret = NormalizeSecret(request.SignSecret);
        var streamClientId = NormalizeDingTalkIdentifier(request.StreamClientId, "Stream Client ID");
        var streamSecret = NormalizeSecret(request.StreamClientSecret);
        var robotCode = NormalizeDingTalkIdentifier(request.RobotCode, "机器人 robotCode");
        ValidateStreamConfiguration(request.StreamEnabled, streamClientId ?? channel.StreamClientId, streamSecret, robotCode ?? channel.DingTalkRobotCode, !string.IsNullOrWhiteSpace(channel.StreamClientSecretProtected) && !request.ClearStreamClientSecret);
        EnsureUniqueStreamIdentity(channel.Id, request.Enabled && request.StreamEnabled, streamClientId ?? channel.StreamClientId, robotCode ?? channel.DingTalkRobotCode);
        channel.Name = name;
        channel.IsEnabled = request.Enabled;
        channel.UpdatedAt = DateTime.UtcNow;
        if (token != null) channel.AccessTokenProtected = _protector.Protect(token);
        if (request.ClearSignSecret) channel.SignSecretProtected = null;
        else if (secret != null) channel.SignSecretProtected = _protector.Protect(secret);
        if (streamClientId != null) channel.StreamClientId = streamClientId;
        if (robotCode != null) channel.DingTalkRobotCode = robotCode;
        if (request.ClearStreamClientSecret) channel.StreamClientSecretProtected = null;
        else if (streamSecret != null) channel.StreamClientSecretProtected = _protector.Protect(streamSecret);
        channel.StreamEnabled = request.StreamEnabled;
        channel.StreamStatus = request.StreamEnabled ? "connecting" : "disabled";
        channel.StreamLastErrorCode = null;
        if (channel.IsEnabled == true && string.IsNullOrWhiteSpace(channel.AccessTokenProtected)) throw new InvalidOperationException("请先保存 Webhook access_token 后再启用通道。");
        _db.Updateable(channel).UpdateColumns(item => new { item.Name, item.AccessTokenProtected, item.SignSecretProtected, item.DingTalkRobotCode, item.StreamClientId, item.StreamClientSecretProtected, item.StreamEnabled, item.StreamStatus, item.StreamLastErrorCode, item.IsEnabled, item.UpdatedAt }).ExecuteCommand();
        SyncChannelProjects(channel.Id, request.ProjectIds, administrator.Id);
        WriteAudit("administrator", administrator.Id, "channel_updated", "success", null, channel.Id, new { channel.Name, channel.IsEnabled, token_updated = token != null, sign_secret_updated = secret != null || request.ClearSignSecret, stream_enabled = request.StreamEnabled, stream_secret_updated = streamSecret != null || request.ClearStreamClientSecret, project_count = request.ProjectIds.Distinct().Count() });
        return Task.FromResult<PushChannelDto?>(ToChannelDtos(new[] { channel }).Single());
    }

    public Task<bool> DeleteChannelAsync(AuthenticatedUser administrator, long channelId, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;
        var affected = _db.Updateable<AiPushChannel>().SetColumns(item => item.IsEnabled == false).SetColumns(item => item.IsDeleted == true).SetColumns(item => item.DeletedAt == now).SetColumns(item => item.UpdatedAt == now).Where(item => item.Id == channelId && item.IsDeleted != true).ExecuteCommand();
        if (affected <= 0) return Task.FromResult(false);
        _db.Updateable<AiProjectPushBinding>().SetColumns(item => item.IsEnabled == false).SetColumns(item => item.UpdatedAt == now).Where(item => item.PushChannelId == channelId && item.IsDeleted != true).ExecuteCommand();
        _db.Updateable<AiPushOutboxMessage>().SetColumns(item => item.Status == "cancelled").Where(item => item.PushChannelId == channelId && (item.Status == "pending" || item.Status == "retry_wait")).ExecuteCommand();
        WriteAudit("administrator", administrator.Id, "channel_deleted", "success", null, channelId, new { });
        return Task.FromResult(true);
    }

    public async Task<PushChannelTestResultDto?> TestChannelAsync(AuthenticatedUser administrator, long channelId, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        var channel = FindChannel(channelId);
        if (channel == null) return null;
        if (channel.IsEnabled != true) throw new InvalidOperationException("请先启用推送通道后再发送测试消息。");
        if (string.IsNullOrWhiteSpace(channel.AccessTokenProtected)) throw new InvalidOperationException("请先保存 Webhook access_token 后再发送测试消息。");
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        if (_db.Queryable<AiPushAuditLog>().Where(item => item.PushChannelId == channelId && item.Action == "channel_test" && item.OccurredAt >= cutoff).Count() >= 3)
            throw new InvalidOperationException("同一推送通道 10 分钟内最多发送 3 次测试消息。");

        var now = DateTime.UtcNow;
        try
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(now, ChinaStandardTime);
            await SendTextAsync(channel, $"【AiAgent】钉钉推送通道测试成功。时间：{localTime:yyyy-MM-dd HH:mm:ss} 北京时间（UTC+08:00）", cancellationToken);
            channel.LastTestedAt = now;
            channel.UpdatedAt = now;
            channel.LastTestStatus = "success";
            channel.LastErrorCode = null;
            UpdateChannelTestState(channel);
            WriteAudit("administrator", administrator.Id, "channel_test", "success", null, channel.Id, new { });
            return new PushChannelTestResultDto { Status = "success", Summary = "测试消息已发送。", TestedAt = now };
        }
        catch (PushDeliveryException ex)
        {
            channel.LastTestedAt = now;
            channel.UpdatedAt = now;
            channel.LastTestStatus = "failed";
            channel.LastErrorCode = ex.Code;
            UpdateChannelTestState(channel);
            WriteAudit("administrator", administrator.Id, "channel_test", "failed", null, channel.Id, new { error_code = ex.Code });
            return new PushChannelTestResultDto { Status = "failed", Summary = "测试消息发送失败，请检查 Webhook 配置或服务器网络。", TestedAt = now };
        }
    }

    public async Task<PushChannelTestResultDto?> TestStreamChannelAsync(AuthenticatedUser administrator, long channelId, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        var channel = FindChannel(channelId);
        if (channel == null) return null;
        if (string.IsNullOrWhiteSpace(channel.DingTalkRobotCode) || string.IsNullOrWhiteSpace(channel.StreamClientId) || string.IsNullOrWhiteSpace(channel.StreamClientSecretProtected))
            throw new InvalidOperationException("请先保存机器人 robotCode、Stream Client ID 和 Client Secret。");
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        if (_db.Queryable<AiPushAuditLog>().Where(item => item.PushChannelId == channelId && item.Action == "stream_test" && item.OccurredAt >= cutoff).Count() >= 3)
            throw new InvalidOperationException("同一推送通道 10 分钟内最多执行 3 次 Stream 测试。");

        var now = DateTime.UtcNow;
        var errorCode = (string?)null;
        var summary = string.Empty;
        try
        {
            var clientSecret = Unprotect(channel.StreamClientSecretProtected, "无法读取已保存的 Stream Client Secret，请重新保存通道。");
            using var testTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            testTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            var endpoint = await OpenDingTalkStreamEndpointAsync(channel.StreamClientId, clientSecret, testTimeout.Token);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(endpoint, testTimeout.Token);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "stream test complete", testTimeout.Token);
            channel.StreamStatus = "verified";
            channel.StreamLastErrorCode = null;
            channel.StreamConnectedAt = now;
            channel.UpdatedAt = now;
            UpdateStreamTestState(channel);
            WriteAudit("administrator", administrator.Id, "stream_test", "success", null, channel.Id, new { stage = "websocket_connected" });
            return new PushChannelTestResultDto { Status = "success", Summary = "Stream 凭据、ticket 和 WebSocket 连接验证成功。请在钉钉群内 @机器人 发送文本，验证实际回调。", TestedAt = now };
        }
        catch (PushDeliveryException ex)
        {
            errorCode = ex.Code;
            summary = "Stream 配置校验失败，请检查已保存的 Client ID / Client Secret。";
        }
        catch (HttpRequestException ex)
        {
            errorCode = ex.StatusCode.HasValue ? $"gateway_http_{(int)ex.StatusCode.Value}" : "gateway_network_error";
            summary = ex.StatusCode.HasValue ? $"钉钉 Stream 网关返回 HTTP {(int)ex.StatusCode.Value}。请检查应用凭据、机器人能力和发布状态。" : "无法连接钉钉 Stream 网关。请检查服务器网络、代理或 DNS。";
        }
        catch (WebSocketException)
        {
            errorCode = "websocket_connect_failed";
            summary = "已取得 Stream ticket，但 WebSocket 连接失败。请检查服务器到钉钉的 WSS 网络策略。";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            errorCode = "stream_test_timeout";
            summary = "Stream 连接超时。请检查服务器网络或钉钉应用配置。";
        }
        finally
        {
            if (errorCode != null)
            {
                channel.StreamStatus = "test_failed";
                channel.StreamLastErrorCode = errorCode;
                channel.UpdatedAt = now;
                UpdateStreamTestState(channel);
                WriteAudit("administrator", administrator.Id, "stream_test", "failed", null, channel.Id, new { error_code = errorCode });
            }
        }
        return new PushChannelTestResultDto { Status = "failed", Summary = summary, TestedAt = now };
    }

    public Task<List<ProjectPushBindingDto>> ListBindingsAsync(AuthenticatedUser administrator, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var bindings = _db.Queryable<AiProjectPushBinding>().Where(item => item.IsDeleted != true).OrderByDescending(item => item.IsEnabled).OrderByDescending(item => item.UpdatedAt).ToList();
        var projectIds = bindings.Where(item => item.ProjectId.HasValue).Select(item => item.ProjectId!.Value).Distinct().ToList();
        var channelIds = bindings.Where(item => item.PushChannelId.HasValue).Select(item => item.PushChannelId!.Value).Distinct().ToList();
        var projects = projectIds.Count == 0 ? new Dictionary<long, AiCodeProject>() : _db.Queryable<AiCodeProject>().Where(item => projectIds.Contains(item.Id) && !item.IsDeleted).ToList().ToDictionary(item => item.Id);
        var channels = channelIds.Count == 0 ? new Dictionary<long, AiPushChannel>() : _db.Queryable<AiPushChannel>().Where(item => channelIds.Contains(item.Id) && item.IsDeleted != true).ToList().ToDictionary(item => item.Id);
        return Task.FromResult(bindings.Where(item => item.ProjectId.HasValue && item.PushChannelId.HasValue && projects.ContainsKey(item.ProjectId.Value) && channels.ContainsKey(item.PushChannelId.Value)).Select(item => ToBindingDto(item, projects[item.ProjectId!.Value], channels[item.PushChannelId!.Value])).ToList());
    }

    public Task<ProjectPushBindingDto> CreateBindingAsync(AuthenticatedUser administrator, ProjectPushBindingUpsertRequest request, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var project = FindProject(request.ProjectId) ?? throw new InvalidOperationException("所选项目不存在或已被删除。");
        var channel = FindChannel(request.PushChannelId) ?? throw new InvalidOperationException("所选推送通道不存在或已被删除。");
        if (channel.IsEnabled != true) throw new InvalidOperationException("只能绑定已启用的推送通道。");
        if (_db.Queryable<AiProjectPushBinding>().Any(item => item.ProjectId == project.Id && item.PushChannelId == channel.Id && item.TriggerType == GitPushSucceeded && item.IsDeleted != true)) throw new InvalidOperationException("该项目已绑定此推送通道。");
        var now = DateTime.UtcNow;
        var binding = new AiProjectPushBinding { ProjectId = project.Id, PushChannelId = channel.Id, TriggerType = GitPushSucceeded, TemplateCode = GitPushSucceeded, IsEnabled = request.Enabled, IsDeleted = false, CreatedBy = administrator.Id, CreatedAt = now, UpdatedAt = now };
        _db.Insertable(binding).ExecuteCommand();
        WriteAudit("administrator", administrator.Id, "binding_created", "success", project.Id, channel.Id, new { binding_id = binding.Id });
        return Task.FromResult(ToBindingDto(binding, project, channel));
    }

    public Task<ProjectPushBindingDto?> UpdateBindingAsync(AuthenticatedUser administrator, long bindingId, ProjectPushBindingUpsertRequest request, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = _db.Queryable<AiProjectPushBinding>().First(item => item.Id == bindingId && item.IsDeleted != true);
        if (binding == null) return Task.FromResult<ProjectPushBindingDto?>(null);
        var project = FindProject(binding.ProjectId ?? 0) ?? throw new InvalidOperationException("绑定项目已不存在。");
        var channel = FindChannel(binding.PushChannelId ?? 0) ?? throw new InvalidOperationException("绑定推送通道已不存在。");
        if (request.Enabled && channel.IsEnabled != true) throw new InvalidOperationException("只能启用已启用的推送通道绑定。");
        binding.IsEnabled = request.Enabled;
        binding.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(binding).UpdateColumns(item => new { item.IsEnabled, item.UpdatedAt }).ExecuteCommand();
        if (!request.Enabled) _db.Updateable<AiPushOutboxMessage>().SetColumns(item => item.Status == "cancelled").Where(item => item.ProjectPushBindingId == binding.Id && (item.Status == "pending" || item.Status == "retry_wait")).ExecuteCommand();
        WriteAudit("administrator", administrator.Id, "binding_updated", "success", project.Id, channel.Id, new { binding_id = binding.Id, binding.IsEnabled });
        return Task.FromResult<ProjectPushBindingDto?>(ToBindingDto(binding, project, channel));
    }

    public Task<bool> DeleteBindingAsync(AuthenticatedUser administrator, long bindingId, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = _db.Queryable<AiProjectPushBinding>().First(item => item.Id == bindingId && item.IsDeleted != true);
        if (binding == null) return Task.FromResult(false);
        var now = DateTime.UtcNow;
        _db.Updateable<AiProjectPushBinding>().SetColumns(item => item.IsEnabled == false).SetColumns(item => item.IsDeleted == true).SetColumns(item => item.UpdatedAt == now).Where(item => item.Id == bindingId).ExecuteCommand();
        _db.Updateable<AiPushOutboxMessage>().SetColumns(item => item.Status == "cancelled").Where(item => item.ProjectPushBindingId == bindingId && (item.Status == "pending" || item.Status == "retry_wait")).ExecuteCommand();
        WriteAudit("administrator", administrator.Id, "binding_deleted", "success", binding.ProjectId, binding.PushChannelId, new { binding_id = bindingId });
        return Task.FromResult(true);
    }

    public Task<List<PushAuditDto>> ListAuditAsync(AuthenticatedUser administrator, int limit, CancellationToken cancellationToken)
    {
        RequireAdministrator(administrator);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = _db.Queryable<AiPushAuditLog>().OrderByDescending(item => item.OccurredAt).Take(Math.Clamp(limit, 1, 200)).ToList();
        return Task.FromResult(rows.Select(item => new PushAuditDto { Id = item.Id, OccurredAt = item.OccurredAt, Action = item.Action ?? string.Empty, Outcome = item.Outcome ?? string.Empty, ProjectId = item.ProjectId, PushChannelId = item.PushChannelId, Metadata = item.MetadataJson }).ToList());
    }

    public Task QueueGitPushSucceededAsync(ProjectGitPushSucceededEvent pushEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var project = FindProject(pushEvent.ProjectId);
        if (project == null) return Task.CompletedTask;
        var bindings = _db.Queryable<AiProjectPushBinding>().Where(item => item.ProjectId == pushEvent.ProjectId && item.TriggerType == GitPushSucceeded && item.IsEnabled == true && item.IsDeleted != true).ToList();
        foreach (var binding in bindings)
        {
            if (!binding.PushChannelId.HasValue) continue;
            var channel = FindChannel(binding.PushChannelId.Value);
            if (channel?.IsEnabled != true) continue;
            var dedupKey = $"{pushEvent.OperationId}:{pushEvent.RepositoryId}:{binding.Id}";
            if (_db.Queryable<AiPushOutboxMessage>().Any(item => item.DedupKey == dedupKey)) continue;
            var payload = new GitPushPayload
            {
                Project = Clean(project.DisplayName, 120),
                Repository = Clean(pushEvent.RepositoryName, 120),
                Branch = Clean(pushEvent.Branch, 120),
                ShortSha = Clean(pushEvent.ShortSha, 16) ?? "未知",
                CommitSummary = Clean(pushEvent.CommitSummary, 200),
                OperatorId = Clean(pushEvent.OperatorId, 64)
            };
            var now = DateTime.UtcNow;
            _db.Insertable(new AiPushOutboxMessage { CorrelationId = Guid.NewGuid().ToString("N"), ProjectId = pushEvent.ProjectId, PushChannelId = channel.Id, ProjectPushBindingId = binding.Id, EventType = GitPushSucceeded, DedupKey = dedupKey, PayloadJson = JsonSerializer.Serialize(payload), Status = "pending", AttemptCount = 0, NextAttemptAt = now, CreatedAt = now }).ExecuteCommand();
            WriteAudit("system", pushEvent.OperatorId, "git_push_queued", "success", pushEvent.ProjectId, channel.Id, new { repository_id = pushEvent.RepositoryId, binding_id = binding.Id });
        }
        return Task.CompletedTask;
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleSendingAt = now.AddMinutes(-2);
        var recovered = _db.Updateable<AiPushOutboxMessage>()
            .SetColumns(item => item.Status == "retry_wait")
            .SetColumns(item => item.NextAttemptAt == now)
            .SetColumns(item => item.SendingAt == null)
            .Where(item => item.Status == "sending" && item.SendingAt < staleSendingAt)
            .ExecuteCommand();
        if (recovered > 0) _logger.LogWarning("Recovered {Count} stale push deliveries.", recovered);
        var candidates = _db.Queryable<AiPushOutboxMessage>().Where(item => (item.Status == "pending" || item.Status == "retry_wait") && item.NextAttemptAt <= now).OrderBy(item => item.CreatedAt).Take(20).ToList();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimed = _db.Updateable<AiPushOutboxMessage>().SetColumns(item => item.Status == "sending").SetColumns(item => item.SendingAt == now).Where(item => item.Id == candidate.Id && (item.Status == "pending" || item.Status == "retry_wait")).ExecuteCommand();
            if (claimed != 1) continue;
            await DeliverAsync(candidate.Id, cancellationToken);
        }
    }

    private async Task DeliverAsync(long messageId, CancellationToken cancellationToken)
    {
        var message = _db.Queryable<AiPushOutboxMessage>().First(item => item.Id == messageId && item.Status == "sending");
        if (message == null) return;
        var binding = message.ProjectPushBindingId.HasValue ? _db.Queryable<AiProjectPushBinding>().First(item => item.Id == message.ProjectPushBindingId.Value && item.IsDeleted != true) : null;
        var channel = message.PushChannelId.HasValue ? FindChannel(message.PushChannelId.Value) : null;
        if (binding?.IsEnabled != true || channel?.IsEnabled != true)
        {
            Finish(message, binding, "cancelled", null, null);
            return;
        }
        try
        {
            var payload = JsonSerializer.Deserialize<GitPushPayload>(message.PayloadJson ?? "{}") ?? throw new PushDeliveryException("invalid_payload", false);
            await SendTextAsync(channel, BuildGitPushText(payload), cancellationToken);
            Finish(message, binding, "sent", null, DateTime.UtcNow);
            WriteAudit("system", null, "message_sent", "success", message.ProjectId, channel.Id, new { message_id = message.Id });
        }
        catch (PushDeliveryException ex)
        {
            RecordDeliveryFailure(message, binding, channel, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Push delivery {MessageId} failed unexpectedly.", message.Id);
            RecordDeliveryFailure(message, binding, channel, new PushDeliveryException("unexpected_error", true));
        }
    }

    private async Task SendTextAsync(AiPushChannel channel, string content, CancellationToken cancellationToken)
    {
        var token = Unprotect(channel.AccessTokenProtected, "Unable to read saved Webhook access token. Save this channel again.");
        var secret = string.IsNullOrWhiteSpace(channel.SignSecretProtected) ? null : Unprotect(channel.SignSecretProtected, "Unable to read saved signing secret. Save this channel again.");
        var endpoint = BuildEndpoint(token, secret);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(new { msgtype = "text", text = new { content } }) };
            using var response = await _httpClientFactory.CreateClient("DingTalkWebhook").SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new PushDeliveryException($"http_{(int)response.StatusCode}", response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500);
            try
            {
                using var document = JsonDocument.Parse(body);
                var errorCode = 0;
                var hasErrorCode = document.RootElement.TryGetProperty("errcode", out var code) && code.TryGetInt32(out errorCode);
                if (!hasErrorCode || errorCode != 0) throw new PushDeliveryException(hasErrorCode ? $"dingtalk_{errorCode}" : "invalid_response", false);
            }
            catch (JsonException)
            {
                throw new PushDeliveryException("invalid_response", true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new PushDeliveryException("timeout", true);
        }
        catch (HttpRequestException)
        {
            throw new PushDeliveryException("network_error", true);
        }
    }

    private static string BuildEndpoint(string token, string? secret)
    {
        var endpoint = $"https://oapi.dingtalk.com/robot/send?access_token={Uri.EscapeDataString(token)}";
        if (string.IsNullOrWhiteSpace(secret)) return endpoint;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var toSign = $"{timestamp}\n{secret}";
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(toSign)));
        return $"{endpoint}&timestamp={timestamp}&sign={Uri.EscapeDataString(signature)}";
    }

    private async Task<Uri> OpenDingTalkStreamEndpointAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dingtalk.com/v1.0/gateway/connections/open")
            {
                Content = JsonContent.Create(new
                {
                    clientId,
                    clientSecret,
                    subscriptions = new[] { new { type = "CALLBACK", topic = "/v1.0/im/bot/messages/get" }, new { type = "EVENT", topic = "*" } },
                    ua = "aiagent-dotnet/1.0"
                })
            };
            using var response = await _httpClientFactory.CreateClient("DingTalkStreamGateway").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("DingTalk Stream gateway rejected the connection request.", null, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var endpoint = document.RootElement.TryGetProperty("endpoint", out var endpointElement) ? endpointElement.GetString() : null;
            var ticket = document.RootElement.TryGetProperty("ticket", out var ticketElement) ? ticketElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(ticket) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "wss")
                throw new PushDeliveryException("gateway_invalid_response", false);
            return new Uri(uri.ToString() + (uri.Query.Length == 0 ? "?" : "&") + "ticket=" + Uri.EscapeDataString(ticket));
        }
        catch (JsonException)
        {
            throw new PushDeliveryException("gateway_invalid_response", false);
        }
    }

    private void Finish(AiPushOutboxMessage message, AiProjectPushBinding? binding, string status, string? errorCode, DateTime? sentAt)
    {
        message.Status = status;
        message.LastErrorCode = errorCode;
        message.SentAt = sentAt;
        message.NextAttemptAt = null;
        message.SendingAt = null;
        _db.Updateable(message).UpdateColumns(item => new { item.Status, item.LastErrorCode, item.SentAt, item.NextAttemptAt, item.SendingAt }).ExecuteCommand();
        if (binding != null) _db.Updateable<AiProjectPushBinding>().SetColumns(item => item.LastStatus == status).SetColumns(item => item.LastErrorCode == errorCode).SetColumns(item => item.LastSentAt == sentAt).SetColumns(item => item.UpdatedAt == DateTime.UtcNow).Where(item => item.Id == binding.Id).ExecuteCommand();
    }

    private void RecordDeliveryFailure(AiPushOutboxMessage message, AiProjectPushBinding? binding, AiPushChannel? channel, PushDeliveryException exception)
    {
        var attempts = (message.AttemptCount ?? 0) + 1;
        var retry = exception.Retryable && attempts < MaxAttempts;
        message.AttemptCount = attempts;
        message.LastErrorCode = exception.Code;
        message.Status = retry ? "retry_wait" : "failed";
        message.NextAttemptAt = retry ? DateTime.UtcNow.Add(GetRetryDelay(attempts)) : null;
        message.SendingAt = null;
        _db.Updateable(message).UpdateColumns(item => new { item.AttemptCount, item.LastErrorCode, item.Status, item.NextAttemptAt, item.SendingAt }).ExecuteCommand();
        if (binding != null) _db.Updateable<AiProjectPushBinding>().SetColumns(item => item.LastStatus == message.Status).SetColumns(item => item.LastErrorCode == exception.Code).SetColumns(item => item.UpdatedAt == DateTime.UtcNow).Where(item => item.Id == binding.Id).ExecuteCommand();
        WriteAudit("system", null, "message_sent", retry ? "retry_wait" : "failed", message.ProjectId, channel?.Id, new { message_id = message.Id, error_code = exception.Code });
        _logger.LogWarning("Push delivery {MessageId} ended with {ErrorCode}; retry={Retry}", message.Id, exception.Code, retry);
    }

    private void UpdateChannelTestState(AiPushChannel channel) => _db.Updateable(channel).UpdateColumns(item => new { item.LastTestedAt, item.LastTestStatus, item.LastErrorCode, item.UpdatedAt }).ExecuteCommand();
    private void UpdateStreamTestState(AiPushChannel channel) => _db.Updateable(channel).UpdateColumns(item => new { item.StreamStatus, item.StreamLastErrorCode, item.StreamConnectedAt, item.UpdatedAt }).ExecuteCommand();

    private AiPushChannel? FindChannel(long id) => _db.Queryable<AiPushChannel>().First(item => item.Id == id && item.IsDeleted != true);
    private AiCodeProject? FindProject(long id) => _db.Queryable<AiCodeProject>().First(item => item.Id == id && !item.IsDeleted);

    private void WriteAudit(string actorType, string? actorId, string action, string outcome, long? projectId, long? channelId, object metadata)
    {
        _db.Insertable(new AiPushAuditLog { CorrelationId = Guid.NewGuid().ToString("N"), ActorType = actorType, ActorId = actorId, Action = action, Outcome = outcome, ProjectId = projectId, PushChannelId = channelId, MetadataJson = JsonSerializer.Serialize(metadata), OccurredAt = DateTime.UtcNow }).ExecuteCommand();
    }

    private string Unprotect(string? protectedValue, string error)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) throw new PushDeliveryException("credential_missing", false);
        try { return _protector.Unprotect(protectedValue); }
        catch { throw new PushDeliveryException("credential_unreadable", false, error); }
    }

    private List<PushChannelDto> ToChannelDtos(IReadOnlyCollection<AiPushChannel> channels)
    {
        var channelIds = channels.Select(channel => channel.Id).ToList();
        List<AiProjectPushBinding> bindings = channelIds.Count == 0 ? [] : _db.Queryable<AiProjectPushBinding>()
            .Where(binding => binding.PushChannelId.HasValue && channelIds.Contains(binding.PushChannelId.Value) && binding.TriggerType == GitPushSucceeded && binding.IsEnabled == true && binding.IsDeleted != true)
            .ToList();
        var projectIds = bindings.Where(binding => binding.ProjectId.HasValue).Select(binding => binding.ProjectId!.Value).Distinct().ToList();
        var projects = projectIds.Count == 0 ? new Dictionary<long, AiCodeProject>() : _db.Queryable<AiCodeProject>().Where(project => projectIds.Contains(project.Id) && !project.IsDeleted).ToList().ToDictionary(project => project.Id);
        return channels.Select(channel => ToChannelDto(channel, bindings.Where(binding => binding.PushChannelId == channel.Id).Select(binding => binding.ProjectId!.Value).Where(projects.ContainsKey).Distinct().ToList(), projects)).ToList();
    }

    private static PushChannelDto ToChannelDto(AiPushChannel channel, IReadOnlyCollection<long>? projectIds = null, IReadOnlyDictionary<long, AiCodeProject>? projects = null) => new()
    {
        Id = channel.Id,
        Name = channel.Name ?? string.Empty,
        ProviderType = channel.ProviderType ?? ProviderType,
        Enabled = channel.IsEnabled == true,
        HasAccessToken = !string.IsNullOrWhiteSpace(channel.AccessTokenProtected),
        HasSignSecret = !string.IsNullOrWhiteSpace(channel.SignSecretProtected),
        RobotCode = channel.DingTalkRobotCode,
        StreamClientId = channel.StreamClientId,
        HasStreamClientSecret = !string.IsNullOrWhiteSpace(channel.StreamClientSecretProtected),
        StreamEnabled = channel.StreamEnabled == true,
        StreamStatus = channel.StreamStatus,
        StreamLastErrorCode = channel.StreamLastErrorCode,
        StreamConnectedAt = AsUtc(channel.StreamConnectedAt),
        StreamLastFrameAt = AsUtc(channel.StreamLastFrameAt),
        StreamLastCallbackAt = AsUtc(channel.StreamLastCallbackAt),
        StreamReconnectAttempt = channel.StreamReconnectAttempt,
        StreamNextReconnectAt = AsUtc(channel.StreamNextReconnectAt),
        ProjectIds = projectIds?.ToList() ?? [],
        ProjectNames = projectIds == null || projects == null ? [] : projectIds.Where(projects.ContainsKey).Select(projectId => projects[projectId].DisplayName).ToList(),
        LastTestedAt = AsUtc(channel.LastTestedAt),
        LastTestStatus = channel.LastTestStatus,
        LastErrorCode = channel.LastErrorCode,
        UpdatedAt = AsUtc(channel.UpdatedAt)
    };

    private static DateTime? AsUtc(DateTime? value) => value == null ? null : value.Value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static TimeZoneInfo ResolveChinaStandardTime()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
        catch (TimeZoneNotFoundException)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("UTC+08", TimeSpan.FromHours(8), "北京时间", "北京时间"); }
        }
    }

    private void EnsureUniqueStreamIdentity(long? excludedChannelId, bool enabled, string? streamClientId, string? robotCode)
    {
        if (!enabled || string.IsNullOrWhiteSpace(streamClientId) || string.IsNullOrWhiteSpace(robotCode)) return;
        var duplicate = _db.Queryable<AiPushChannel>().Any(channel => (!excludedChannelId.HasValue || channel.Id != excludedChannelId.Value) && channel.IsDeleted != true && channel.IsEnabled == true && channel.StreamEnabled == true && channel.StreamClientId == streamClientId && channel.DingTalkRobotCode == robotCode);
        if (duplicate) throw new InvalidOperationException("同一个钉钉机器人只能配置为一个启用的 Stream 通道；请在该通道中多选项目，而不要重复创建连接。");
    }

    private void SyncChannelProjects(long channelId, IReadOnlyCollection<long>? rawProjectIds, string administratorId)
    {
        var projectIds = (rawProjectIds ?? []).Where(id => id > 0).Distinct().ToList();
        List<AiCodeProject> projects = projectIds.Count == 0 ? [] : _db.Queryable<AiCodeProject>().Where(project => projectIds.Contains(project.Id) && !project.IsDeleted).ToList();
        if (projects.Count != projectIds.Count) throw new InvalidOperationException("所选项目不存在或已被删除。");

        var current = _db.Queryable<AiProjectPushBinding>().Where(binding => binding.PushChannelId == channelId && binding.TriggerType == GitPushSucceeded && binding.IsDeleted != true).ToList();
        var now = DateTime.UtcNow;
        foreach (var binding in current.Where(binding => !binding.ProjectId.HasValue || !projectIds.Contains(binding.ProjectId.Value)))
        {
            var bindingId = binding.Id;
            _db.Updateable<AiProjectPushBinding>().SetColumns(item => item.IsEnabled == false).SetColumns(item => item.UpdatedAt == now).Where(item => item.Id == bindingId).ExecuteCommand();
        }
        foreach (var existing in current.Where(binding => binding.ProjectId.HasValue && projectIds.Contains(binding.ProjectId.Value)))
            _db.Updateable<AiProjectPushBinding>().SetColumns(binding => binding.IsEnabled == true).SetColumns(binding => binding.IsDeleted == false).SetColumns(binding => binding.UpdatedAt == now).Where(binding => binding.Id == existing.Id).ExecuteCommand();
        foreach (var projectId in projectIds.Except(current.Where(binding => binding.ProjectId.HasValue).Select(binding => binding.ProjectId!.Value)))
            _db.Insertable(new AiProjectPushBinding { ProjectId = projectId, PushChannelId = channelId, TriggerType = GitPushSucceeded, TemplateCode = GitPushSucceeded, IsEnabled = true, IsDeleted = false, CreatedBy = administratorId, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
    }

    private static ProjectPushBindingDto ToBindingDto(AiProjectPushBinding binding, AiCodeProject project, AiPushChannel channel) => new()
    {
        Id = binding.Id,
        ProjectId = project.Id,
        ProjectName = project.DisplayName,
        PushChannelId = channel.Id,
        PushChannelName = channel.Name ?? string.Empty,
        ProviderType = channel.ProviderType ?? ProviderType,
        TriggerType = binding.TriggerType ?? GitPushSucceeded,
        Enabled = binding.IsEnabled == true,
        LastSentAt = binding.LastSentAt,
        LastStatus = binding.LastStatus,
        LastErrorCode = binding.LastErrorCode
    };

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 80 || name.Any(char.IsControl)) throw new InvalidOperationException("通道名称需为 2–80 个非控制字符。");
        return name;
    }

    private static string? NormalizeDingTalkIdentifier(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl) || normalized.Any(char.IsWhiteSpace)) throw new InvalidOperationException($"{label} 格式无效。");
        return normalized;
    }

    private static void ValidateStreamConfiguration(bool enabled, string? clientId, string? newSecret, string? robotCode, bool existingSecret)
    {
        if (!enabled) return;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(robotCode) || (!existingSecret && string.IsNullOrWhiteSpace(newSecret)))
            throw new InvalidOperationException("启用 Stream 前必须配置机器人 robotCode、Client ID 与 Client Secret。");
    }

    private static string? ExtractAccessToken(string? rawUrl, bool required)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            if (required) throw new InvalidOperationException("请粘贴钉钉自定义机器人 Webhook 地址。");
            return null;
        }
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "oapi.dingtalk.com", StringComparison.OrdinalIgnoreCase) || uri.Port != 443 || uri.AbsolutePath != "/robot/send" || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Webhook 必须是 https://oapi.dingtalk.com/robot/send?access_token=... 格式。");
        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1) throw new InvalidOperationException("Webhook 只能包含一个 access_token 查询参数。");
        var pair = parts[0].Split('=', 2);
        if (pair.Length != 2 || !string.Equals(Uri.UnescapeDataString(pair[0]), "access_token", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(pair[1])) throw new InvalidOperationException("Webhook 缺少有效的 access_token。");
        var token = Uri.UnescapeDataString(pair[1]);
        if (token.Length > 512 || token.Any(char.IsControl) || token.Any(char.IsWhiteSpace)) throw new InvalidOperationException("Webhook access_token 格式无效。");
        return token;
    }

    private static string? NormalizeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var secret = value.Trim();
        if (secret.Length > 512 || secret.Any(char.IsControl) || secret.Any(char.IsWhiteSpace)) throw new InvalidOperationException("加签 secret 格式无效。");
        return secret;
    }

    private static string BuildGitPushText(GitPushPayload payload) => $"【{payload.Project}】代码已推送\n仓库：{payload.Repository} · 分支：{payload.Branch}\n任务：未关联任务\n提交：{payload.ShortSha} {payload.CommitSummary}\n触发人：{payload.OperatorId}";
    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        normalized = new string(normalized.Where(character => !char.IsControl(character)).ToArray());
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static TimeSpan GetRetryDelay(int attempt) => attempt switch { 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), _ => TimeSpan.FromHours(1) };
    private static void RequireAdministrator(AuthenticatedUser user) { if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required."); }

    private sealed class GitPushPayload
    {
        public string? Project { get; set; }
        public string? Repository { get; set; }
        public string? Branch { get; set; }
        public string? ShortSha { get; set; }
        public string? CommitSummary { get; set; }
        public string? OperatorId { get; set; }
    }

    private sealed class PushDeliveryException : Exception
    {
        public PushDeliveryException(string code, bool retryable, string? message = null) : base(message ?? code) => (Code, Retryable) = (code, retryable);
        public string Code { get; }
        public bool Retryable { get; }
    }
}

public sealed class ProjectPushHostedService : BackgroundService
{
    private readonly IProjectPushService _pushes;
    private readonly ILogger<ProjectPushHostedService> _logger;
    public ProjectPushHostedService(IProjectPushService pushes, ILogger<ProjectPushHostedService> logger) => (_pushes, _logger) = (pushes, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _pushes.ProcessPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Project push worker failed."); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
