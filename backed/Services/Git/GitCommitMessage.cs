namespace AiAgent.Backend.Services.Git;

/// <summary>
/// Applies the platform user's identity to commits created by the server-side Git boundary.
/// </summary>
public static class GitCommitMessage
{
    public static string WithAuthor(string? message, string? username)
    {
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "chore: update" : message.Trim();
        if (normalizedMessage.Length > 200) normalizedMessage = normalizedMessage[..200];
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername.Length == 0) return normalizedMessage;

        var suffix = $"by {normalizedUsername}";
        return normalizedMessage.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalizedMessage
            : $"{normalizedMessage}\n\n{suffix}";
    }

    private static string NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return string.Empty;
        var normalized = username.Trim();
        if (normalized.Length > 128) normalized = normalized[..128];
        return new string(normalized.Where(character => !char.IsControl(character)).ToArray()).Trim();
    }
}
