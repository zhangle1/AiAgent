using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

/// <summary>
/// Durable, privacy-preserving claim for one native tool invocation. Arguments
/// and tool output deliberately never enter this table.
/// </summary>
[SugarTable("ai_agent_tool_claim")]
public sealed class AiAgentToolClaim
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? RunId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? UserId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SessionId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? TurnId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ToolCallId { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? InvocationFingerprint { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ToolName { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? StepNumber { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Status { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
