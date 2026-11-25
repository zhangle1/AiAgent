using System.Text.Json.Serialization;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Ontology;

public sealed record CreateOntologyRequest([property: JsonPropertyName("name")] string Name);
public sealed record RenameOntologyRequest(int Version, string Name);
public sealed record SaveOntologyRequest(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("graph")] OntologyGraph? Graph,
    [property: JsonPropertyName("connection")] OntologyConnection? Connection,
    [property: JsonPropertyName("password")] string? Password);

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/ontology/{projectId:long}")]
public sealed class OntologyAppService(OntologyStore store, OntologySources sources, IAuthService auth, IProjectAccessService access, IHttpContextAccessor context) : IDynamicApiController
{
    private Task<IActionResult> Run(long projectId, Func<string, object> operation, CancellationToken token) =>
        RunAsync(projectId, user => Task.FromResult(operation(user)), token);
    private async Task<IActionResult> RunAsync(long projectId, Func<string, Task<object>> operation, CancellationToken token)
    {
        var user = await auth.TryGetCurrentUserAsync(context.HttpContext!, token);
        if (user == null) return new UnauthorizedResult();
        if (!access.CanAccess(user, projectId)) return new ForbidResult();
        context.HttpContext!.Response.Headers.CacheControl = "no-store";
        try { return new OkObjectResult(await operation(user.Id)); }
        catch (ArgumentException e) { return new BadRequestObjectResult(new { message = e.Message }); }
        catch (KeyNotFoundException) { return new NotFoundResult(); }
        catch (InvalidOperationException e) { return new ConflictObjectResult(new { message = e.Message }); }
    }
    [HttpGet("plugins")] public Task<IActionResult> Plugins(long projectId, CancellationToken token) => Run(projectId, _ => OntologyStore.Plugins, token);
    [HttpPost("models/{id}/sources/test")] public Task<IActionResult> TestSource(long projectId, string id, [FromBody] SourceRequest request, CancellationToken token) =>
        RunAsync(projectId, user => sources.Verify(user, projectId, id, request, false, token), token);
    [HttpPut("models/{id}/sources")] public Task<IActionResult> SaveSource(long projectId, string id, [FromBody] SourceRequest request, CancellationToken token) =>
        RunAsync(projectId, user => sources.Verify(user, projectId, id, request, true, token), token);
    [HttpPost("models/{id}/lookup")] public Task<IActionResult> Lookup(long projectId, string id, [FromBody] LookupRequest request, CancellationToken token) =>
        RunAsync(projectId, user => sources.Lookup(user, projectId, id, request, token), token);
    [HttpDelete("models/{id}/sources/{sourceId}")] public Task<IActionResult> RemoveSource(long projectId, string id, string sourceId, [FromQuery] int version, CancellationToken token) =>
        Run(projectId, user => store.RemoveSource(user, projectId, id, sourceId, version), token);
    [HttpGet("models")] public Task<IActionResult> List(long projectId, CancellationToken token) => Run(projectId, user => store.List(user, projectId), token);
    [HttpPut("models/{id}/name")] public Task<IActionResult> Rename(long projectId, string id, [FromBody] RenameOntologyRequest request, CancellationToken token) =>
        Run(projectId, user => store.Rename(user, projectId, id, request.Version, request.Name), token);
    [HttpDelete("models/{id}")] public Task<IActionResult> Delete(long projectId, string id, [FromQuery] int version, CancellationToken token) =>
        Run(projectId, user => store.Delete(user, projectId, id, version), token);
    [HttpPost("models")] public Task<IActionResult> Create(long projectId, [FromBody] CreateOntologyRequest request, CancellationToken token) => Run(projectId, user => store.Create(user, projectId, request.Name), token);
    [HttpGet("models/{id}")] public Task<IActionResult> Get(long projectId, string id, CancellationToken token) => Run(projectId, user => store.View(store.Get(user, projectId, id)), token);
    [HttpPut("models/{id}/graph")] public Task<IActionResult> Graph(long projectId, string id, [FromBody] SaveOntologyRequest request, CancellationToken token) => Run(projectId, user => request.Graph == null ? throw new ArgumentException("缺少流程。") : store.Update(user, projectId, id, request.Version, request.Graph, null, null), token);
    [HttpPut("models/{id}/connection")] public Task<IActionResult> Connection(long projectId, string id, [FromBody] SaveOntologyRequest request, CancellationToken token) => Run(projectId, user => request.Connection == null ? throw new ArgumentException("缺少连接。") : store.Update(user, projectId, id, request.Version, request.Graph, request.Connection, request.Password), token);
    [HttpGet("models/{id}/context")] public Task<IActionResult> Context(long projectId, string id, CancellationToken token) => Run(projectId, user => store.Context(user, projectId, id), token);
}
