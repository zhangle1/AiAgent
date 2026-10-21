using AiAgent.Backend.Models.Settings;
using AiAgent.Backend.Services.Settings;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace AiAgent.Backend.Services.KunBuddy;

/// <summary>Only configured models are routable. Upstream credentials never leave the server.</summary>
public sealed class KunBuddyGatewayService(IModelCatalogService catalog, IHttpClientFactory clients)
{
    public object Models()
    {
        var llm = catalog.Load(redactSecrets: true).Services.Llm;
        return new { profiles = llm.Profiles.Where(profile => profile.Models.Any(model => !string.IsNullOrWhiteSpace(model.Model))).Select(profile => new
        {
            id = profile.Id, name = profile.Name, api_style = ApiStyle(profile.Binding, profile.ApiStyle),
            models = profile.Models.Where(model => !string.IsNullOrWhiteSpace(model.Model)).Select(model => new
            {
                id = model.Id, name = model.Name, context_window = model.ContextWindow,
                max_output_tokens = model.MaxOutputTokens, supports_images = model.SupportsImages,
                supports_tools = model.SupportsTools ?? model.SupportsNativeToolCalling,
                is_default = model.Id == llm.ActiveModelId
            })
        }) };
    }

    internal static string ApiStyle(string? binding, string? configured = null) => configured is "chat_completions" or "responses" or "anthropic_messages" or "google_generative_ai" ? configured : binding?.ToLowerInvariant() switch
    {
        "anthropic" or "claude" => "anthropic_messages",
        "google" or "gemini" => "google_generative_ai",
        _ => "chat_completions"
    };

    public object ModelsForProfile(string profileId)
    {
        var profile = catalog.Load(redactSecrets: true).Services.Llm.Profiles.FirstOrDefault(item => item.Id == profileId)
            ?? throw new KeyNotFoundException("The model profile is unavailable.");
        return new
        {
            data = profile.Models.Where(model => !string.IsNullOrWhiteSpace(model.Model)).Select(model => new { id = model.Id, display_name = model.Name, context_window = Capacity(model.ContextWindow), max_output_tokens = Capacity(model.MaxOutputTokens) }),
            models = profile.Models.Where(model => !string.IsNullOrWhiteSpace(model.Model)).Select(model => new { name = "models/" + model.Id, displayName = model.Name, inputTokenLimit = Capacity(model.ContextWindow), outputTokenLimit = Capacity(model.MaxOutputTokens) })
        };
    }

    private static int? Capacity(string? value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    public async Task ForwardAsync(HttpContext context, string profileId, string operation, string? routeModel, CancellationToken ct)
    {
        var profile = catalog.Load().Services.Llm.Profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is null) { context.Response.StatusCode = 404; return; }
        var style = ApiStyle(profile.Binding, profile.ApiStyle);
        var valid = style switch
        {
            "anthropic_messages" => operation == "messages",
            "google_generative_ai" => operation is "generateContent" or "streamGenerateContent",
            "responses" => operation == "responses",
            _ => operation == "chat/completions"
        };
        if (!valid) { context.Response.StatusCode = 400; return; }
        using var source = new StreamContent(context.Request.Body);
        var bytes = await ModelDiscoveryService.ReadBoundedAsync(source, 20 * 1024 * 1024, ct);
        var body = JsonNode.Parse(bytes) as JsonObject ?? throw new ArgumentException("A JSON object is required.");
        var requestedModel = routeModel ?? body["model"]?.GetValue<string>();
        var model = profile.Models.FirstOrDefault(item => item.Id == requestedModel);
        if (model is null || string.IsNullOrWhiteSpace(model.Model)) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = new { message = "The requested model is not in the authorized catalog." } }, ct); return; }
        var root = ModelDiscoveryService.ValidateEndpoint(profile.BaseUrl).AbsoluteUri.TrimEnd('/');
        string endpoint;
        if (style == "anthropic_messages")
        {
            if (root.EndsWith("/messages")) root = root[..^9];
            endpoint = root.EndsWith("/v1") ? root + "/messages" : root + "/v1/messages";
        }
        else if (style == "google_generative_ai")
        {
            if (!root.EndsWith("/v1") && !root.EndsWith("/v1beta")) root += "/v1beta";
            endpoint = root + "/models/" + Uri.EscapeDataString(model.Model) + ":" + operation + (operation == "streamGenerateContent" ? "?alt=sse" : "");
        }
        else if (style == "responses")
        {
            if (root.EndsWith("/chat/completions")) root = root[..^17];
            endpoint = root.EndsWith("/responses") ? root : root + "/responses";
        }
        else
        {
            if (profile.Binding?.Equals("ollama", StringComparison.OrdinalIgnoreCase) == true && !root.EndsWith("/v1")) root += "/v1";
            endpoint = root.EndsWith("/chat/completions") ? root : root + "/chat/completions";
        }
        if (routeModel is null) body["model"] = model.Model;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ModelDiscoveryService.ApplyHeaders(request, style == "google_generative_ai" ? "google" : style == "anthropic_messages" ? "anthropic" : style, profile.ApiKey, profile.ExtraHeaders);
        foreach (var name in new[] { "anthropic-beta", "anthropic-version", "openai-beta" })
        {
            var value = context.Request.Headers[name].ToString();
            if (profile.ExtraHeaders.Keys.Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            if (!string.IsNullOrEmpty(value) && value.Length <= 4096 && !value.Any(character => character is '\r' or '\n'))
            { request.Headers.Remove(name); request.Headers.TryAddWithoutValidation(name, value); }
        }
        if (style == "anthropic_messages" && !string.IsNullOrWhiteSpace(profile.ApiVersion))
        {
            if (profile.ApiVersion.Length > 128 || profile.ApiVersion.Any(char.IsWhiteSpace)) throw new ArgumentException("Invalid provider API version.");
            request.Headers.Remove("anthropic-version"); request.Headers.TryAddWithoutValidation("anthropic-version", profile.ApiVersion);
        }
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        using var client = clients.CreateClient("kunbuddy-gateway");
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            // Provider bodies can echo credentials or prompt content; return only a classification.
            context.Response.StatusCode = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? 502 : (int)response.StatusCode;
            await context.Response.WriteAsJsonAsync(new { error = new { message = $"AiAgent upstream model returned HTTP {(int)response.StatusCode}.", type = "upstream_error" } }, ct);
            return;
        }
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
}
