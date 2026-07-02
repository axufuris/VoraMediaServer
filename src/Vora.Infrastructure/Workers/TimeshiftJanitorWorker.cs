using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;

namespace Vora.Infrastructure.Workers;

public class TimeshiftJanitorWorker : BackgroundService
{
    private const string DefaultTranscodeDirectory = "/transcode";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxIdleDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LiveMaxIdleDuration = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan OrphanMaxIdleAge = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITimeshiftCoordinator _coordinator;
    private readonly ITunerRegistry _tunerRegistry;
    private readonly ILogger<TimeshiftJanitorWorker> _logger;

    public TimeshiftJanitorWorker(
        IServiceScopeFactory scopeFactory,
        ITimeshiftCoordinator coordinator,
        ITunerRegistry tunerRegistry,
        ILogger<TimeshiftJanitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _coordinator = coordinator;
        _tunerRegistry = tunerRegistry;
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
                    var evictedTimeshifts = await _coordinator.EvictStaleSessionsAsync(MaxIdleDuration);
                    foreach (var profileId in evictedTimeshifts)
                    {
                        _tunerRegistry.Release(IptvManager.TimeshiftLeaseKey(profileId));
                    }

                    _tunerRegistry.EvictIdle(TunerLeaseKind.Live, LiveMaxIdleDuration);

                    await ReapOrphansAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during timeshift janitor tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Timeshift Janitor Worker is stopping.");
        }
    }

    private async Task ReapOrphansAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        var settings = await repository.GetServerSettingsAsync();
        if (cancellationToken.IsCancellationRequested) return;

        var tempDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory)
            ? DefaultTranscodeDirectory
            : settings.TranscoderTempDirectory;
        var timeshiftRoot = Path.Combine(tempDir, TimeshiftCoordinator.TimeshiftSubdirectory);

        await _coordinator.ReapOrphanedDirectoriesAsync(timeshiftRoot, OrphanMaxIdleAge);
    }
}
