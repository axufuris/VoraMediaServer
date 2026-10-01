using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Library;

namespace Vora.Infrastructure.Workers;

public class ScheduledJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledJobWorker> _logger;

    private readonly DailyScheduleGate _nightlyScan = new();
    private readonly DailyScheduleGate _silenceDetection = new();
    private readonly DailyScheduleGate _chronologySync = new();
    private readonly DailyScheduleGate _contentSync = new();
    private readonly DailyScheduleGate _aiEmbed = new();
    private readonly DailyScheduleGate _overlaySync = new();
    private readonly DailyScheduleGate _iptvSync = new();
    private readonly DailyScheduleGate _iptvHealthCheck = new();
    private readonly DailyScheduleGate _videoThumbnails = new();
    private readonly DailyScheduleGate _trashPurge = new();
    private readonly DailyScheduleGate _musicPopularity = new();

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

    private static bool IsCollectionSyncDue(DateTime? lastSyncedAt, int intervalDays)
    {
        var interval = Math.Max(1, intervalDays);
        return lastSyncedAt == null || (DateTime.UtcNow - lastSyncedAt.Value).TotalDays >= interval;
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

        // Read against the admin's own clock, not the container's. Every time on
        // the settings page is a wall-clock time somebody typed meaning their
        // own night; a container with no TZ set would otherwise compare them
        // against UTC.
        var now = ScheduleClock.Now(settings.ScheduleTimeZone, DateTime.UtcNow);

        var aiScheduleStr = await settingsRepo.GetPluginSettingAsync("openai_recommendations", "schedule_time");
        var aiParsed = TimeSpan.TryParse(string.IsNullOrWhiteSpace(aiScheduleStr) ? "02:00" : aiScheduleStr, out var aiTime);

        var overlayScheduleStr = await settingsRepo.GetPluginSettingAsync("local_imagesharp_overlays", "schedule_time");
        var overlayParsed = TimeSpan.TryParse(string.IsNullOrWhiteSpace(overlayScheduleStr) ? "03:00" : overlayScheduleStr, out var overlayTime);

        if (_nightlyScan.IsDue(settings.NightlyScanTime, now) && settings.EnableNightlyScan)
        {
            _logger.LogInformation("Triggering Scheduled Nightly Library Scan.");

            var libraries = await libraryManager.GetLibrariesAsync(true, new List<Guid>());

            foreach (var lib in libraries)
            {
                taskQueue.QueueScanLibrary(lib.Id, lib.Name);
            }

            await metadataManager.TriggerActorMetadataRefreshAsync();

            taskQueue.QueueOverlayOrphanSweep();

            _nightlyScan.MarkRan(now);
        }

        if (_trashPurge.IsDue(settings.NightlyScanTime, now) && settings.EnableTrashAutoPurge && settings.MissingMediaRetentionDays > 0)
        {
            var mediaManager = scope.ServiceProvider.GetRequiredService<IMediaManager>();
            var purged = await mediaManager.PurgeExpiredTrashAsync(settings.MissingMediaRetentionDays);
            if (purged > 0)
            {
                _logger.LogInformation("Purged {Count} expired missing media item(s) past the {Days}-day retention window.", purged, settings.MissingMediaRetentionDays);
            }

            _trashPurge.MarkRan(now);
        }

        // Daily, but cheap on any day but the first: the refresher only asks about
        // artists never refreshed or not refreshed in thirty days. Deliberately not
        // tied to EnableNightlyScan — popularity has nothing to do with whether
        // the library is rescanned, and the refresher is a no-op when no listening
        // provider is configured.
        if (_musicPopularity.IsDue(settings.NightlyScanTime, now))
        {
            taskQueue.QueueRefreshMusicPopularity();
            taskQueue.QueueRateMusicContent();
            _musicPopularity.MarkRan(now);
        }

        // Queued before analysis on purpose: the two share the library-maint
        // resource key, so whichever is queued first holds it and the other
        // waits. Thumbnails are the cheaper pass and the one a viewer notices
        // missing, so they go first when both fall due together.
        bool shouldRunThumbnails = settings.VideoThumbnailGeneration == Domain.Enums.DetectionTrigger.OnSchedule ||
                                   settings.VideoThumbnailGeneration == Domain.Enums.DetectionTrigger.OnAdditionAndSchedule;

        if (_videoThumbnails.IsDue(settings.VideoThumbnailScheduleTime, now) && shouldRunThumbnails)
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
                if (!lib.Type.HasVideoContent()) continue;
                taskQueue.QueueGenerateLibraryVideoThumbnails(lib.Id, lib.Name, isScheduleTrigger: true);
            }

            _videoThumbnails.MarkRan(now);
        }

        bool shouldRunDetections = settings.RunDetections == Domain.Enums.DetectionTrigger.OnSchedule ||
                                   settings.RunDetections == Domain.Enums.DetectionTrigger.OnAdditionAndSchedule;

        if (_silenceDetection.IsDue(settings.DetectionScheduleTime, now) && shouldRunDetections)
        {
            _logger.LogInformation("Triggering Scheduled Silence Detection.");

            var libraries = await libraryManager.GetLibrariesAsync(true, new List<Guid>());

            foreach (var lib in libraries)
            {
                taskQueue.QueueAnalyzeLibraryMediaContent(lib.Id, lib.Name, isScheduleTrigger: true);
            }

            _silenceDetection.MarkRan(now);
        }

        if (_chronologySync.IsDue(settings.NightlyScanTime, now) && settings.EnableNightlyScan)
        {
            _logger.LogInformation("Triggering Scheduled Chronology Auto-Syncs.");

            var autoSyncCollections = await collectionManager.GetAutoSyncCollectionsAsync();

            foreach (var collection in autoSyncCollections)
            {
                if (IsCollectionSyncDue(collection.ChronologySyncedAt, collection.SyncIntervalDays))
                {
                    taskQueue.QueueCollectionChronologySync(collection.Id, collection.Title);
                }
            }

            _chronologySync.MarkRan(now);
        }

        if (_contentSync.IsDue(settings.NightlyScanTime, now) && settings.EnableNightlyScan)
        {
            _logger.LogInformation("Triggering Scheduled Collection Auto-Fills.");

            var contentSyncCollections = await collectionManager.GetContentSyncCollectionsAsync();

            foreach (var collection in contentSyncCollections)
            {
                if (IsCollectionSyncDue(collection.ContentSyncedAt, collection.SyncIntervalDays))
                {
                    taskQueue.QueueCollectionContentSync(collection.Id, collection.Title);
                }
            }

            _contentSync.MarkRan(now);
        }

        if (aiParsed && _aiEmbed.IsDue(aiTime, now))
        {
            var isAiEnabled = await settingsRepo.GetPluginSettingAsync("openai_recommendations", "is_enabled");
            if (isAiEnabled != "false")
            {
                _logger.LogInformation("Triggering Nightly AI Vector Generation.");
                taskQueue.QueueGenerateAiEmbeddings();
            }

            // Its own switch, not the recommendations plugin's: AI playlists only
            // need the OpenAI key that plugin holds.
            if (settings.EnableAiMusicPlaylists)
            {
                taskQueue.QueueGenerateAiPlaylists();
            }

            _aiEmbed.MarkRan(now);
        }

        if (overlayParsed && _overlaySync.IsDue(overlayTime, now))
        {
            var isOverlayEnabledStr = await settingsRepo.GetPluginSettingAsync("local_imagesharp_overlays", "enable_schedule");

            if (bool.TryParse(isOverlayEnabledStr, out bool isOverlayEnabled) && isOverlayEnabled)
            {
                _logger.LogInformation("Triggering Nightly Poster Overlay Sync.");

                var libraries = await libraryManager.GetLibrariesAsync(true, new List<Guid>());
                foreach (var lib in libraries)
                {
                    taskQueue.QueueGenerateLibraryPosterOverlays(lib.Id, lib.Name);
                }
            }

            _overlaySync.MarkRan(now);
        }

        if (_iptvSync.IsDue(settings.IptvSyncTime, now))
        {
            _logger.LogInformation("Triggering Scheduled IPTV EPG Sync.");

            taskQueue.QueueIptvEpgSync();

            _iptvSync.MarkRan(now);
        }

        if (_iptvHealthCheck.IsDue(settings.IptvHealthCheckTime, now))
        {
            var iptvManager = scope.ServiceProvider.GetRequiredService<IIptvManager>();
            var playlists = await iptvManager.GetAllPlaylistsAsync();
            var enabled = playlists.Where(p => p.EnableHealthCheck).ToList();
            if (enabled.Count > 0)
            {
                _logger.LogInformation("Triggering Scheduled IPTV Channel Health Check for {Count} playlist(s).", enabled.Count);
                foreach (var pl in enabled)
                {
                    taskQueue.QueueIptvHealthCheck(pl.Id, pl.Name);
                }
            }

            _iptvHealthCheck.MarkRan(now);
        }

    }
}
