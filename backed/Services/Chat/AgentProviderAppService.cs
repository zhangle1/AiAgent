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
    private readonly IImageOcrPolicyService _imageOcrPolicy;
    private readonly IImageOcrService _imageOcr;
    private readonly IChatImageAttachmentService _attachments;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _auth;

    public AgentProviderAppService(
        IAgentProviderEnvironmentService environments,
        ICodexModelPolicyService codexModelPolicy,
        IImageOcrPolicyService imageOcrPolicy,
        IImageOcrService imageOcr,
        IChatImageAttachmentService attachments,
        IHttpContextAccessor httpContextAccessor,
        IAuthService auth)
    {
        _environments = environments;
        _codexModelPolicy = codexModelPolicy;
        _imageOcrPolicy = imageOcrPolicy;
        _imageOcr = imageOcr;
        _attachments = attachments;
        _httpContextAccessor = httpContextAccessor;
        _auth = auth;
    }

    [HttpGet("environments")]
    public Task<List<AgentProviderEnvironmentDto>> GetEnvironments(CancellationToken cancellationToken) => _environments.GetEnvironmentsAsync(cancellationToken);

    [HttpGet("codex-model-policy")]
    public CodexModelPolicyDto GetCodexModelPolicy() => _codexModelPolicy.GetPolicy();

    [HttpGet("image-ocr-policy")]
    public ImageOcrPolicyDto GetImageOcrPolicy() => _imageOcrPolicy.GetPolicy();

    [HttpPost("image-ocr-diagnostics")]
    public async Task<ImageOcrDiagnosticDto> DiagnoseImageOcr([FromBody] ImageOcrDiagnosticRequest request, CancellationToken cancellationToken)
    {
        var user = await RequireAdministratorAsync(cancellationToken);
        string? imagePath = null;
        if (!string.IsNullOrWhiteSpace(request.AttachmentId))
        {
            var attachments = await _attachments.ResolveLocalAttachmentsAsync(user, null, new[] { request.AttachmentId }, cancellationToken);
            imagePath = attachments.Single().LocalPath;
        }
        return await _imageOcr.DiagnoseAsync(imagePath, cancellationToken);
    }

    [HttpPut("codex-model-policy")]
    public async Task<CodexModelPolicyDto> UpdateCodexModelPolicy([FromBody] CodexModelPolicyUpdateRequest request, CancellationToken cancellationToken)
    {
        var user = await _auth.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        return _codexModelPolicy.UpdatePolicy(user, request);
    }

    [HttpPut("image-ocr-policy")]
    public async Task<ImageOcrPolicyDto> UpdateImageOcrPolicy([FromBody] ImageOcrPolicyUpdateRequest request, CancellationToken cancellationToken)
    {
        var user = await _auth.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        return _imageOcrPolicy.UpdatePolicy(user, request);
    }

    private async Task<AuthenticatedUser> RequireAdministratorAsync(CancellationToken cancellationToken)
    {
        var user = await _auth.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (!user.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        return user;
    }
}
