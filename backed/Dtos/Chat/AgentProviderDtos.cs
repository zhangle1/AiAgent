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
