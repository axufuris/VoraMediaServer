using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Libraries;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;

namespace Vora.Infrastructure.Workers;

public class ScheduledJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledJobWorker> _logger;

    private DateTime _lastNightlyScanDate = DateTime.MinValue.Date;
    private DateTime _lastSilenceDetectionDate = DateTime.MinValue.Date;
    private DateTime _lastChronologySyncDate = DateTime.MinValue.Date;
    private DateTime _lastContentSyncDate = DateTime.MinValue.Date;
    private DateTime _lastAiEmbedDate = DateTime.MinValue.Date;
    private DateTime _lastOverlaySyncDate = DateTime.MinValue.Date;
    private DateTime _lastIptvSyncDate = DateTime.MinValue.Date;
    private DateTime _lastVideoThumbnailDate = DateTime.MinValue.Date;

    private int _scannerFrequency = 5;

    public ScheduledJobWorker(IServiceScopeFactory scopeFactory, ILogger<ScheduledJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled Job Worker is starting.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_scannerFrequency));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAndRunSchedulesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while checking scheduled jobs.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scheduled Job Worker is stopping.");
        }
    }

    private async Task CheckAndRunSchedulesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var libraryManager = scope.ServiceProvider.GetRequiredService<ILibraryManager>();
        var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
        var analyzerManager = scope.ServiceProvider.GetRequiredService<IMediaAnalyzerManager>();
        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        var collectionManager = scope.ServiceProvider.GetRequiredService<ICollectionManager>();

        var settings = await settingsRepo.GetSettingsAsync();
        var now = DateTime.Now;
        var timeOfDay = now.TimeOfDay;
        var today = now.Date;

        if (settings.EnableNightlyScan && timeOfDay >= settings.NightlyScanTime && _lastNightlyScanDate < today)
        {
            _logger.LogInformation("Triggering Scheduled Nightly Library Scan.");

            var libraries = await libraryManager.GetLibrariesAsync(true, new List<Guid>());

            foreach (var lib in libraries)
            {
                taskQueue.QueueScanLibrary(lib.Id, lib.Name);
            }

            await metadataManager.TriggerActorMetadataRefreshAsync();

            _lastNightlyScanDate = today;
        }

        bool shouldRunDetections = settings.RunDetections == Domain.Enums.DetectionTrigger.OnSchedule ||
                                   settings.RunDetections == Domain.Enums.DetectionTrigger.OnAdditionAndSchedule;

        if (shouldRunDetections && timeOfDay >= settings.DetectionScheduleTime && _lastSilenceDetectionDate < today)
        {
            _logger.LogInformation("Triggering Scheduled Silence Detection.");

            var libraries = await libraryManager.GetLibrariesAsync(true, new List<Guid>());

            foreach (var lib in libraries)
            {
                taskQueue.QueueAnalyzeLibraryMediaContent(lib.Id, lib.Name, isScheduleTrigger: true);
            }

            _lastSilenceDetectionDate = today;
        }

        if (settings.EnableNightlyScan && timeOfDay >= settings.NightlyScanTime && _lastChronologySyncDate < today)
        {
            _logger.LogInformation("Triggering Scheduled Chronology Auto-Syncs.");

            var autoSyncCollections = await collectionManager.GetAutoSyncCollectionsAsync();

            foreach (var collection in autoSyncCollections)
            {
                taskQueue.QueueCollectionChronologySync(collection.Id, collection.Title);
            }

            _lastChronologySyncDate = today;
        }

        if (settings.EnableNightlyScan && timeOfDay >= settings.NightlyScanTime && _lastContentSyncDate < today)
        {
            _logger.LogInformation("Triggering Scheduled Collection Auto-Fills.");

            var contentSyncCollections = await collectionManager.GetContentSyncCollectionsAsync();

            foreach (var collection in contentSyncCollections)
            {
                taskQueue.QueueCollectionContentSync(collection.Id, collection.Title);
            }

            _lastContentSyncDate = today;
        }

        var aiScheduleStr = await settingsRepo.GetPluginSettingAsync("openai_recommendations", "schedule_time");
        var aiTimeStr = string.IsNullOrWhiteSpace(aiScheduleStr) ? "02:00" : aiScheduleStr;

        if (TimeSpan.TryParse(aiTimeStr, out var aiTime) && timeOfDay >= aiTime && _lastAiEmbedDate < today)
        {
            var isAiEnabled = await settingsRepo.GetPluginSettingAsync("openai_recommendations", "is_enabled");
            if (isAiEnabled != "false")
            {
                _logger.LogInformation("Triggering Nightly AI Vector Generation.");
                taskQueue.QueueGenerateAiEmbeddings();
            }

            _lastAiEmbedDate = today;
        }

        var overlayScheduleStr = await settingsRepo.GetPluginSettingAsync("local_imagesharp_overlays", "schedule_time");
        var overlayTimeStr = string.IsNullOrWhiteSpace(overlayScheduleStr) ? "03:00" : overlayScheduleStr;

        if (TimeSpan.TryParse(overlayTimeStr, out var overlayTime) && timeOfDay >= overlayTime && _lastOverlaySyncDate < today)
        {
            var isOverlayEnabledStr = await settingsRepo.GetPluginSettingAsync("local_imagesharp_overlays", "enable_schedule");

            if (bool.TryParse(isOverlayEnabledStr, out bool isOverlayEnabled) && isOverlayEnabled)
            {
                _logger.LogInformation("Triggering Nightly Poster Overlay Sync.");

                var libraries = await libraryManager.GetLibrariesAsync(true, new List<Guid>());
                foreach (var lib in libraries)
                {
                    taskQueue.QueueGenerateLibraryPosterOverlays(lib.Id);
                }
            }

            _lastOverlaySyncDate = today;
        }

        if (timeOfDay >= settings.IptvSyncTime && _lastIptvSyncDate < today)
        {
            _logger.LogInformation("Triggering Scheduled IPTV EPG Sync.");

            taskQueue.QueueIptvEpgSync();

            _lastIptvSyncDate = today;
        }

        if (timeOfDay >= settings.VideoThumbnailScheduleTime && _lastVideoThumbnailDate < today)
        {
            _logger.LogInformation("Triggering Scheduled Video Thumbnail Generation.");

            var libraryRepo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
            var thumbnailLibraries = await libraryRepo.GetAllProjectedAsync(l => new
            {
                l.Id,
                l.Name,
                l.Type,
                l.EnableVideoPreviewThumbnails
            });

            foreach (var lib in thumbnailLibraries)
            {
                if (!lib.EnableVideoPreviewThumbnails) continue;
                if (!VideoThumbnailManager.IsVideoBearingLibrary(lib.Type)) continue;
                taskQueue.QueueGenerateLibraryVideoThumbnails(lib.Id, lib.Name, isScheduleTrigger: true);
            }

            _lastVideoThumbnailDate = today;
        }
    }
}
