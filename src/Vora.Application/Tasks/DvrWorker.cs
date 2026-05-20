using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tasks;

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
                await ProcessRecordingsAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DVR Precision Worker is stopping.");
        }
    }

    private async Task ProcessRecordingsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        var dvrManager = scope.ServiceProvider.GetRequiredService<IDvrManager>();
        var recordingService = scope.ServiceProvider.GetRequiredService<IDvrRecordingService>();

        var now = DateTime.UtcNow;

        var sessionsToStop = await repo.GetActiveSessionsToStopAsync(now);
        foreach (var session in sessionsToStop)
        {
            _logger.LogInformation($"[DVR Scheduler] Stopping finished recording: {session.Title}");
            await recordingService.StopRecordingAsync(session.Id);
        }

        var triggerTime = now.AddMinutes(1);
        var sessionsToStart = await repo.GetPendingSessionsToStartAsync(triggerTime);

        foreach (var session in sessionsToStart)
        {
            if (session.StartTime < now.AddHours(-2))
            {
                _logger.LogWarning($"[DVR Scheduler] Session {session.Id} is too old. Marking as Failed.");
                await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Failed, errorMessage: "Recording window expired before tuners became available.");
                continue;
            }

            try
            {
                bool canAllocate = await dvrManager.CanAllocateTunerAsync(session.Schedule.Channel.PlaylistId);

                if (canAllocate)
                {
                    _logger.LogInformation($"[DVR Scheduler] Starting recording: {session.Title}");
                    await recordingService.StartRecordingAsync(session.Id);
                }
                else
                {
                    _logger.LogWarning($"[DVR Scheduler] Insufficient tuners to record: {session.Title}. Marking as conflict.");
                    await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Conflict, errorMessage: "No tuners available at start time.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DVR Scheduler] Fatal error trying to start session {session.Id}");
                await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Failed, errorMessage: "Internal scheduler error.");
            }
        }
    }
}