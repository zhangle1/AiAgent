using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Auth;

public sealed class LoginRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public sealed class AuthStatusResponse
{
    [JsonPropertyName("authenticated")] public bool Authenticated { get; set; }
    [JsonPropertyName("user_id")] public string? UserId { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
}
