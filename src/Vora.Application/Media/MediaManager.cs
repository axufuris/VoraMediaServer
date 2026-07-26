using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IMediaManager
{
    Task<MediaDetailsVM?> GetMediaItemAsync(Guid id, Guid? profileId, bool hasAllAccess, List<Guid> allowedLibs, bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated);
    Task<IEnumerable<LibraryItemVM>> GetLibraryContentAsync(Guid libraryId, Guid? profileId, bool hasAllAccess, List<Guid> allowedLibs, bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated);
    Task<MediaDetailsVM?> GetMediaItemAsync(Guid id);
    Task<IEnumerable<LibraryItemVM>> GetLibraryContentAsync(Guid libraryId);
    Task<SeasonDetailsVM?> GetSeasonDetailsAsync(Guid seasonId);
    Task<List<MediaMarkerVM>> GetMarkersAsync(Guid mediaItemId);
    Task ReplaceMarkersAsync(Guid mediaItemId, IEnumerable<MediaMarkerVM> markers);
    Task<bool> GetMarkersLockedAsync(Guid mediaItemId);
    Task SetMarkersLockedAsync(Guid mediaItemId, bool locked);
    Task<MarkerCoverageVM> GetLibraryMarkerCoverageAsync(Guid libraryId);
    Task UpdateMediaMetadataAsync(Guid id, UpdateMediaRequest request);
    Task UpdateSeasonMetadataAsync(Guid id, UpdateSeasonRequest request);
    Task DeleteMediaAsync(Guid id);
    Task TriggerTargetedScanAsync(Guid id);
    Task<List<TrashMediaItemVM>> GetTrashAsync();
    Task RestoreFromTrashAsync(Guid id);
    Task<int> PurgeExpiredTrashAsync(int retentionDays);
}

public class MediaManager : IMediaManager
{
    private const string OverlayMarker = "_overlay_";
    private const string CustomArtworkUrlPrefix = "/api/artwork/custom/";
    private const string DefaultStorageRoot = "Storage";
    private const string DefaultCustomArtworkFolder = "CustomArtwork";

    private readonly IMediaRepository _repository;
    private readonly IUserMediaStateRepository _stateRepository;
    private readonly IClientNotifier _notifier;
    private readonly ITaskQueueManager _taskQueueManager;
    private readonly ISystemSettingsRepository _settingsRepository;
    private readonly IEnumerable<ILocalMediaScannerProvider> _scanners;
    private readonly StoragePathsOptions _storagePaths;
    private readonly Vora.Application.Thumbnails.IVideoThumbnailStorageService _thumbnailStorage;
    private readonly Vora.Application.Artwork.IArtworkThumbnailService _artworkThumbnails;
    private readonly ILogger<MediaManager> _logger;

    public MediaManager(
        IMediaRepository repository,
        IUserMediaStateRepository stateRepository,
        IClientNotifier notifier,
        ITaskQueueManager taskQueueManager,
        ISystemSettingsRepository settingsRepository,
        IEnumerable<ILocalMediaScannerProvider> scanners,
        IOptions<StoragePathsOptions> storagePaths,
        Vora.Application.Thumbnails.IVideoThumbnailStorageService thumbnailStorage,
        Vora.Application.Artwork.IArtworkThumbnailService artworkThumbnails,
        ILogger<MediaManager> logger)
    {
        _repository = repository;
        _stateRepository = stateRepository;
        _notifier = notifier;
        _taskQueueManager = taskQueueManager;
        _settingsRepository = settingsRepository;
        _scanners = scanners;
        _storagePaths = storagePaths.Value;
        _thumbnailStorage = thumbnailStorage;
        _artworkThumbnails = artworkThumbnails;
        _logger = logger;
    }

    public async Task<MediaDetailsVM?> GetMediaItemAsync(Guid id, Guid? profileId, bool hasAllAccess, List<Guid> allowedLibs, bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated)
    {
        var vm = await _repository.GetProjectedAsync(id, MediaDetailsVM.Projection, hasAllAccess, allowedLibs, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated);

        if (vm == null || !profileId.HasValue) return vm;

        await _stateRepository.AttachUserMediaStateAsync(vm, profileId.Value);
        return vm;
    }

    public async Task<IEnumerable<LibraryItemVM>> GetLibraryContentAsync(Guid libraryId, Guid? profileId, bool hasAllAccess, List<Guid> allowedLibs, bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated)
    {
        var items = await _repository.GetAllProjectedAsync(LibraryItemVM.Projection, libraryId, null, hasAllAccess, allowedLibs, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated);
        var groupedItems = items.GroupBy(e => $"{e.Title.ToLower().Trim()}_{e.ReleaseDate?.Year}").Select(g => g.First()).ToList();

        if (profileId.HasValue) await _stateRepository.AttachLibraryItemUserStatesAsync(groupedItems, profileId.Value);

        return groupedItems;
    }

    public Task<MediaDetailsVM?> GetMediaItemAsync(Guid id) =>
        _repository.GetProjectedAsync(id, MediaDetailsVM.Projection);

    public async Task<IEnumerable<LibraryItemVM>> GetLibraryContentAsync(Guid libraryId)
    {
        var items = await _repository.GetAllProjectedAsync(LibraryItemVM.Projection, libraryId);
        return items.GroupBy(e => $"{e.Title.ToLower().Trim()}_{e.ReleaseDate?.Year}").Select(g => g.First()).ToList();
    }

    public Task<SeasonDetailsVM?> GetSeasonDetailsAsync(Guid seasonId) =>
        _repository.GetProjectedAsync(seasonId, SeasonDetailsVM.Projection);

    public async Task<List<MediaMarkerVM>> GetMarkersAsync(Guid mediaItemId)
    {
        var markers = await _repository.GetMarkersAsync(mediaItemId);
        return markers
            .OrderBy(m => m.Start)
            .Select(m => new MediaMarkerVM
            {
                Type = m.Type.ToString(),
                StartSeconds = m.Start.TotalSeconds,
                EndSeconds = m.End.TotalSeconds,
                Order = m.Order
            })
            .ToList();
    }

    public async Task ReplaceMarkersAsync(Guid mediaItemId, IEnumerable<MediaMarkerVM> markers)
    {
        var entities = markers
            .Where(m => m.EndSeconds > m.StartSeconds && m.StartSeconds >= 0)
            .Select(m => new Domain.Entities.Media.MediaItemMarker
            {
                MediaItemId = mediaItemId,
                Type = Enum.TryParse<Domain.Entities.Media.MarkerType>(m.Type, out var t) ? t : Domain.Entities.Media.MarkerType.Intro,
                Start = TimeSpan.FromSeconds(m.StartSeconds),
                End = TimeSpan.FromSeconds(m.EndSeconds),
                Order = m.Order
            })
            .ToList();
        await _repository.ReplaceMarkersAsync(mediaItemId, entities);
        await _notifier.NotifyMediaAnalysisUpdatedAsync(mediaItemId);
    }

    public Task<bool> GetMarkersLockedAsync(Guid mediaItemId) =>
        _repository.AreMarkersLockedAsync(mediaItemId);

    public async Task SetMarkersLockedAsync(Guid mediaItemId, bool locked)
    {
        await _repository.SetMarkersLockedAsync(mediaItemId, locked);
        await _notifier.NotifyMediaAnalysisUpdatedAsync(mediaItemId);
    }

    public Task<MarkerCoverageVM> GetLibraryMarkerCoverageAsync(Guid libraryId) =>
        _repository.GetMarkerCoverageAsync(libraryId);

    public async Task UpdateMediaMetadataAsync(Guid id, UpdateMediaRequest request)
    {
        var item = await _repository.GetForBasicUpdateAsync(id);
        if (item == null) throw new InvalidOperationException("Media item not found.");

        ApplyPosterChange(item, request.PosterUrl);
        ApplyBackgroundChange(item, request.BackgroundUrl);

        item.Title = request.Title;
        item.SortTitle = request.SortTitle;
        item.Overview = request.Overview;
        item.OriginalTitle = request.OriginalTitle;
        item.OriginalLanguage = request.OriginalLanguage;
        item.Status = request.Status;
        item.Tagline = request.Tagline;
        item.HomePage = request.HomePage;
        item.ContentRating = request.ContentRating;
        item.ReleaseDate = request.ReleaseDate;
        item.LockedFields = request.LockedFields;

        await _repository.UpdateMediaItemAsync(item);

        _taskQueueManager.QueueGeneratePosterOverlays(item.Id);

        await _notifier.NotifyMediaItemUpdatedAsync(item.Id);
    }

    public async Task UpdateSeasonMetadataAsync(Guid id, UpdateSeasonRequest request)
    {
        var season = await _repository.GetForBasicUpdateAsync(id);
        if (season == null) throw new InvalidOperationException("Season not found.");

        ApplyPosterChange(season, request.PosterUrl);

        season.Title = request.Title ?? season.Title;
        season.Overview = request.Overview;
        season.LockedFields = request.LockedFields;

        await _repository.UpdateMediaItemAsync(season);

        _taskQueueManager.QueueGeneratePosterOverlays(season.Id);

        await _notifier.NotifyMediaItemUpdatedAsync(id);
    }

    public async Task DeleteMediaAsync(Guid id)
    {
        var item = await _repository.GetForBasicUpdateAsync(id);
        if (item == null) return;

        var libraryId = item.LibraryId;

        CleanupOrphanedOverlay(item);
        _artworkThumbnails.RemoveThumbnailsForSource(item.PosterUrl);
        _artworkThumbnails.RemoveThumbnailsForSource(item.BackgroundUrl);
        _thumbnailStorage.DeleteItemDirectory(id);

        await _repository.DeleteMediaItemAsync(id);
        await _notifier.NotifyLibraryUpdatedAsync(libraryId);
    }

    public Task<List<TrashMediaItemVM>> GetTrashAsync() =>
        _repository.GetMissingMediaAsync();

    public async Task RestoreFromTrashAsync(Guid id)
    {
        var item = await _repository.GetForBasicUpdateAsync(id);
        if (item == null || item.MissingSince == null) return;

        await _repository.RestoreMissingMediaAsync(id);
        await _notifier.NotifyLibraryUpdatedAsync(item.LibraryId);
    }

    public async Task<int> PurgeExpiredTrashAsync(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var expiredIds = await _repository.GetExpiredMissingMediaIdsAsync(cutoff);

        foreach (var expiredId in expiredIds)
        {
            await DeleteMediaAsync(expiredId);
        }

        return expiredIds.Count;
    }

    public async Task TriggerTargetedScanAsync(Guid id)
    {
        var item = await _repository.GetForBasicUpdateAsync(id);
        if (item == null) return;

        var libraryId = item.LibraryId;

        var settings = await _settingsRepository.GetSettingsAsync();
        var activeScannerId = settings.LocalMediaScannerProviderId;

        var scanner = _scanners.FirstOrDefault(s => s.Id == activeScannerId) ?? _scanners.FirstOrDefault();

        if (scanner == null)
        {
            throw new InvalidOperationException("No Local Media Scanner plugins are installed.");
        }

        if (item is Movie) await scanner.ScanMovieAsync(id);
        else if (item is TvShow) await scanner.ScanTvShowAsync(id);
        else if (item is Season) await scanner.ScanSeasonAsync(id);
        else if (item is Episode) await scanner.ScanEpisodeAsync(id);

        await _notifier.NotifyMediaItemUpdatedAsync(id);
        await _notifier.NotifyLibraryUpdatedAsync(libraryId);
    }

    private void ApplyPosterChange(MediaItem item, string? newPosterUrl)
    {
        if (string.IsNullOrEmpty(newPosterUrl) || newPosterUrl == item.PosterUrl) return;

        CleanupOrphanedOverlay(item);
        _artworkThumbnails.RemoveThumbnailsForSource(item.PosterUrl);
        item.OriginalPosterUrl = newPosterUrl;
        item.PosterUrl = newPosterUrl;
    }

    private void ApplyBackgroundChange(MediaItem item, string? newBackgroundUrl)
    {
        if (string.IsNullOrEmpty(newBackgroundUrl) || newBackgroundUrl == item.BackgroundUrl) return;

        if (item is Episode)
        {
            CleanupOrphanedOverlay(item);
            _artworkThumbnails.RemoveThumbnailsForSource(item.PosterUrl);
            item.OriginalPosterUrl = newBackgroundUrl;
        }
        _artworkThumbnails.RemoveThumbnailsForSource(item.BackgroundUrl);
        item.BackgroundUrl = newBackgroundUrl;
    }

    private void CleanupOrphanedOverlay(MediaItem item)
    {
        var configPath = _storagePaths.CustomArtwork;
        var overlayDir = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : Path.Combine(AppContext.BaseDirectory, DefaultStorageRoot, DefaultCustomArtworkFolder);

        var urlsToCheck = new[] { item.PosterUrl, item.BackgroundUrl };

        foreach (var url in urlsToCheck)
        {
            if (string.IsNullOrEmpty(url) || !url.Contains(OverlayMarker) || !url.StartsWith(CustomArtworkUrlPrefix)) continue;

            var fileName = url.Split('/').Last();
            var physicalPath = Path.Combine(overlayDir, fileName);
            if (!File.Exists(physicalPath)) continue;

            try
            {
                File.Delete(physicalPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete orphaned overlay file at {Path}.", physicalPath);
            }
        }
    }
}
