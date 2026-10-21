using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Settings;

public sealed class ModelDiscoveryRequest
{
    [JsonPropertyName("service")] public string Service { get; set; } = "llm";
    [JsonPropertyName("profile_id")] public string? ProfileId { get; set; }
    [JsonPropertyName("binding")] public string Binding { get; set; } = "openai";
    [JsonPropertyName("api_style")] public string? ApiStyle { get; set; }
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("api_key")] public string? ApiKey { get; set; }
    [JsonPropertyName("extra_headers")] public Dictionary<string, string> ExtraHeaders { get; set; } = new();
}

public sealed class DiscoveredModelDto
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("context_window")] public int? ContextWindow { get; set; }
    [JsonPropertyName("max_output_tokens")] public int? MaxOutputTokens { get; set; }
    [JsonPropertyName("max_output_tokens_source")] public string? MaxOutputTokensSource { get; set; }
    [JsonPropertyName("supports_images")] public bool? SupportsImages { get; set; }
    [JsonPropertyName("supports_tools")] public bool? SupportsTools { get; set; }
    [JsonPropertyName("context_window_source")] public string? ContextWindowSource { get; set; }
}
