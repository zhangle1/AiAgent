namespace AiAgent.Backend.Services.Memory;

/// <summary>
/// 对闲置会话进行低频、限量的候选记忆提炼。失败只保留原始观察，下一轮可重试。
/// </summary>
public sealed class MemoryCandidateHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private readonly IMemoryCandidateService _candidates;
    private readonly ILogger<MemoryCandidateHostedService> _logger;

    public MemoryCandidateHostedService(IMemoryCandidateService candidates, ILogger<MemoryCandidateHostedService> logger)
        => (_candidates, _logger) = (candidates, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _candidates.ProcessIdleSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Idle memory candidate consolidation failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
        }
    }
}
