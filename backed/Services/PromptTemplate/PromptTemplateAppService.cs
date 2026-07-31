using AiAgent.Backend.Dtos.PromptTemplate;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.PromptTemplate;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/prompt-templates")]
public sealed class PromptTemplateAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IPromptTemplateService _templates;

    public PromptTemplateAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IPromptTemplateService templates)
        => (_httpContextAccessor, _authService, _templates) = (httpContextAccessor, authService, templates);

    [HttpGet("list")]
    public async Task<object> List([FromQuery] string? stage, [FromQuery] string? q, CancellationToken cancellationToken)
        => new { templates = await _templates.ListAsync(await RequireUser(cancellationToken), stage, q, cancellationToken) };

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var template = await _templates.GetAsync(await RequireUser(cancellationToken), id, cancellationToken);
        return template is null ? new NotFoundObjectResult(new { message = "模板不存在或你没有访问权限。" }) : new OkObjectResult(template);
    }

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] PromptTemplateSaveRequest request, CancellationToken cancellationToken)
    {
        var result = await _templates.CreateAsync(await RequireUser(cancellationToken), request, cancellationToken);
        return result.Template is null ? new BadRequestObjectResult(new { message = result.Error ?? "创建模板失败。" }) : new OkObjectResult(result.Template);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] PromptTemplateSaveRequest request, CancellationToken cancellationToken)
    {
        var result = await _templates.UpdateAsync(await RequireUser(cancellationToken), id, request, cancellationToken);
        return result.Template is null ? new BadRequestObjectResult(new { message = result.Error ?? "保存模板失败。" }) : new OkObjectResult(result.Template);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
        => await _templates.DeleteAsync(await RequireUser(cancellationToken), id, cancellationToken) ? new OkObjectResult(new { ok = true }) : new NotFoundObjectResult(new { message = "模板不存在或你没有删除权限。" });

    [HttpPost("{id:long}/like")]
    public async Task<IActionResult> SetLiked(long id, [FromBody] PromptTemplateUserStateRequest request, CancellationToken cancellationToken)
    {
        var template = await _templates.SetLikedAsync(await RequireUser(cancellationToken), id, request.Enabled, cancellationToken);
        return template is null ? new NotFoundObjectResult(new { message = "模板不存在或你没有访问权限。" }) : new OkObjectResult(template);
    }

    [HttpPost("{id:long}/favorite")]
    public async Task<IActionResult> SetFavorited(long id, [FromBody] PromptTemplateUserStateRequest request, CancellationToken cancellationToken)
    {
        var template = await _templates.SetFavoritedAsync(await RequireUser(cancellationToken), id, request.Enabled, cancellationToken);
        return template is null ? new NotFoundObjectResult(new { message = "模板不存在或你没有访问权限。" }) : new OkObjectResult(template);
    }

    [HttpPost("{id:long}/use")]
    public async Task<IActionResult> Use(long id, [FromBody] PromptTemplateUseRequest request, CancellationToken cancellationToken)
    {
        var result = await _templates.UseAsync(await RequireUser(cancellationToken), id, request, cancellationToken);
        return result.Result is null ? new BadRequestObjectResult(new { message = result.Error ?? "使用模板失败。" }) : new OkObjectResult(result.Result);
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken)
        => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
}
