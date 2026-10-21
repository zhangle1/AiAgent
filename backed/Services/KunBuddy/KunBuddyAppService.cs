using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Dtos.KunBuddy;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.KunBuddy;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/kunbuddy")]
public sealed class KunBuddyAppService(KunBuddyGatewayService gateway, IAuthService auth, IHttpContextAccessor context, KunBuddySessionService sessions) : IDynamicApiController
{
    [HttpPost("sessions/{sessionId}/markdown")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadSession(string sessionId, [FromBody] KunBuddySessionUpload input, CancellationToken ct)
    {
        var user = await auth.TryGetKunBuddyUserAsync(context.HttpContext!, ct);
        if (user is null) return new UnauthorizedResult();
        if (!string.Equals(input.Account, user.Username, StringComparison.Ordinal)) return new StatusCodeResult(StatusCodes.Status403Forbidden);
        return new OkObjectResult(await sessions.UploadAsync(user, sessionId, input, ct));
    }

    [HttpGet("models")]
    public object Models() => gateway.Models();

    [HttpGet("profiles/{profileId}/models")]
    [HttpGet("profiles/{profileId}/v1/models")]
    public object ProfileModels(string profileId) => gateway.ModelsForProfile(profileId);

    [HttpPost("logout")]
    public async Task<object> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(context.HttpContext!, ct);
        return new { ok = true };
    }

    [HttpPost("profiles/{profileId}/chat/completions")]
    public Task Chat(string profileId, CancellationToken ct) => gateway.ForwardAsync(context.HttpContext!, profileId, "chat/completions", null, ct);

    [HttpPost("profiles/{profileId}/responses")]
    public Task Responses(string profileId, CancellationToken ct) => gateway.ForwardAsync(context.HttpContext!, profileId, "responses", null, ct);

    [HttpPost("profiles/{profileId}/v1/messages")]
    [HttpPost("profiles/{profileId}/messages")]
    public Task Messages(string profileId, CancellationToken ct) => gateway.ForwardAsync(context.HttpContext!, profileId, "messages", null, ct);

    [HttpPost("profiles/{profileId}/models/{modelId}:streamGenerateContent")]
    public Task GoogleStream(string profileId, string modelId, CancellationToken ct) => gateway.ForwardAsync(context.HttpContext!, profileId, "streamGenerateContent", modelId, ct);

    [HttpPost("profiles/{profileId}/models/{modelId}:generateContent")]
    public Task GoogleComplete(string profileId, string modelId, CancellationToken ct) => gateway.ForwardAsync(context.HttpContext!, profileId, "generateContent", modelId, ct);
}
