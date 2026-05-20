using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Iptv;

public class TimeshiftJanitorWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxIdleDuration = TimeSpan.FromMinutes(2);

    private readonly ITimeshiftCoordinator _coordinator;
    private readonly ILogger<TimeshiftJanitorWorker> _logger;

    public TimeshiftJanitorWorker(ITimeshiftCoordinator coordinator, ILogger<TimeshiftJanitorWorker> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timeshift Janitor Worker is starting.");

        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _coordinator.EvictStaleSessionsAsync(MaxIdleDuration);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while evicting stale timeshift sessions.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Timeshift Janitor Worker is stopping.");
        }
    }
}
