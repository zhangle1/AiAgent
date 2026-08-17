using SqlSugar;

namespace AiAgent.Backend.Entities.Push;

[SugarTable("ai_project_push_binding")]
public sealed class AiProjectPushBinding
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ProjectId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? PushChannelId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? TriggerType { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? TemplateCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? IsEnabled { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? IsDeleted { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastSentAt { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? LastStatus { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? LastErrorCode { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? CreatedBy { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}
