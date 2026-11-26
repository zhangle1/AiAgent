using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Chat;

public sealed class ChatDeliveryDto
{
    [JsonPropertyName("command_id")] public string CommandId { get; set; } = "";
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("queue_held")] public bool QueueHeld { get; set; }
    [JsonPropertyName("agent")] public string? Agent { get; set; }
    [JsonPropertyName("position")] public long Position { get; set; }
}
