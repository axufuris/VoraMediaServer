using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    private readonly IMediaRepository _mediaRepository;
    private readonly ILibraryRepository _libraryRepository;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IVideoThumbnailStorageService _storage;
    private readonly IVideoThumbnailGeneratorService _generator;
    private readonly IClientNotifier _notifier;
    private readonly ILogger<VideoThumbnailManager> _logger;

    public VideoThumbnailManager(
        IMediaRepository mediaRepository,
        ILibraryRepository libraryRepository,
        ISystemSettingsRepository settingsRepo,
        IVideoThumbnailStorageService storage,
        IVideoThumbnailGeneratorService generator,
        IClientNotifier notifier,
        ILogger<VideoThumbnailManager> logger)
    {
        _mediaRepository = mediaRepository;
        _libraryRepository = libraryRepository;
        _settingsRepo = settingsRepo;
        _storage = storage;
        _generator = generator;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task TriggerMediaItemThumbnailGenerationAsync(Guid mediaItemId, bool forceOverride = false, bool isScheduleTrigger = false)
    {
        var itemType = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.GetType().Name);

        if (itemType == nameof(TvShow))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForShowAsync(mediaItemId);
            foreach (var epId in epIds)
            {
                await RunSingleAsync(epId, forceOverride);
            }
            return;
        }

        if (itemType == nameof(Season))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(mediaItemId);
            foreach (var epId in epIds)
            {
                await RunSingleAsync(epId, forceOverride);
            }
            return;
        }

        if (itemType != nameof(Movie) && itemType != nameof(Episode)) return;

        await RunSingleAsync(mediaItemId, forceOverride);
    }

    public async Task TriggerLibraryThumbnailGenerationAsync(Guid libraryId, bool forceOverride = false, bool isScheduleTrigger = false)
    {
        var (libraryType, enabled) = await GetLibraryThumbnailStateAsync(libraryId);
        if (!IsVideoBearingLibrary(libraryType)) return;
        if (!enabled && !forceOverride) return;

        var ids = await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId);
        foreach (var id in ids)
        {
            try
            {
                var itemType = await _mediaRepository.GetProjectedAsync(id, m => m.GetType().Name);
                if (itemType != nameof(Movie) && itemType != nameof(Episode)) continue;
                await RunSingleAsync(id, forceOverride);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video thumbnail generation failed for {MediaItemId}.", id);
            }
        }
    }

    public async Task<(int Total, int WithThumbnails)> GetCoverageAsync(Guid libraryId)
    {
        var (libraryType, _) = await GetLibraryThumbnailStateAsync(libraryId);
        if (!IsVideoBearingLibrary(libraryType)) return (0, 0);

        var counts = await _mediaRepository.GetAllProjectedAsync(
            m => new
            {
                Type = m.GetType().Name,
                Has = m.LastVideoThumbnailGenerationAt != null
            },
            libraryId: libraryId);

        var list = counts
            .Where(c => c.Type == nameof(Movie) || c.Type == nameof(Episode))
            .ToList();

        return (list.Count, list.Count(c => c.Has));
    }

    private async Task PurgeMediaItemThumbnailsAsync(Guid mediaItemId)
    {
        _storage.DeleteItemDirectory(mediaItemId);

        var item = await _mediaRepository.GetForBasicUpdateAsync(mediaItemId);
        if (item == null) return;

        item.LastVideoThumbnailGenerationAt = null;
        item.VideoThumbnailSpriteVersion = null;
        item.VideoThumbnailSpriteCount = 0;
        item.VideoThumbnailIntervalSeconds = 0;
        item.VideoThumbnailSpriteColumns = 0;
        item.VideoThumbnailWidth = 0;
        item.VideoThumbnailHeight = 0;

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

    private async Task RunSingleAsync(Guid mediaItemId, bool forceOverride)
    {
        var meta = await _mediaRepository.GetProjectedAsync(mediaItemId, m => new
        {
            LibraryType = m.Library.Type,
            LibraryEnabled = m.Library.EnableVideoPreviewThumbnails,
            LockedFields = m.LockedFields,
            CurrentVersion = m.VideoThumbnailSpriteVersion,
            CurrentGeneratedAt = m.LastVideoThumbnailGenerationAt
        });

        if (meta == null) return;
        if (!IsVideoBearingLibrary(meta.LibraryType)) return;
        if (!meta.LibraryEnabled && !forceOverride) return;
        if (meta.LockedFields != null && meta.LockedFields.Contains(ThumbnailsLockField, StringComparer.OrdinalIgnoreCase)) return;

        var settings = await _settingsRepo.GetSettingsAsync();
        var version = ComputeSpriteVersion(settings.VideoThumbnailIntervalSeconds, settings.VideoThumbnailWidth, settings.VideoThumbnailHeight, settings.VideoThumbnailJpegQuality, settings.VideoThumbnailSpriteColumns);

        if (!forceOverride && meta.CurrentVersion == version && _storage.HasGeneratedAssets(mediaItemId))
        {
            return;
        }

        var filePaths = await _mediaRepository.GetMediaFilePathsAsync(mediaItemId);
        if (filePaths == null || filePaths.Count == 0) return;
        var input = filePaths[0];
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            _logger.LogWarning("Skipping video thumbnail generation for {MediaItemId}: file not found at {Input}", mediaItemId, input);
            return;
        }

        _storage.EnsureItemDirectory(mediaItemId);
        var spritePath = _storage.GetSpritePath(mediaItemId);
        var vttPath = _storage.GetVttPath(mediaItemId);

        VideoThumbnailGenerationResult result;
        try
        {
            result = await _generator.GenerateAsync(
                new VideoThumbnailGenerationParameters
                {
                    InputPath = input,
                    IntervalSeconds = settings.VideoThumbnailIntervalSeconds,
                    Width = settings.VideoThumbnailWidth,
                    Height = settings.VideoThumbnailHeight,
                    JpegQuality = settings.VideoThumbnailJpegQuality,
                    SpriteColumns = settings.VideoThumbnailSpriteColumns
                },
                spritePath,
                vttPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ffmpeg generation failed for {MediaItemId} ({Input})", mediaItemId, input);
            return;
        }

        await File.WriteAllTextAsync(_storage.GetVersionMarkerPath(mediaItemId), version);

        var item = await _mediaRepository.GetForBasicUpdateAsync(mediaItemId);
        if (item != null)
        {
            item.LastVideoThumbnailGenerationAt = DateTime.UtcNow;
            item.VideoThumbnailSpriteVersion = version;
            item.VideoThumbnailSpriteCount = result.SpriteCount;
            item.VideoThumbnailIntervalSeconds = result.IntervalSeconds;
            item.VideoThumbnailSpriteColumns = result.SpriteColumns;
            item.VideoThumbnailWidth = result.Width;
            item.VideoThumbnailHeight = result.Height;
            await _mediaRepository.UpdateMediaItemAsync(item);
        }

        await _notifier.NotifyVideoThumbnailsReadyAsync(mediaItemId);
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
        var seed = string.Create(CultureInfo.InvariantCulture, $"v1|{interval}|{width}|{height}|{quality}|{columns}");
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
