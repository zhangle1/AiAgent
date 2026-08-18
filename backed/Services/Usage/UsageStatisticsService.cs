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
        var totals = query.Select(item => new UsageTotalsRow
        {
            TotalTokens = SqlFunc.AggregateSum(item.TotalTokens),
            PromptTokens = SqlFunc.AggregateSum(item.PromptTokens),
            CompletionTokens = SqlFunc.AggregateSum(item.CompletionTokens),
            TurnCount = SqlFunc.AggregateCount(item.Id),
            EstimatedTurnCount = SqlFunc.AggregateSum(item.IsEstimated ? 1 : 0)
        }).First();
        var activityByDay = query
            .GroupBy(item => SqlFunc.DateValue(item.CreatedAt))
            .Select(item => new UsageActivityAggregateRow
            {
                Date = SqlFunc.DateValue(item.CreatedAt),
                TotalTokens = SqlFunc.AggregateSum(item.TotalTokens),
                TurnCount = SqlFunc.AggregateCount(item.Id)
            })
            .ToList()
            .ToDictionary(item => item.Date.Date, item => item);
        var providers = query
            .GroupBy(item => new { item.ProviderKind, item.ProviderId, Model = item.ModelName ?? item.ModelId })
            .Select(item => new UsageProviderAggregateRow
            {
                ProviderKind = item.ProviderKind,
                ProviderId = item.ProviderId,
                Model = item.ModelName ?? item.ModelId,
                TotalTokens = SqlFunc.AggregateSum(item.TotalTokens),
                PromptTokens = SqlFunc.AggregateSum(item.PromptTokens),
                CompletionTokens = SqlFunc.AggregateSum(item.CompletionTokens),
                TurnCount = SqlFunc.AggregateCount(item.Id),
                EstimatedTurnCount = SqlFunc.AggregateSum(item.IsEstimated ? 1 : 0)
            })
            .ToList();
        var activity = Enumerable.Range(0, periodDays)
            .Select(offset => from.AddDays(offset).Date)
            .Select(date => activityByDay.TryGetValue(date, out var value)
                ? new UsageActivityDayDto { Date = date, TotalTokens = value.TotalTokens, TurnCount = value.TurnCount }
                : new UsageActivityDayDto { Date = date })
            .ToList();

        return Task.FromResult(new UsageSummaryDto
        {
            Scope = useAll ? "all" : "me",
            CanViewAll = canViewAll,
            PeriodDays = periodDays,
            From = from,
            To = to,
            TotalTokens = totals.TotalTokens,
            PromptTokens = totals.PromptTokens,
            CompletionTokens = totals.CompletionTokens,
            TurnCount = totals.TurnCount,
            EstimatedTurnCount = totals.EstimatedTurnCount,
            Providers = providers.Select(ToProviderSummary)
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
        var totals = query.Select(item => new UsageTotalsRow
        {
            TotalTokens = SqlFunc.AggregateSum(item.TotalTokens),
            PromptTokens = SqlFunc.AggregateSum(item.PromptTokens),
            CompletionTokens = SqlFunc.AggregateSum(item.CompletionTokens),
            TurnCount = SqlFunc.AggregateCount(item.Id),
            EstimatedTurnCount = SqlFunc.AggregateSum(item.IsEstimated ? 1 : 0)
        }).First();
        var providers = query
            .GroupBy(item => new { item.ProviderKind, item.ProviderId, Model = item.ModelName ?? item.ModelId })
            .Select(item => new UsageProviderAggregateRow
            {
                ProviderKind = item.ProviderKind,
                ProviderId = item.ProviderId,
                Model = item.ModelName ?? item.ModelId,
                TotalTokens = SqlFunc.AggregateSum(item.TotalTokens),
                PromptTokens = SqlFunc.AggregateSum(item.PromptTokens),
                CompletionTokens = SqlFunc.AggregateSum(item.CompletionTokens),
                TurnCount = SqlFunc.AggregateCount(item.Id),
                EstimatedTurnCount = SqlFunc.AggregateSum(item.IsEstimated ? 1 : 0)
            })
            .ToList();

        return Task.FromResult(new UsageDayDetailDto
        {
            Scope = useAll ? "all" : "me",
            CanViewAll = canViewAll,
            Date = from,
            TotalTokens = totals.TotalTokens,
            PromptTokens = totals.PromptTokens,
            CompletionTokens = totals.CompletionTokens,
            TurnCount = totals.TurnCount,
            Providers = providers.Select(ToProviderSummary)
                .OrderByDescending(item => item.TotalTokens)
                .ThenBy(item => item.ProviderKind)
                .ToList()
        });
    }

    private static bool CanViewAll(AuthenticatedUser user) => user.IsAdministrator;

    private static UsageProviderSummaryDto ToProviderSummary(UsageProviderAggregateRow item) => new()
    {
        ProviderKind = item.ProviderKind,
        ProviderId = item.ProviderId,
        Model = item.Model,
        TotalTokens = item.TotalTokens,
        PromptTokens = item.PromptTokens,
        CompletionTokens = item.CompletionTokens,
        TurnCount = item.TurnCount,
        EstimatedTurnCount = item.EstimatedTurnCount
    };

    private sealed class UsageTotalsRow
    {
        public long TotalTokens { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public int TurnCount { get; set; }
        public int EstimatedTurnCount { get; set; }
    }

    private sealed class UsageActivityAggregateRow
    {
        public DateTime Date { get; set; }
        public long TotalTokens { get; set; }
        public int TurnCount { get; set; }
    }

    private sealed class UsageProviderAggregateRow
    {
        public string ProviderKind { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public string? Model { get; set; }
        public long TotalTokens { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public int TurnCount { get; set; }
        public int EstimatedTurnCount { get; set; }
    }

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
