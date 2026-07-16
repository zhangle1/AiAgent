using SqlSugar;

namespace AiAgent.Backend.Entities.Settings;

[SugarTable("ai_setting_snapshot")]
public sealed class AiSettingSnapshot
{
    /// <summary>
    /// 快照自增主键。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 设置键，例如 ui。
    /// </summary>
    [SugarColumn(Length = 128)]
    public string SettingKey { get; set; } = string.Empty;

    /// <summary>
    /// 设置内容 JSON。
    /// </summary>
    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// 版本号。
    /// </summary>
    public int VersionNo { get; set; } = 1;

    /// <summary>
    /// 应用时间，使用 UTC。
    /// </summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 应用人。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? AppliedBy { get; set; }

    /// <summary>
    /// 备注。
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? Remark { get; set; }
}