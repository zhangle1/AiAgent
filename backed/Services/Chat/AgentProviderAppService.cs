using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Chat;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/agent-providers")]
public sealed class AgentProviderAppService : IDynamicApiController
{
    private readonly IAgentProviderEnvironmentService _environments;
    private readonly ICodexModelPolicyService _codexModelPolicy;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _auth;

    public AgentProviderAppService(
        IAgentProviderEnvironmentService environments,
        ICodexModelPolicyService codexModelPolicy,
        IHttpContextAccessor httpContextAccessor,
        IAuthService auth)
    {
        _environments = environments;
        _codexModelPolicy = codexModelPolicy;
        _httpContextAccessor = httpContextAccessor;
        _auth = auth;
    }

    [HttpGet("environments")]
    public Task<List<AgentProviderEnvironmentDto>> GetEnvironments(CancellationToken cancellationToken) => _environments.GetEnvironmentsAsync(cancellationToken);

    [HttpGet("codex-model-policy")]
    public CodexModelPolicyDto GetCodexModelPolicy() => _codexModelPolicy.GetPolicy();

    [HttpPut("codex-model-policy")]
    public async Task<CodexModelPolicyDto> UpdateCodexModelPolicy([FromBody] CodexModelPolicyUpdateRequest request, CancellationToken cancellationToken)
    {
        var user = await _auth.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        return _codexModelPolicy.UpdatePolicy(user, request);
    }
}
