using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.PromptTemplate;

public sealed class PromptTemplateVariableDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default_value")]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = [];
}

public sealed class PromptTemplateSaveRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("variables")]
    public List<PromptTemplateVariableDto>? Variables { get; set; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

public sealed class PromptTemplateUseRequest
{
    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("variables")]
    public Dictionary<string, string>? Variables { get; set; }
}

public sealed class PromptTemplateUserStateRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class PromptTemplateDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("variables")]
    public List<PromptTemplateVariableDto> Variables { get; set; } = [];

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = string.Empty;

    [JsonPropertyName("author_name")]
    public string AuthorName { get; set; } = string.Empty;

    [JsonPropertyName("created_by_me")]
    public bool CreatedByMe { get; set; }

    [JsonPropertyName("liked_by_me")]
    public bool LikedByMe { get; set; }

    [JsonPropertyName("favorited_by_me")]
    public bool FavoritedByMe { get; set; }

    [JsonPropertyName("like_count")]
    public int LikeCount { get; set; }

    [JsonPropertyName("use_count")]
    public int UseCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public sealed class PromptTemplateUseResult
{
    [JsonPropertyName("template")]
    public PromptTemplateDto Template { get; set; } = new();

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; set; }

    [JsonPropertyName("rendered_content")]
    public string RenderedContent { get; set; } = string.Empty;
}
