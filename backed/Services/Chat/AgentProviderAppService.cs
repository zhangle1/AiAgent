using AiAgent.Backend.Dtos.Chat;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Chat;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/agent-providers")]
public sealed class AgentProviderAppService : IDynamicApiController
{
    private readonly IAgentProviderEnvironmentService _environments;
    public AgentProviderAppService(IAgentProviderEnvironmentService environments) => _environments = environments;

    [HttpGet("environments")]
    public Task<List<AgentProviderEnvironmentDto>> GetEnvironments(CancellationToken cancellationToken) => _environments.GetEnvironmentsAsync(cancellationToken);
}
