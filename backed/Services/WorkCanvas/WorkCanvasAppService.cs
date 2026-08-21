using AiAgent.Backend.Dtos.WorkCanvas;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.WorkCanvas;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/work-canvases")]
public sealed class WorkCanvasAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _http;
    private readonly IAuthService _auth;
    private readonly IWorkCanvasService _canvases;
    public WorkCanvasAppService(IHttpContextAccessor http, IAuthService auth, IWorkCanvasService canvases) => (_http, _auth, _canvases) = (http, auth, canvases);

    [HttpGet("")] public async Task<object> List(CancellationToken token) => new { canvases = _canvases.List(await User(token)) };
    [HttpPost("")] public async Task<WorkCanvasSnapshotDto> Create([FromBody] CreateWorkCanvasRequest request, CancellationToken token) => _canvases.Create(await User(token), request);
    [HttpGet("{canvasId}/snapshot")] public async Task<IActionResult> Get(string canvasId, CancellationToken token) => _canvases.Get(await User(token), canvasId) is { } value ? new OkObjectResult(value) : new NotFoundObjectResult(new { message = "工作画布不存在。" });
    [HttpPatch("{canvasId}")] public async Task<IActionResult> Update(string canvasId, [FromBody] UpdateWorkCanvasRequest request, CancellationToken token) => _canvases.Update(await User(token), canvasId, request) ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = "工作画布不存在或参数无效。" });
    [HttpPost("{canvasId}/nodes")] public async Task<IActionResult> AddNode(string canvasId, [FromBody] AddWorkCanvasNodeRequest request, CancellationToken token) => _canvases.AddNode(await User(token), canvasId, request) is { } value ? new OkObjectResult(value) : new BadRequestObjectResult(new { message = "会话不存在、无权访问或画布无效。" });
    [HttpDelete("{canvasId}/nodes/{nodeId}")] public async Task<IActionResult> RemoveNode(string canvasId, string nodeId, CancellationToken token) => _canvases.RemoveNode(await User(token), canvasId, nodeId) ? new OkObjectResult(new { removed = true }) : new NotFoundObjectResult(new { message = "节点不存在。" });
    [HttpPatch("{canvasId}/layout")] public async Task<IActionResult> Layout(string canvasId, [FromBody] UpdateWorkCanvasLayoutRequest request, CancellationToken token) => _canvases.UpdateLayout(await User(token), canvasId, request) is { } version ? new OkObjectResult(new { version }) : new ConflictObjectResult(new { message = "画布已被更新，请刷新后重试。" });
    [HttpPost("{canvasId}/delivery-links")] public async Task<IActionResult> AddLink(string canvasId, [FromBody] CreateDeliveryLinkRequest request, CancellationToken token) => _canvases.AddLink(await User(token), canvasId, request) is { } value ? new OkObjectResult(value) : new BadRequestObjectResult(new { message = "无法建立投递关系，请检查两端会话。" });
    [HttpDelete("{canvasId}/delivery-links/{linkId}")] public async Task<IActionResult> RemoveLink(string canvasId, string linkId, CancellationToken token) => _canvases.RemoveLink(await User(token), canvasId, linkId) ? new OkObjectResult(new { removed = true }) : new NotFoundObjectResult(new { message = "投递关系不存在。" });
    [HttpPost("deliveries")] public async Task<IActionResult> SendDelivery([FromBody] CreateCanvasDeliveryRequest request, CancellationToken token) => _canvases.SendDelivery(await User(token), request) is { } value ? new OkObjectResult(value) : new BadRequestObjectResult(new { message = "投递内容或目标无效。" });
    [HttpGet("sessions/{sessionId}/inbox")] public async Task<IActionResult> Inbox(string sessionId, CancellationToken token) => _canvases.Inbox(await User(token), sessionId) is { } value ? new OkObjectResult(new { deliveries = value }) : new NotFoundObjectResult(new { message = "会话不存在。" });
    [HttpPost("deliveries/{deliveryId}/decision")] public async Task<IActionResult> Decide(string deliveryId, [FromBody] DecideCanvasDeliveryRequest request, CancellationToken token) => _canvases.DecideDelivery(await User(token), deliveryId, request) ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = "投递不存在或决定无效。" });
    private async Task<AuthenticatedUser> User(CancellationToken token) => await _auth.TryGetCurrentUserAsync(_http.HttpContext!, token) ?? throw new UnauthorizedAccessException();
}
