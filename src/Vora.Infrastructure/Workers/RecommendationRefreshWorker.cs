using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Settings;

namespace Vora.Infrastructure.Workers;

public class RecommendationRefreshWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecommendationRefreshWorker> _logger;

    public RecommendationRefreshWorker(IServiceScopeFactory scopeFactory, ILogger<RecommendationRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recommendation Refresh Worker is starting.");

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            do
            {
                await RunTickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Recommendation Refresh Worker is stopping.");
        }
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();

            if (!settings.EnableDailyMixes)
            {
                _logger.LogDebug("Daily mixes disabled in settings; skipping tick.");
                return;
            }

            var manager = scope.ServiceProvider.GetRequiredService<IMusicRecommendationManager>();

            if (IsDueForRefresh(settings.DailyMixSchedule, settings.DailyMixLastRefreshedAt, DateTime.UtcNow))
            {
                _logger.LogInformation("Daily mix refresh: starting (schedule={Schedule}, last={Last})",
                    settings.DailyMixSchedule, settings.DailyMixLastRefreshedAt);
                await manager.RefreshAllActiveProfilesAsync(stoppingToken);
                _logger.LogInformation("Daily mix refresh complete.");
            }

            if (settings.EnableWeeklyMixes && IsWeeklyDue(settings.WeeklyMixLastRefreshedAt, DateTime.UtcNow))
            {
                _logger.LogInformation("Weekly mix refresh: starting (last={Last})", settings.WeeklyMixLastRefreshedAt);
                await manager.RefreshWeeklyMixesForAllAsync(stoppingToken);
                _logger.LogInformation("Weekly mix refresh complete.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recommendation refresh tick crashed.");
        }
    }

    internal static bool IsDueForRefresh(string schedulePreset, DateTime? lastRefreshedAt, DateTime nowUtc)
    {
        if (string.Equals(schedulePreset, "ManualOnly", StringComparison.OrdinalIgnoreCase)) return false;

        switch (schedulePreset)
        {
            case "Every6Hours":
                return lastRefreshedAt == null || (nowUtc - lastRefreshedAt.Value) >= TimeSpan.FromHours(6);
            case "Every12Hours":
                return lastRefreshedAt == null || (nowUtc - lastRefreshedAt.Value) >= TimeSpan.FromHours(12);
            case "WeeklySunday3am":
                return IsDueForDailyOrWeeklyUtc(nowUtc, lastRefreshedAt, targetHourUtc: 3, weekly: true, targetDow: DayOfWeek.Sunday);
            case "DailyMidnight":
                return IsDueForDailyOrWeeklyUtc(nowUtc, lastRefreshedAt, targetHourUtc: 0, weekly: false, targetDow: null);
            case "Daily6am":
                return IsDueForDailyOrWeeklyUtc(nowUtc, lastRefreshedAt, targetHourUtc: 6, weekly: false, targetDow: null);
            case "Daily3am":
            default:
                return IsDueForDailyOrWeeklyUtc(nowUtc, lastRefreshedAt, targetHourUtc: 3, weekly: false, targetDow: null);
        }
    }

    internal static bool IsWeeklyDue(DateTime? lastRefreshedAtUtc, DateTime nowUtc)
    {
        if (lastRefreshedAtUtc == null) return true;
        return (nowUtc - lastRefreshedAtUtc.Value) >= TimeSpan.FromDays(7);
    }

    private static bool IsDueForDailyOrWeeklyUtc(DateTime nowUtc, DateTime? lastRefreshedAtUtc, int targetHourUtc, bool weekly, DayOfWeek? targetDow)
    {
        var todayTargetUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, targetHourUtc, 0, 0, DateTimeKind.Utc);
        var pastTargetToday = nowUtc >= todayTargetUtc;
        var dowMatches = !weekly || (targetDow.HasValue && nowUtc.DayOfWeek == targetDow.Value);

        if (!dowMatches || !pastTargetToday) return false;

        if (lastRefreshedAtUtc == null) return true;
        if (weekly)
        {
            return (nowUtc - lastRefreshedAtUtc.Value).TotalDays >= 6.9;
        }
        return lastRefreshedAtUtc.Value.Date < nowUtc.Date;
    }
}
