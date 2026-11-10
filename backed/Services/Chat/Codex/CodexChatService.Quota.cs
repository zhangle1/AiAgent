using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Chat;

public sealed record CodexQuotaWindow(
    [property: JsonPropertyName("used_percent")] double UsedPercent,
    [property: JsonPropertyName("window_minutes")] int? WindowMinutes,
    [property: JsonPropertyName("resets_at")] long? ResetsAt);

public sealed record CodexQuotaResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("plan")] string? Plan,
    [property: JsonPropertyName("primary")] CodexQuotaWindow? Primary,
    [property: JsonPropertyName("secondary")] CodexQuotaWindow? Secondary,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("next_refresh_at")] DateTimeOffset NextRefreshAt,
    [property: JsonPropertyName("account_hint")] string? AccountHint = null);

public sealed partial class CodexChatService
{
    private readonly SemaphoreSlim _quotaLock = new(1, 1);
    private CodexQuotaResponse? _quotaCache;
    private string? _quotaCommand;

    public async Task<CodexQuotaResponse> ReadQuotaAsync(CancellationToken cancellationToken)
    {
        await _quotaLock.WaitAsync(cancellationToken);
        try
        {
            var command = ResolveCodexCommand();
            if (_quotaCache is { } cached && cached.NextRefreshAt > DateTimeOffset.UtcNow && _quotaCommand == command)
                return cached;
            // A dedicated worker never starts a thread/turn or shares the chat stdout reader.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            string? accountHint = null;
            try
            {
                using var worker = await CodexAppServerWorker.CreateAsync(command, AppContext.BaseDirectory, null,
                    "read-only", TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(25), linked.Token);
                var account = await worker.ReadAccountAsync("account/read", linked.Token);
                if (!account.TryGetProperty("account", out var identity) || identity.ValueKind != JsonValueKind.Object)
                    return Save("not_logged_in", null, null, null, 30);
                if (ReadString(identity, "type") != "chatgpt")
                    return Save("unsupported", null, null, null, 30);
                var email = ReadString(identity, "email");
                var at = email?.IndexOf('@') ?? -1;
                if (at > 0 && email!.Length <= 254) accountHint = email[..1] + "***" + email[at..];
                var result = await worker.ReadAccountAsync("account/rateLimits/read", linked.Token);
                var limits = result.GetProperty("rateLimits");
                return Save("available", ReadString(limits, "planType"), Window(limits, "primary"), Window(limits, "secondary"), 30);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Do not expose CLI errors, account identifiers, paths or credentials.
                return Save("unavailable", null, null, null, 1);
            }

            CodexQuotaResponse Save(string status, string? plan, CodexQuotaWindow? primary, CodexQuotaWindow? secondary, int minutes)
            {
                _quotaCommand = command;
                return _quotaCache = new(status, plan, primary, secondary, now, now.AddMinutes(minutes), accountHint);
            }
        }
        finally { _quotaLock.Release(); }
    }

    private static CodexQuotaWindow? Window(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var used) || !used.TryGetDouble(out var percent) || !double.IsFinite(percent)) return null;
        return new(Math.Clamp(percent, 0, 100),
            window.TryGetProperty("windowDurationMins", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out var minutes) ? minutes : null,
            window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var seconds) ? seconds : null);
    }
}
