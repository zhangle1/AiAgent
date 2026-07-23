using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Usage;

public sealed class UsageSummaryDto
{
    [JsonPropertyName("scope")] public string Scope { get; set; } = "me";
    [JsonPropertyName("can_view_all")] public bool CanViewAll { get; set; }
    [JsonPropertyName("period_days")] public int PeriodDays { get; set; }
    [JsonPropertyName("from")] public DateTime From { get; set; }
    [JsonPropertyName("to")] public DateTime To { get; set; }
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("prompt_tokens")] public long PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public long CompletionTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
    [JsonPropertyName("estimated_turn_count")] public int EstimatedTurnCount { get; set; }
    [JsonPropertyName("providers")] public List<UsageProviderSummaryDto> Providers { get; set; } = [];
    [JsonPropertyName("activity")] public List<UsageActivityDayDto> Activity { get; set; } = [];
}

public sealed class UsageProviderSummaryDto
{
    [JsonPropertyName("provider_kind")] public string ProviderKind { get; set; } = string.Empty;
    [JsonPropertyName("provider_id")] public string ProviderId { get; set; } = string.Empty;
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("prompt_tokens")] public long PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public long CompletionTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
    [JsonPropertyName("estimated_turn_count")] public int EstimatedTurnCount { get; set; }
}

public sealed class UsageActivityDayDto
{
    [JsonPropertyName("date")] public DateTime Date { get; set; }
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
}

public sealed class UsageDayDetailDto
{
    [JsonPropertyName("scope")] public string Scope { get; set; } = "me";
    [JsonPropertyName("can_view_all")] public bool CanViewAll { get; set; }
    [JsonPropertyName("date")] public DateTime Date { get; set; }
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("prompt_tokens")] public long PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public long CompletionTokens { get; set; }
    [JsonPropertyName("turn_count")] public int TurnCount { get; set; }
    [JsonPropertyName("providers")] public List<UsageProviderSummaryDto> Providers { get; set; } = [];
}
