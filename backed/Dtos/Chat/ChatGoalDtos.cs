using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Chat;

public sealed class ChatGoalRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "start";
    [JsonPropertyName("objective")]
    public string? Objective { get; set; }
    [JsonPropertyName("token_budget")]
    public long? TokenBudget { get; set; }
    [JsonPropertyName("expected_version")]
    public long? ExpectedVersion { get; set; }
}

// Stored independently of chat preferences. Thread ids are never accepted from clients.
public sealed class ChatGoalState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Objective { get; set; } = "";
    public string Status { get; set; } = "paused";
    public long? TokenBudget { get; set; }
    public long TokensUsed { get; set; }
    public long TimeUsedSeconds { get; set; }
    public long Version { get; set; }
    public string? StopReason { get; set; }
    public string? ThreadId { get; set; }
    public string Binding { get; set; } = "";
    public object Snapshot(bool running) => new { Id, Objective, Status, TokenBudget, TokensUsed, TimeUsedSeconds, Version, StopReason, Running = running };
}
