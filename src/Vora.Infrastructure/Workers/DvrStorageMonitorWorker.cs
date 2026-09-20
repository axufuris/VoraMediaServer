using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;
using Vora.Application.Notifications;
using Vora.Application.Settings;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Workers;

public class DvrStorageMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DvrStorageMonitorWorker> _logger;
    private bool _hasAlertedAboveThreshold;

    public DvrStorageMonitorWorker(IServiceScopeFactory scopeFactory, ILogger<DvrStorageMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DVR Storage Monitor Worker is starting.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DVR storage monitor check failed.");
            }
        }
    }

    private async Task CheckAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var settings = await settingsRepo.GetSettingsAsync();

        if (!settings.DvrNotifyOnStorageThreshold) return;
        if (settings.DvrMaxStorageGb <= 0) return;
        if (settings.DvrStorageWarningPercent <= 0 || settings.DvrStorageWarningPercent > 100) return;

        var iptvRepo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        long usedBytes = await iptvRepo.GetDvrTotalUsageBytesAsync();
        long maxBytes = settings.DvrMaxStorageGb * 1024L * 1024L * 1024L;
        long thresholdBytes = maxBytes * settings.DvrStorageWarningPercent / 100;

        bool aboveThreshold = usedBytes >= thresholdBytes;

        if (aboveThreshold && !_hasAlertedAboveThreshold)
        {
            var alerts = scope.ServiceProvider.GetRequiredService<IAdminNotificationManager>();
            double usedGb = usedBytes / 1024.0 / 1024.0 / 1024.0;
            double maxGb = settings.DvrMaxStorageGb;
            var title = $"DVR storage at {(int)(usedBytes * 100.0 / maxBytes)}% capacity";
            var message = $"Using {usedGb:F1} GB of {maxGb:F0} GB ({settings.DvrStorageWarningPercent}% threshold reached). New recordings will continue but consider freeing space or raising the cap.";
            await alerts.RaiseAsync(AdminNotificationSeverity.Warning, title, message);
            _hasAlertedAboveThreshold = true;
        }
        else if (!aboveThreshold && _hasAlertedAboveThreshold)
        {
            _hasAlertedAboveThreshold = false;
        }
    }
}
