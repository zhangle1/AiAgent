using AiAgent.Backend.Dtos.TaskCenter;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.TaskCenter;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/task-center")]
public sealed class TaskCenterAppService(ITaskCenterService tasks) : IDynamicApiController
{
    [HttpGet("list")]
    public TaskCenterResponseDto List([FromQuery] string? domain = null, [FromQuery] string? status = null, [FromQuery] int limit = 100)
        => tasks.List(domain, status, limit);

    [HttpPost("{domain}/{id:long}/cancel")]
    public TaskCenterTaskDto Cancel([FromRoute] string domain, [FromRoute] long id) => tasks.Cancel(domain, id);

    [HttpPost("{domain}/{id:long}/retry")]
    public TaskCenterTaskDto Retry([FromRoute] string domain, [FromRoute] long id) => tasks.Retry(domain, id);
}
