using AiAgent.Backend.Dtos.Settings;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using AiAgent.Backend.Services.Auth;

namespace AiAgent.Backend.Services.Settings;

[DynamicApiController]
[ApiDescriptionSettings("Settings", KeepName = true)]
[Route("api/v1/settings/model-discovery")]
public sealed class ModelDiscoveryAppService(ModelDiscoveryService discovery, ModelMetadataStore metadata,
    IAuthService auth, IHttpContextAccessor context) : IDynamicApiController
{
    [HttpPost("")]
    public async Task<IActionResult> Discover([FromBody] ModelDiscoveryRequest input, CancellationToken ct)
    {
        if ((await auth.TryGetCurrentUserAsync(context.HttpContext!, ct))?.IsAdministrator != true) return new StatusCodeResult(StatusCodes.Status403Forbidden);
        return new OkObjectResult(new { models = await discovery.DiscoverAsync(input, ct), catalog_path = metadata.CatalogPath });
    }

    [HttpGet("status")]
    public object Status()
    {
        metadata.EnsureLoaded();
        return new { catalog_path = metadata.CatalogPath, source = "models.dev", credentials_included = false };
    }
}
