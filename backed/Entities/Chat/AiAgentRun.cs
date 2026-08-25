using SqlSugar;

namespace AiAgent.Backend.Entities.Chat;

[SugarTable("ai_agent_run")]
public sealed class AiAgentRun
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string RunId { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? UserId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SessionId { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? RuntimeKind { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? RuntimeVersion { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ProtocolVersion { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Status { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ModelId { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? PromptTokens { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? CompletionTokens { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? TotalTokens { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? ToolCalls { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? FileChanges { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ErrorCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAt { get; set; }
}

