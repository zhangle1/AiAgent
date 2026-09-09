using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.CodeRepository;

public sealed class MaintenanceSettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("interval_hours"), Range(1, 168)] public int IntervalHours { get; set; } = 24;
    [JsonPropertyName("mode"), RegularExpression("^(analyze|optimize)$")] public string Mode { get; set; } = "analyze";
    [JsonPropertyName("auto_push")] public bool AutoPush { get; set; }
    [JsonPropertyName("instructions"), StringLength(4000)] public string Instructions { get; set; } = "理解代码结构，识别可验证的问题，优先修复小范围缺陷并补充必要测试。";
    [JsonPropertyName("model_id"), StringLength(128)] public string? ModelId { get; set; }
    [JsonPropertyName("timeout_minutes"), Range(5, 120)] public int TimeoutMinutes { get; set; } = 30;
    [JsonPropertyName("max_files"), Range(1, 100)] public int MaxFiles { get; set; } = 15;
}

public sealed record MaintenancePlanDto(
    [property: JsonPropertyName("project_id")] long ProjectId,
    [property: JsonPropertyName("settings")] MaintenanceSettings Settings,
    [property: JsonPropertyName("next_run_at")] DateTime? NextRunAt,
    [property: JsonPropertyName("active_run_id")] string? ActiveRunId);

public sealed record MaintenanceRunDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("project_id")] long? ProjectId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("trigger")] string? Trigger,
    [property: JsonPropertyName("report")] string? Report,
    [property: JsonPropertyName("log")] string? Log,
    [property: JsonPropertyName("change_set_id")] long? ChangeSetId,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
    [property: JsonPropertyName("finished_at")] DateTime? FinishedAt);
