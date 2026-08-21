using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Thumbnails;

public class VideoThumbnailManager : IVideoThumbnailManager
{
    public const string ThumbnailsLockField = "Thumbnails";

    // Each item's sprite pass is a full-file FFmpeg decode. The admin-configurable
    // VideoThumbnailConcurrency governs how many run at once, clamped to this ceiling.
    private const int MaxThumbnailParallelism = 16;

    private readonly IMediaRepository _mediaRepository;
    private readonly ILibraryRepository _libraryRepository;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IVideoThumbnailStorageService _storage;
    private readonly IVideoThumbnailGeneratorService _generator;
    private readonly IClientNotifier _notifier;
    private readonly Notifications.IAdminNotificationManager _adminNotifications;
    private readonly Vora.Plugins.Interfaces.ITaskProgressReporter _progress;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VideoThumbnailManager> _logger;
    private readonly string _customArtworkBase;

    public VideoThumbnailManager(
        IMediaRepository mediaRepository,
        ILibraryRepository libraryRepository,
        ISystemSettingsRepository settingsRepo,
        IVideoThumbnailStorageService storage,
        IVideoThumbnailGeneratorService generator,
        IClientNotifier notifier,
        Notifications.IAdminNotificationManager adminNotifications,
        Vora.Plugins.Interfaces.ITaskProgressReporter progress,
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<Settings.StoragePathsOptions> storagePaths,
        ILogger<VideoThumbnailManager> logger)
    {
        _mediaRepository = mediaRepository;
        _libraryRepository = libraryRepository;
        _settingsRepo = settingsRepo;
        _storage = storage;
        _generator = generator;
        _notifier = notifier;
        _adminNotifications = adminNotifications;
        _progress = progress;
        _scopeFactory = scopeFactory;
        _logger = logger;
        var customArtwork = storagePaths.Value.CustomArtwork;
        _customArtworkBase = !string.IsNullOrWhiteSpace(customArtwork)
            ? customArtwork
            : System.IO.Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");
    }

    public async Task TriggerMediaItemThumbnailGenerationAsync(Guid mediaItemId, bool forceOverride = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default)
    {
        var itemType = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.GetType().Name);

        if (itemType == nameof(TvShow))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForShowAsync(mediaItemId);
            await GenerateManyAsync(epIds, forceOverride, cancellationToken);
            return;
        }

        if (itemType == nameof(Season))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(mediaItemId);
            await GenerateManyAsync(epIds, forceOverride, cancellationToken);
            return;
        }

        if (itemType != nameof(Movie) && itemType != nameof(Episode)) return;

        await RunSingleAsync(mediaItemId, forceOverride, cancellationToken);
    }

    public async Task TriggerLibraryThumbnailGenerationAsync(Guid libraryId, bool forceOverride = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default)
    {
        var (libraryType, enabled) = await GetLibraryThumbnailStateAsync(libraryId);
        if (!IsVideoBearingLibrary(libraryType)) return;
        if (!enabled && !forceOverride) return;

        // On a non-forced run only pull the items that still need thumbnails
        // (never generated or generated with an outdated sprite version), so the
        // progress total reflects what's left instead of the whole library.
        var settings = await _settingsRepo.GetSettingsAsync();
        var version = ComputeSpriteVersion(settings.VideoThumbnailIntervalSeconds, settings.VideoThumbnailWidth, settings.VideoThumbnailHeight, settings.VideoThumbnailJpegQuality, settings.VideoThumbnailSpriteColumns);
        var ids = await _mediaRepository.GetVideoThumbnailTargetIdsAsync(libraryId, version, includeCompleted: forceOverride);
        await GenerateManyAsync(ids, forceOverride, cancellationToken);
    }

    public Task GenerateForItemAsync(Guid mediaItemId, bool forceOverride, CancellationToken cancellationToken = default) =>
        RunSingleAsync(mediaItemId, forceOverride, cancellationToken);

    // Each item's ffmpeg pass is an independent full-file decode, so run several
    // at once — each in its own DI scope (own DbContext) so parallel writes don't
    // share a context. Mirrors the file-analysis/overlay/marker fan-out.
    private async Task GenerateManyAsync(IEnumerable<Guid> idsSource, bool forceOverride, CancellationToken cancellationToken)
    {
        var ids = idsSource as IReadOnlyList<Guid> ?? idsSource.ToList();
        if (ids.Count == 0) return;

        var titles = await _mediaRepository.GetDisplayTitlesByIdsAsync(ids);
        var total = ids.Count;
        var done = 0;

        var settings = await _settingsRepo.GetSettingsAsync();
        var parallelism = Math.Clamp(settings.VideoThumbnailConcurrency <= 0 ? 2 : settings.VideoThumbnailConcurrency, 1, MaxThumbnailParallelism);
        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (id, ct) =>
            {
                // Report BEFORE generating: each item is a slow FFmpeg run, so
                // reporting only on completion left the task blank at the start and
                // between items. Showing the item as it starts keeps the "what it's
                // on" line live.
                var n = Interlocked.Increment(ref done);
                _progress.Report($"{ProgressTitle(titles, id)} ({n}/{total})");

                try
                {
                    // Each item runs in its own scope so parallel work never shares
                    // the manager's DbContext (a shared query here raced and threw
                    // "a second operation was started on this context"). The id list
                    // is already filtered to Movies/Episodes by the caller.
                    using var scope = _scopeFactory.CreateScope();
                    var manager = scope.ServiceProvider.GetRequiredService<IVideoThumbnailManager>();
                    await manager.GenerateForItemAsync(id, forceOverride, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Video thumbnail generation failed for {MediaItemId}.", id);
                }
            });
    }

    private static string ProgressTitle(IReadOnlyDictionary<Guid, string> titles, Guid id) =>
        titles.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t) ? t : "…";

    public async Task<(int Total, int WithThumbnails)> GetCoverageAsync(Guid libraryId)
    {
        var (libraryType, _) = await GetLibraryThumbnailStateAsync(libraryId);
        if (!IsVideoBearingLibrary(libraryType)) return (0, 0);

        return await _mediaRepository.GetVideoThumbnailCoverageAsync(libraryId);
    }

    private async Task PurgeMediaItemThumbnailsAsync(Guid mediaItemId)
    {
        _storage.DeleteItemDirectory(mediaItemId);

        var item = await _mediaRepository.GetItemWithPartsForThumbnailsAsync(mediaItemId);
        if (item == null) return;

        item.LastVideoThumbnailGenerationAt = null;
        item.VideoThumbnailSpriteVersion = null;
        item.VideoThumbnailSpriteCount = 0;
        item.VideoThumbnailIntervalSeconds = 0;
        item.VideoThumbnailSpriteColumns = 0;
        item.VideoThumbnailWidth = 0;
        item.VideoThumbnailHeight = 0;

        foreach (var part in item.MediaParts)
        {
            part.ThumbnailSourcePartId = null;
            part.VideoThumbnailSpriteVersion = null;
            part.VideoThumbnailSpriteCount = 0;
            part.VideoThumbnailIntervalSeconds = 0;
            part.VideoThumbnailSpriteColumns = 0;
            part.VideoThumbnailWidth = 0;
            part.VideoThumbnailHeight = 0;
        }

        await _mediaRepository.UpdateMediaItemAsync(item);
    }

    public async Task PurgeLibraryThumbnailsAsync(Guid libraryId)
    {
        var ids = await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId);
        foreach (var id in ids)
        {
            try
            {
                await PurgeMediaItemThumbnailsAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to purge video thumbnails for {MediaItemId}.", id);
            }
        }
    }

    private async Task RunSingleAsync(Guid mediaItemId, bool forceOverride, CancellationToken cancellationToken = default)
    {
        var meta = await _mediaRepository.GetProjectedAsync(mediaItemId, m => new
        {
            LibraryType = m.Library.Type,
            LibraryEnabled = m.Library.EnableVideoPreviewThumbnails,
            LockedFields = m.LockedFields,
            CurrentVersion = m.VideoThumbnailSpriteVersion,
            CurrentGeneratedAt = m.LastVideoThumbnailGenerationAt,
            IsEpisode = m is Episode,
            HasPoster = m.PosterUrl != null
        });

        if (meta == null) return;
        if (!IsVideoBearingLibrary(meta.LibraryType)) return;
        if (!meta.LibraryEnabled && !forceOverride) return;
        if (meta.LockedFields != null && meta.LockedFields.Contains(ThumbnailsLockField, StringComparer.OrdinalIgnoreCase)) return;

        var settings = await _settingsRepo.GetSettingsAsync();
        var version = ComputeSpriteVersion(settings.VideoThumbnailIntervalSeconds, settings.VideoThumbnailWidth, settings.VideoThumbnailHeight, settings.VideoThumbnailJpegQuality, settings.VideoThumbnailSpriteColumns);

        if (!forceOverride && meta.CurrentVersion == version)
        {
            return;
        }

        var item = await _mediaRepository.GetItemWithPartsForThumbnailsAsync(mediaItemId);
        if (item == null) return;

        var partsWithFiles = item.MediaParts
            .Where(p => !string.IsNullOrWhiteSpace(p.FilePath) && File.Exists(p.FilePath))
            .OrderBy(p => p.Id)
            .ToList();

        if (partsWithFiles.Count == 0)
        {
            _logger.LogWarning("Skipping video thumbnail generation for {MediaItemId}: no readable media file on record", mediaItemId);
            return;
        }

        _storage.EnsureItemDirectory(mediaItemId);

        // Group the parts into cuts by runtime (±5s). Parts in the same cut share a
        // single sprite (generated once from the group's representative); a part
        // whose runtime differs by more than the tolerance — a genuinely different
        // edit — gets its own sprite. Unknown-duration parts each get their own.
        var cuts = GroupPartsIntoCuts(partsWithFiles);

        var anySuccess = false;
        VideoThumbnailGenerationResult? firstResult = null;
        string? firstInput = null;

        foreach (var cut in cuts)
        {
            var representative = cut[0];
            _storage.EnsurePartDirectory(mediaItemId, representative.Id);
            var spritePath = _storage.GetPartSpritePath(mediaItemId, representative.Id);
            var vttPath = _storage.GetPartVttPath(mediaItemId, representative.Id);

            VideoThumbnailGenerationResult result;
            try
            {
                result = await _generator.GenerateAsync(
                    new VideoThumbnailGenerationParameters
                    {
                        InputPath = representative.FilePath,
                        IntervalSeconds = settings.VideoThumbnailIntervalSeconds,
                        Width = settings.VideoThumbnailWidth,
                        Height = settings.VideoThumbnailHeight,
                        JpegQuality = settings.VideoThumbnailJpegQuality,
                        SpriteColumns = settings.VideoThumbnailSpriteColumns,
                        UseHardwareDecode = settings.UseHardwareAcceleration,
                        HardwareDevice = settings.HardwareTranscodingDevice
                    },
                    spritePath,
                    vttPath,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ffmpeg generation failed for {MediaItemId} part {PartId} ({Input}); skipping this cut", mediaItemId, representative.Id, representative.FilePath);
                await RaiseThumbnailFailureAlertAsync(mediaItemId, representative.FilePath);
                continue;
            }

            anySuccess = true;
            firstResult ??= result;
            firstInput ??= representative.FilePath;

            foreach (var part in cut)
            {
                part.ThumbnailSourcePartId = representative.Id;
                part.VideoThumbnailSpriteVersion = version;
                part.VideoThumbnailSpriteCount = result.SpriteCount;
                part.VideoThumbnailIntervalSeconds = result.IntervalSeconds;
                part.VideoThumbnailSpriteColumns = result.SpriteColumns;
                part.VideoThumbnailWidth = result.Width;
                part.VideoThumbnailHeight = result.Height;
            }
        }

        if (!anySuccess) return;

        item.LastVideoThumbnailGenerationAt = DateTime.UtcNow;
        item.VideoThumbnailSpriteVersion = version;
        item.VideoThumbnailSpriteCount = firstResult!.SpriteCount;
        item.VideoThumbnailIntervalSeconds = firstResult.IntervalSeconds;
        item.VideoThumbnailSpriteColumns = firstResult.SpriteColumns;
        item.VideoThumbnailWidth = firstResult.Width;
        item.VideoThumbnailHeight = firstResult.Height;

        // An episode with no artwork gets a still grabbed from the middle of its
        // own runtime, so the season/episode pages show a real frame instead of a
        // placeholder. Use the first cut's representative file and duration.
        if (meta.IsEpisode && !meta.HasPoster && firstInput != null && firstResult.SourceDuration > TimeSpan.Zero)
        {
            var posterUrl = await TryExtractEpisodeStillAsync(mediaItemId, firstInput, firstResult.SourceDuration, cancellationToken);
            if (posterUrl != null)
            {
                item.PosterUrl = posterUrl;
                item.OriginalPosterUrl = posterUrl;
            }
        }

        await _mediaRepository.UpdateMediaItemAsync(item);

        await File.WriteAllTextAsync(_storage.GetVersionMarkerPath(mediaItemId), version);
        await _notifier.NotifyVideoThumbnailsReadyAsync(mediaItemId);
    }

    private const double ThumbnailCutToleranceSeconds = 5.0;

    private static List<List<MediaPart>> GroupPartsIntoCuts(List<MediaPart> partsSortedById)
    {
        var cuts = new List<List<MediaPart>>();
        foreach (var part in partsSortedById)
        {
            var dur = part.Duration?.TotalSeconds ?? -1;
            var cut = dur < 0
                ? null
                : cuts.FirstOrDefault(c =>
                {
                    var repDur = c[0].Duration?.TotalSeconds ?? -1;
                    return repDur >= 0 && Math.Abs(repDur - dur) <= ThumbnailCutToleranceSeconds;
                });

            if (cut == null)
            {
                cut = new List<MediaPart>();
                cuts.Add(cut);
            }
            cut.Add(part);
        }
        return cuts;
    }

    private async Task<string?> TryExtractEpisodeStillAsync(Guid mediaItemId, string input, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = $"media_{mediaItemId}_still_{Guid.NewGuid():N}.jpg";
            var outputPath = System.IO.Path.Combine(_customArtworkBase, fileName);
            var midpoint = TimeSpan.FromSeconds(duration.TotalSeconds / 2);
            var ok = await _generator.ExtractFrameAsync(input, midpoint, outputPath, cancellationToken);
            return ok ? $"/api/artwork/custom/{fileName}" : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract fallback still for episode {MediaItemId}", mediaItemId);
            return null;
        }
    }

    // A broken/unreadable source file will fail every scheduled pass, so the alert
    // is deduplicated on the item id: the admin gets one actionable notification
    // (which file to fix) rather than a fresh one on every run until it's cleared.
    private async Task RaiseThumbnailFailureAlertAsync(Guid mediaItemId, string input)
    {
        try
        {
            var title = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.Title) ?? "Unknown item";
            await _adminNotifications.RaiseAsync(
                AdminNotificationSeverity.Warning,
                "Video preview thumbnails failed",
                $"Could not generate scrub-bar thumbnails for \"{title}\". The source file may be corrupt or unreadable: {input}",
                $"{{\"thumbnailFailure\":\"{mediaItemId}\"}}",
                deduplicateByContext: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to raise admin notification for thumbnail failure {MediaItemId}", mediaItemId);
        }
    }

    private async Task<(LibraryType Type, bool Enabled)> GetLibraryThumbnailStateAsync(Guid libraryId)
    {
        var meta = await _libraryRepository.GetProjectedByIdAsync(libraryId, l => new
        {
            Type = l.Type,
            Enabled = l.EnableVideoPreviewThumbnails
        });
        return meta == null ? (LibraryType.Movie, false) : (meta.Type, meta.Enabled);
    }

    public static bool IsVideoBearingLibrary(LibraryType type) =>
        type == LibraryType.Movie || type == LibraryType.TvShow || type == LibraryType.HomeVideo;

    internal static string ComputeSpriteVersion(int interval, int width, int height, int quality, int columns)
    {
        // Keep the "v2-webp" token: the per-part sprite layout (#241) is backward
        // compatible — items with existing item-level sprites keep serving them via
        // the legacy fallback, and per-part layout is produced for newly generated
        // items and whenever a part is added. Bumping this token would mark every
        // item stale and force a full-library re-encode, which isn't wanted just to
        // relocate identical single-part sprites. Only change it when the sprite
        // bytes themselves must change (new size/interval/quality already vary it).
        var seed = string.Create(CultureInfo.InvariantCulture, $"v2-webp|{interval}|{width}|{height}|{quality}|{columns}");
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
