using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Git;

public sealed class GiteeOAuthAuthorizeResponse
{
    [JsonPropertyName("authorize_url")] public string AuthorizeUrl { get; set; } = string.Empty;
}

public sealed class GiteeOAuthUser
{
    [JsonPropertyName("login")] public string? Login { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}

public sealed class GiteeOAuthTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
}
