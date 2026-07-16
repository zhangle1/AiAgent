using SqlSugar;

namespace AiAgent.Backend.Entities.Settings;

[SugarTable("ai_model_diagnostic_run")]
public sealed class AiModelDiagnosticRun
{
    /// <summary>
    /// 诊断记录自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 服务类型。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 被测试的配置档 Id。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? ProfileId { get; set; }

    /// <summary>
    /// 被测试的模型 Id。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? ModelId { get; set; }

    /// <summary>
    /// Provider 编码。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ProviderCode { get; set; }

    /// <summary>
    /// Provider 侧模型编码。
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? ModelCode { get; set; }

    /// <summary>
    /// 诊断状态。
    /// </summary>
    [SugarColumn(Length = 32)]
    public string State { get; set; } = "NotRun";

    /// <summary>
    /// 诊断消息。
    /// </summary>
    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? Message { get; set; }

    /// <summary>
    /// 请求快照 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? RequestJson { get; set; }

    /// <summary>
    /// 响应快照 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? ResponseJson { get; set; }

    /// <summary>
    /// 诊断开始时间，使用 UTC。
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 诊断结束时间，使用 UTC。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// 创建人。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? CreatedBy { get; set; }
}