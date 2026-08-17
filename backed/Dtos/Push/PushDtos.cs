using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Push;

public sealed class PushChannelUpsertRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("webhook_url")] public string? WebhookUrl { get; set; }
    [JsonPropertyName("sign_secret")] public string? SignSecret { get; set; }
    [JsonPropertyName("clear_sign_secret")] public bool ClearSignSecret { get; set; }
    [JsonPropertyName("robot_code")] public string? RobotCode { get; set; }
    [JsonPropertyName("stream_client_id")] public string? StreamClientId { get; set; }
    [JsonPropertyName("stream_client_secret")] public string? StreamClientSecret { get; set; }
    [JsonPropertyName("clear_stream_client_secret")] public bool ClearStreamClientSecret { get; set; }
    [JsonPropertyName("stream_enabled")] public bool StreamEnabled { get; set; }
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

public sealed class PushChannelDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("provider_type")] public string ProviderType { get; set; } = "dingtalk_custom_webhook";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("has_access_token")] public bool HasAccessToken { get; set; }
    [JsonPropertyName("has_sign_secret")] public bool HasSignSecret { get; set; }
    [JsonPropertyName("robot_code")] public string? RobotCode { get; set; }
    [JsonPropertyName("stream_client_id")] public string? StreamClientId { get; set; }
    [JsonPropertyName("has_stream_client_secret")] public bool HasStreamClientSecret { get; set; }
    [JsonPropertyName("stream_enabled")] public bool StreamEnabled { get; set; }
    [JsonPropertyName("stream_status")] public string? StreamStatus { get; set; }
    [JsonPropertyName("stream_last_error_code")] public string? StreamLastErrorCode { get; set; }
    [JsonPropertyName("stream_connected_at")] public DateTime? StreamConnectedAt { get; set; }
    [JsonPropertyName("stream_last_frame_at")] public DateTime? StreamLastFrameAt { get; set; }
    [JsonPropertyName("stream_last_callback_at")] public DateTime? StreamLastCallbackAt { get; set; }
    [JsonPropertyName("stream_reconnect_attempt")] public int? StreamReconnectAttempt { get; set; }
    [JsonPropertyName("stream_next_reconnect_at")] public DateTime? StreamNextReconnectAt { get; set; }
    [JsonPropertyName("project_ids")] public List<long> ProjectIds { get; set; } = [];
    [JsonPropertyName("project_names")] public List<string> ProjectNames { get; set; } = [];
    [JsonPropertyName("last_tested_at")] public DateTime? LastTestedAt { get; set; }
    [JsonPropertyName("last_test_status")] public string? LastTestStatus { get; set; }
    [JsonPropertyName("last_error_code")] public string? LastErrorCode { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}

public sealed class PushChannelTestResultDto
{
    [JsonPropertyName("status")] public string Status { get; set; } = "failed";
    [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("tested_at")] public DateTime TestedAt { get; set; }
}

public sealed class ProjectPushBindingUpsertRequest
{
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("push_channel_id")] public long PushChannelId { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

public sealed class ProjectPushBindingDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("project_name")] public string ProjectName { get; set; } = string.Empty;
    [JsonPropertyName("push_channel_id")] public long PushChannelId { get; set; }
    [JsonPropertyName("push_channel_name")] public string PushChannelName { get; set; } = string.Empty;
    [JsonPropertyName("provider_type")] public string ProviderType { get; set; } = "dingtalk_custom_webhook";
    [JsonPropertyName("trigger_type")] public string TriggerType { get; set; } = "git_push_succeeded";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("last_sent_at")] public DateTime? LastSentAt { get; set; }
    [JsonPropertyName("last_status")] public string? LastStatus { get; set; }
    [JsonPropertyName("last_error_code")] public string? LastErrorCode { get; set; }
}

public sealed class PushAuditDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("occurred_at")] public DateTime? OccurredAt { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = string.Empty;
    [JsonPropertyName("project_id")] public long? ProjectId { get; set; }
    [JsonPropertyName("push_channel_id")] public long? PushChannelId { get; set; }
    [JsonPropertyName("metadata")] public string? Metadata { get; set; }
}
