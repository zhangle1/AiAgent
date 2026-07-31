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
}

public sealed class CodexModelPolicyUpdateRequest
{
    [JsonPropertyName("allowed_model_ids")]
    public List<string>? AllowedModelIds { get; set; }

    [JsonPropertyName("default_model_id")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("allow_chat_model_override")]
    public bool? AllowChatModelOverride { get; set; }
}
