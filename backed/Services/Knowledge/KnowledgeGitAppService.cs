using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Git;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Knowledge;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/knowledge/team-git")]
public sealed class KnowledgeGitAppService(KnowledgeTeamGitService service) : IDynamicApiController
{
    [HttpGet("")] public Task<KnowledgeGitState> State(CancellationToken ct) => service.StateAsync(ct);
    [HttpPut("")] public Task<KnowledgeGitConfig> Configure([FromBody] KnowledgeGitConfig request, CancellationToken ct) => service.ConfigureAsync(request, ct);
    [HttpPost("refresh")] public Task<GitDeliveryValidation> Refresh([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] KnowledgeGitRefresh? request, CancellationToken ct)
        => service.RefreshAsync(request ?? new(), ct);
    [HttpPost("push")] public Task<GitOperationResult> Push([FromBody] KnowledgeGitPush request, CancellationToken ct) => service.PushAsync(request, ct);
}
