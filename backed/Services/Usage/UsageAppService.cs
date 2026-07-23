using AiAgent.Backend.Dtos.Usage;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace AiAgent.Backend.Services.Usage;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/usage")]
public sealed class UsageAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IUsageStatisticsService _usage;

    public UsageAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService, IUsageStatisticsService usage)
        => (_httpContextAccessor, _authService, _usage) = (httpContextAccessor, authService, usage);

    /// <summary>
    /// Returns usage for the current user. Scope=all is reserved for configured administrators.
    /// </summary>
    [HttpGet("summary")]
    public async Task<UsageSummaryDto> Summary([FromQuery] string? scope, [FromQuery] int days = 365, CancellationToken cancellationToken = default)
        => await _usage.GetSummaryAsync(await RequireUser(cancellationToken), scope, days, cancellationToken);

    /// <summary>
    /// Returns provider/model token usage for one UTC day. Scope=all follows the same administrator gate as the summary endpoint.
    /// </summary>
    [HttpGet("days/{date}")]
    public async Task<UsageDayDetailDto> DayDetail([FromRoute] string date, [FromQuery] string? scope, CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            throw new ArgumentException("Date must use yyyy-MM-dd format.", nameof(date));
        }
        return await _usage.GetDayDetailAsync(await RequireUser(cancellationToken), scope, parsed, cancellationToken);
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken)
        => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
}
