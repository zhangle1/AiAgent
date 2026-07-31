using SqlSugar;

namespace AiAgent.Backend.Entities.PromptTemplate;

[SugarTable("ai_prompt_template")]
public sealed class AiPromptTemplate
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 120)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 320)]
    public string Description { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string Stage { get; set; } = "development";

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string TagsJson { get; set; } = "[]";

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string Body { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string VariablesJson { get; set; } = "[]";

    [SugarColumn(IsNullable = true)]
    public long? CodeProjectId { get; set; }

    [SugarColumn(Length = 16)]
    public string Visibility { get; set; } = "personal";

    [SugarColumn(Length = 64)]
    public string CreatedBy { get; set; } = string.Empty;

    public int LikeCount { get; set; }
    public int UseCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
