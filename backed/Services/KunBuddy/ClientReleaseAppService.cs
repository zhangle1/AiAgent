using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AiAgent.Backend.Services.KunBuddy;

public sealed class ClientReleaseAdminFilter(IAuthService auth) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = await auth.TryGetCurrentUserAsync(context.HttpContext, context.HttpContext.RequestAborted);
        if (user is null) context.Result = new UnauthorizedResult();
        else if (!user.IsAdministrator) context.Result = new StatusCodeResult(403);
    }
}

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/admin/client-releases")]
[TypeFilter(typeof(ClientReleaseAdminFilter))]
public sealed class ClientReleaseAdminAppService(ClientReleaseService releases, IAuthService auth, IHttpContextAccessor context) : IDynamicApiController
{
    [HttpGet("list")] public object List() => releases.List();
    [HttpPost("upload")]
    [RequestSizeLimit(ClientReleaseService.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ClientReleaseService.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string version, [FromForm] string platform, [FromForm] string architecture, [FromForm] string? notes, CancellationToken ct)
    {
        try { return new OkObjectResult(await releases.Upload((await auth.TryGetCurrentUserAsync(context.HttpContext!, ct))!, file, version, platform, architecture, notes, ct)); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
    }
    [HttpPost("{id}/withdraw")]
    public async Task<IActionResult> Withdraw(string id, CancellationToken ct)
    {
        try { await releases.Withdraw((await auth.TryGetCurrentUserAsync(context.HttpContext!, ct))!, id, ct); return new OkObjectResult(new { ok = true }); }
        catch (KeyNotFoundException) { return new NotFoundResult(); }
    }
    [HttpGet("{id}/file")] public IActionResult Download(string id) => ReleaseFile(releases, id, true);
    internal static IActionResult ReleaseFile(ClientReleaseService releases, string id, bool administrator)
    {
        try { var file = releases.Open(id, administrator); return new FileStreamResult(new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read), "application/octet-stream") { FileDownloadName = file.Release.Filename, EnableRangeProcessing = true }; }
        catch (KeyNotFoundException) { return new NotFoundResult(); }
    }
}

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/kunbuddy/releases")]
public sealed class ClientReleaseAppService(ClientReleaseService releases) : IDynamicApiController
{
    [HttpGet("latest")]
    public IActionResult Latest([FromQuery] string platform, [FromQuery] string architecture)
    {
        try { return new OkObjectResult(new { release = releases.Latest(platform, architecture) }); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }
    [HttpGet("{id}/file")] public IActionResult Download(string id) => ClientReleaseAdminAppService.ReleaseFile(releases, id, false);
}
