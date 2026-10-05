using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Backups;

namespace Vora.Infrastructure.Workers;

public sealed class BackupScheduleWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupScheduleWorker> _logger;

    public BackupScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<BackupScheduleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup schedule worker started.");
        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAndRunAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup schedule worker tick failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckAndRunAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var settingsStore = scope.ServiceProvider.GetRequiredService<IBackupSettingsStore>();
        var manager = scope.ServiceProvider.GetRequiredService<IBackupManager>();

        var settings = await settingsStore.GetAsync(ct);
        if (!settings.AutoBackupEnabled || settings.Cadence == BackupCadence.Off) return;

        var zone = await settingsStore.GetScheduleTimeZoneAsync(ct);
        if (!BackupScheduleEvaluator.IsDue(settings, DateTime.UtcNow, zone)) return;

        _logger.LogInformation("Auto-backup is due ({Cadence} at {Hour:00}:{Minute:00} {Zone}); creating backup.",
            settings.Cadence, settings.Hour, settings.Minute, zone.Id);
        try
        {
            var summary = await manager.CreateBackupAsync("auto", ct);
            _logger.LogInformation("Auto-backup completed: {File} ({Bytes} bytes, {Sections} sections).",
                summary.FileName, summary.FileSizeBytes, summary.SectionCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-backup failed.");
        }
    }
}
