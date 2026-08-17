using SqlSugar;

namespace AiAgent.Backend.Entities.Push;

[SugarTable("ai_push_channel")]
public sealed class AiPushChannel
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 80, IsNullable = true)]
    public string? Name { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ProviderType { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? AccessTokenProtected { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? SignSecretProtected { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? DingTalkRobotCode { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? StreamClientId { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? StreamClientSecretProtected { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? StreamEnabled { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? StreamStatus { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? StreamLastErrorCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StreamConnectedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StreamLastFrameAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StreamLastCallbackAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? StreamReconnectAttempt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StreamNextReconnectAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? IsEnabled { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? IsDeleted { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastTestedAt { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? LastTestStatus { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? LastErrorCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeletedAt { get; set; }
}
