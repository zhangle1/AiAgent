using SqlSugar;

namespace AiAgent.Backend.Entities.Usage;

/// <summary>
/// Immutable token-usage ledger for one successfully completed chat turn.
/// </summary>
[SugarTable("ai_usage_record")]
public sealed class AiUsageRecord
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserId { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SessionId { get; set; }

    [SugarColumn(Length = 32)]
    public string ProviderKind { get; set; } = "builtin";

    [SugarColumn(Length = 64)]
    public string ProviderId { get; set; } = "builtin";

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ModelId { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? ModelName { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public bool IsEstimated { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
