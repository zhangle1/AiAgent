using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Chat;

public sealed class AgentProviderEnvironmentDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("chat_supported")]
    public bool ChatSupported { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class CodexModelOptionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("profile_name")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("supports_reasoning_effort")]
    public bool SupportsReasoningEffort { get; set; } = true;

    [JsonPropertyName("is_builtin")]
    public bool IsBuiltin { get; set; }
}

public sealed class CodexProfileModelDto
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("profile_name")]
    public string ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("supports_reasoning_effort")]
    public bool SupportsReasoningEffort { get; set; }
}

public sealed class CodexModelPolicyDto
{
    [JsonPropertyName("models")]
    public List<CodexModelOptionDto> Models { get; set; } = [];

    [JsonPropertyName("allowed_model_ids")]
    public List<string> AllowedModelIds { get; set; } = [];

    [JsonPropertyName("default_model_id")]
    public string DefaultModelId { get; set; } = string.Empty;

    [JsonPropertyName("allow_chat_model_override")]
    public bool AllowChatModelOverride { get; set; } = true;

    [JsonPropertyName("allowed_reasoning_efforts")]
    public List<string> AllowedReasoningEfforts { get; set; } = [];

    [JsonPropertyName("default_reasoning_effort")]
    public string DefaultReasoningEffort { get; set; } = "medium";

    [JsonPropertyName("allow_chat_reasoning_effort_override")]
    public bool AllowChatReasoningEffortOverride { get; set; } = true;

    [JsonPropertyName("profile_models")]
    public List<CodexProfileModelDto> ProfileModels { get; set; } = [];
}

public sealed class CodexModelPolicyUpdateRequest
{
    [JsonPropertyName("allowed_model_ids")]
    public List<string>? AllowedModelIds { get; set; }

    [JsonPropertyName("default_model_id")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("allow_chat_model_override")]
    public bool? AllowChatModelOverride { get; set; }

    [JsonPropertyName("allowed_reasoning_efforts")]
    public List<string>? AllowedReasoningEfforts { get; set; }

    [JsonPropertyName("default_reasoning_effort")]
    public string? DefaultReasoningEffort { get; set; }

    [JsonPropertyName("allow_chat_reasoning_effort_override")]
    public bool? AllowChatReasoningEffortOverride { get; set; }

    [JsonPropertyName("profile_models")]
    public List<CodexProfileModelDto>? ProfileModels { get; set; }
}
