using AiAgent.Backend.Dtos.Knowledge;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Knowledge;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/knowledge-resources")]
public sealed class KnowledgeResourceAppService(KnowledgeResourceService resources) : IDynamicApiController
{
    [HttpGet("tree")]
    public List<KnowledgeResourceNodeDto> Tree() => resources.Tree();
    [HttpGet("processing")]
    public List<KnowledgeResourceProcessingDto> Processing() => resources.Processing();
    [HttpGet("read")]
    public Task<KnowledgeResourceReadDto> Read([FromQuery] string uri, CancellationToken ct) => resources.ReadAsync(uri, ct);
    [HttpPost("directories")]
    public Task<KnowledgeResourceNodeDto> CreateDirectory([FromBody] KnowledgeDirectoryRequest request, CancellationToken ct) => resources.CreateDirectoryAsync(request, ct);
    [HttpPost("upload")]
    public Task<KnowledgeResourceImportDto> Upload([FromForm] KnowledgeResourceUploadRequest request, CancellationToken ct) => resources.UploadAsync(request, ct);
    [HttpPost("parse")]
    public KnowledgeCompilationJobDto Parse([FromBody] KnowledgeResourceRequest request) => resources.Parse(request.Uri);
    [HttpGet("task")]
    public KnowledgeCompilationJobDto? TaskStatus([FromQuery] string uri) => resources.TaskStatus(uri);
    [HttpPost("tasks/{id:long}/cancel")]
    public KnowledgeCompilationJobDto Cancel([FromRoute] long id, [FromBody] KnowledgeResourceRequest request) => resources.Cancel(request.Uri, id);
    [HttpPost("ask")]
    public Task<KnowledgeResourceAnswerDto> Ask([FromBody] KnowledgeResourceRequest request, CancellationToken ct) => resources.AskAsync(request, ct);
    [HttpGet("file")]
    public IActionResult File([FromQuery] string uri, [FromQuery] bool download = false)
    {
        var file = resources.File(uri);
        return new PhysicalFileResult(file.Path, file.ContentType) { FileDownloadName = download ? file.Name : null, EnableRangeProcessing = true };
    }
}
