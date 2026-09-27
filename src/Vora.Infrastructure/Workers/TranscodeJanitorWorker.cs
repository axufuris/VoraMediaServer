using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Workers;

public class TranscodeJanitorWorker : BackgroundService
{
    private const string DefaultTranscodeDirectory = "/transcode";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxIdleDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OrphanMaxAge = TimeSpan.FromMinutes(30);

    private readonly ITranscodeService _transcodeService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranscodeJanitorWorker> _logger;

    public TranscodeJanitorWorker(
        ITranscodeService transcodeService,
        IServiceScopeFactory scopeFactory,
        ILogger<TranscodeJanitorWorker> logger)
    {
        _transcodeService = transcodeService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transcode Janitor Worker is starting.");

        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _transcodeService.EvictIdleSessionsAsync(MaxIdleDuration);
                    await ReapOrphansAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error during transcode janitor tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transcode Janitor Worker is stopping.");
        }
    }

    private async Task ReapOrphansAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var settings = await settingsRepo.GetSettingsAsync();
        if (cancellationToken.IsCancellationRequested) return;

        var tempDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory)
            ? DefaultTranscodeDirectory
            : settings.TranscoderTempDirectory;

        await _transcodeService.ReapOrphanedTranscodeFilesAsync(tempDir, OrphanMaxAge);
    }
}
