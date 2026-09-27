using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Workers;

public class DvrWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DvrWorker> _logger;

    public DvrWorker(IServiceScopeFactory scopeFactory, ILogger<DvrWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DVR Precision Worker is starting.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessRecordingsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[DVR Scheduler] Error during recording scheduler pass.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DVR Precision Worker is stopping.");
        }
    }

    private async Task ProcessRecordingsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        var recordingService = scope.ServiceProvider.GetRequiredService<IDvrRecordingService>();

        var now = DateTime.UtcNow;

        var sessionsToStop = await repo.GetActiveSessionsToStopAsync(now);
        foreach (var session in sessionsToStop)
        {
            if (stoppingToken.IsCancellationRequested) return;
            _logger.LogInformation($"[DVR Scheduler] Stopping finished recording: {session.Title}");
            await recordingService.StopRecordingAsync(session.Id);
        }

        var triggerTime = now.AddMinutes(1);
        var sessionsToStart = await repo.GetPendingSessionsToStartAsync(triggerTime);

        foreach (var session in sessionsToStart)
        {
            if (stoppingToken.IsCancellationRequested) return;

            if (session.StartTime < now.AddHours(-2))
            {
                _logger.LogWarning($"[DVR Scheduler] Session {session.Id} is too old. Marking as Failed.");
                await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Failed, errorMessage: "Recording window expired before tuners became available.");
                continue;
            }

            try
            {
                _logger.LogInformation($"[DVR Scheduler] Attempting recording: {session.Title}");
                await recordingService.StartRecordingAsync(session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DVR Scheduler] Fatal error trying to start session {session.Id}");
                await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Failed, errorMessage: "Internal scheduler error.");
            }
        }
    }
}
