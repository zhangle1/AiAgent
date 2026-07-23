using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Usage;
using AiAgent.Backend.Entities.Usage;
using AiAgent.Backend.Services.Auth;
using SqlSugar;

namespace AiAgent.Backend.Services.Usage;

public interface IUsageStatisticsService
{
    Task RecordAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatCompleteResponse response, CancellationToken cancellationToken);
    Task<UsageSummaryDto> GetSummaryAsync(AuthenticatedUser user, string? scope, int days, CancellationToken cancellationToken);
    Task<UsageDayDetailDto> GetDayDetailAsync(AuthenticatedUser user, string? scope, DateTime date, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the append-only usage ledger and aggregate read model.  A user id is
/// never accepted from the browser: the future all-user view is gated here.
/// </summary>
public sealed class UsageStatisticsService : IUsageStatisticsService
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<UsageStatisticsService> _logger;

    public UsageStatisticsService(ISqlSugarClient db, ILogger<UsageStatisticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task RecordAsync(AuthenticatedUser user, ChatCompleteRequest request, ChatCompleteResponse response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var usage = response.Usage ?? EstimateUsage(request.Message, response.Content);
        var promptTokens = Math.Max(0, usage.PromptTokens);
        var completionTokens = Math.Max(0, usage.CompletionTokens);
        var totalTokens = Math.Max(promptTokens + completionTokens, Math.Max(0, usage.TotalTokens));
        var providerId = string.IsNullOrWhiteSpace(request.Agent) ? "builtin" : request.Agent.Trim().ToLowerInvariant();
        var providerKind = providerId == "builtin" ? "builtin" : "third_party";

        try
        {
            _db.Insertable(new AiUsageRecord
            {
                UserId = user.Id,
                SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim(),
                ProviderKind = providerKind,
                ProviderId = providerId,
                ModelId = response.ModelId,
                ModelName = response.Model,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                IsEstimated = usage.IsEstimated,
                CreatedAt = DateTime.UtcNow
            }).ExecuteCommand();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Usage ledger write failed. UserId={UserId}, ProviderId={ProviderId}", user.Id, providerId);
        }
        return Task.CompletedTask;
    }

    public Task<UsageSummaryDto> GetSummaryAsync(AuthenticatedUser user, string? scope, int days, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canViewAll = CanViewAll(user);
        var requestedAll = string.Equals(scope?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
        var useAll = requestedAll && canViewAll;
        var periodDays = Math.Clamp(days, 7, 366);
        var to = DateTime.UtcNow.Date.AddDays(1);
        var from = to.AddDays(-periodDays);

        var query = _db.Queryable<AiUsageRecord>().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (!useAll) query = query.Where(item => item.UserId == user.Id);
        var rows = query.ToList();

        var activityByDay = rows
            .GroupBy(item => item.CreatedAt.Date)
            .ToDictionary(group => group.Key, group => new { Tokens = group.Sum(item => (long)item.TotalTokens), Turns = group.Count() });
        var activity = Enumerable.Range(0, periodDays)
            .Select(offset => from.AddDays(offset).Date)
            .Select(date => activityByDay.TryGetValue(date, out var value)
                ? new UsageActivityDayDto { Date = date, TotalTokens = value.Tokens, TurnCount = value.Turns }
                : new UsageActivityDayDto { Date = date })
            .ToList();

        return Task.FromResult(new UsageSummaryDto
        {
            Scope = useAll ? "all" : "me",
            CanViewAll = canViewAll,
            PeriodDays = periodDays,
            From = from,
            To = to,
            TotalTokens = rows.Sum(item => (long)item.TotalTokens),
            PromptTokens = rows.Sum(item => (long)item.PromptTokens),
            CompletionTokens = rows.Sum(item => (long)item.CompletionTokens),
            TurnCount = rows.Count,
            EstimatedTurnCount = rows.Count(item => item.IsEstimated),
            Providers = rows
                .GroupBy(item => new { item.ProviderKind, item.ProviderId, Model = item.ModelName ?? item.ModelId })
                .Select(group => new UsageProviderSummaryDto
                {
                    ProviderKind = group.Key.ProviderKind,
                    ProviderId = group.Key.ProviderId,
                    Model = group.Key.Model,
                    TotalTokens = group.Sum(item => (long)item.TotalTokens),
                    PromptTokens = group.Sum(item => (long)item.PromptTokens),
                    CompletionTokens = group.Sum(item => (long)item.CompletionTokens),
                    TurnCount = group.Count(),
                    EstimatedTurnCount = group.Count(item => item.IsEstimated)
                })
                .OrderByDescending(item => item.TotalTokens)
                .ThenBy(item => item.ProviderKind)
                .ToList(),
            Activity = activity
        });
    }

    public Task<UsageDayDetailDto> GetDayDetailAsync(AuthenticatedUser user, string? scope, DateTime date, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canViewAll = CanViewAll(user);
        var requestedAll = string.Equals(scope?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
        var useAll = requestedAll && canViewAll;
        var from = date.Date;
        var to = from.AddDays(1);
        var query = _db.Queryable<AiUsageRecord>().Where(item => item.CreatedAt >= from && item.CreatedAt < to);
        if (!useAll) query = query.Where(item => item.UserId == user.Id);
        var rows = query.ToList();

        return Task.FromResult(new UsageDayDetailDto
        {
            Scope = useAll ? "all" : "me",
            CanViewAll = canViewAll,
            Date = from,
            TotalTokens = rows.Sum(item => (long)item.TotalTokens),
            PromptTokens = rows.Sum(item => (long)item.PromptTokens),
            CompletionTokens = rows.Sum(item => (long)item.CompletionTokens),
            TurnCount = rows.Count,
            Providers = rows
                .GroupBy(item => new { item.ProviderKind, item.ProviderId, Model = item.ModelName ?? item.ModelId })
                .Select(group => new UsageProviderSummaryDto
                {
                    ProviderKind = group.Key.ProviderKind,
                    ProviderId = group.Key.ProviderId,
                    Model = group.Key.Model,
                    TotalTokens = group.Sum(item => (long)item.TotalTokens),
                    PromptTokens = group.Sum(item => (long)item.PromptTokens),
                    CompletionTokens = group.Sum(item => (long)item.CompletionTokens),
                    TurnCount = group.Count(),
                    EstimatedTurnCount = group.Count(item => item.IsEstimated)
                })
                .OrderByDescending(item => item.TotalTokens)
                .ThenBy(item => item.ProviderKind)
                .ToList()
        });
    }

    private static bool CanViewAll(AuthenticatedUser user) => user.IsAdministrator;

    private static ChatTokenUsage EstimateUsage(string prompt, string completion)
    {
        var promptTokens = EstimateTokens(prompt);
        var completionTokens = EstimateTokens(completion);
        return new ChatTokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            IsEstimated = true
        };
    }

    private static int EstimateTokens(string value) => string.IsNullOrWhiteSpace(value) ? 0 : Math.Max(1, (int)Math.Ceiling(value.Trim().Length / 3.6));
}
