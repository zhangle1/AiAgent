using AiAgent.Backend.Dtos.Knowledge;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Knowledge;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/knowledge-resources/cleanup")]
public sealed class KnowledgeResourceCleanupAppService(KnowledgeResourceCleanupService cleanup) : IDynamicApiController
{
    [HttpGet("preview")]
    [ProducesResponseType(typeof(KnowledgeCleanupPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status499ClientClosedRequest)]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        var result = await cleanup.PreviewAsync(ct);
        return result is null ? new StatusCodeResult(StatusCodes.Status499ClientClosedRequest) : new OkObjectResult(result);
    }

    [HttpPost("")]
    [ProducesResponseType(typeof(KnowledgeCleanupResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status499ClientClosedRequest)]
    public async Task<IActionResult> Cleanup([FromBody] KnowledgeCleanupRequest request, CancellationToken ct)
    {
        var result = await cleanup.CleanupAsync(request, ct);
        return result is null ? new StatusCodeResult(StatusCodes.Status499ClientClosedRequest) : new OkObjectResult(result);
    }
}
