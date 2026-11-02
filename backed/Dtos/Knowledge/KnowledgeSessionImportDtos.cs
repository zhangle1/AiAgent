using AiAgent.Backend.Dtos.Chat;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Knowledge;

public sealed class PlatformChatSessionSummaryDto : ChatSessionSummaryDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("user_alias")] public string? UserAlias { get; set; }
}

public sealed class PlatformSessionImportOptionsDto
{
    [JsonPropertyName("sessions")] public List<PlatformChatSessionSummaryDto> Sessions { get; set; } = [];
    [JsonPropertyName("can_import_all")] public bool CanImportAll { get; set; }
}

public sealed class PlatformSessionImportRequest
{
    [JsonPropertyName("session_ids")] public List<string> SessionIds { get; set; } = [];
}

public sealed class PlatformSessionImportResultDto
{
    [JsonPropertyName("imported_sessions")] public int ImportedSessions { get; set; }
    [JsonPropertyName("appended_messages")] public int AppendedMessages { get; set; }
    [JsonPropertyName("sessions")] public List<PlatformChatSessionSummaryDto> Sessions { get; set; } = [];
}
