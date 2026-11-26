using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.ChangeAtlas;

public sealed record AtlasCheckpointRequest([property: JsonPropertyName("session_id"), JsonRequired] string? SessionId);

public sealed record AtlasCheckpointSession(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title);

public sealed record AtlasCheckpointDelivery(
    [property: JsonPropertyName("task_title")] string? TaskTitle,
    [property: JsonPropertyName("validation_status")] string? ValidationStatus);

public sealed record AtlasCheckpoint(
    [property: JsonPropertyName("session")] AtlasCheckpointSession? Session,
    [property: JsonPropertyName("sessions")] List<AtlasCheckpointSession> Sessions,
    [property: JsonPropertyName("delivery")] AtlasCheckpointDelivery? Delivery);
