using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    Task UpdateMediaMetadataAsync(Guid id, UpdateMediaRequest request);
    Task UpdateSeasonMetadataAsync(Guid id, UpdateSeasonRequest request);
    Task DeleteMediaAsync(Guid id);
    Task TriggerTargetedScanAsync(Guid id);
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
    private readonly IConfiguration _config;
    private readonly ILogger<MediaManager> _logger;

    public MediaManager(
        IMediaRepository repository,
        IUserMediaStateRepository stateRepository,
        IClientNotifier notifier,
        ITaskQueueManager taskQueueManager,
        ISystemSettingsRepository settingsRepository,
        IEnumerable<ILocalMediaScannerProvider> scanners,
        IConfiguration config,
        ILogger<MediaManager> logger)
    {
        _repository = repository;
        _stateRepository = stateRepository;
        _notifier = notifier;
        _taskQueueManager = taskQueueManager;
        _settingsRepository = settingsRepository;
        _scanners = scanners;
        _config = config;
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

        season.Title = request.Title;
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

        await _repository.DeleteMediaItemAsync(id);
        await _notifier.NotifyLibraryUpdatedAsync(libraryId);
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
        item.OriginalPosterUrl = newPosterUrl;
        item.PosterUrl = newPosterUrl;
    }

    private void ApplyBackgroundChange(MediaItem item, string? newBackgroundUrl)
    {
        if (string.IsNullOrEmpty(newBackgroundUrl) || newBackgroundUrl == item.BackgroundUrl) return;

        if (item is Episode)
        {
            CleanupOrphanedOverlay(item);
            item.OriginalPosterUrl = newBackgroundUrl;
        }
        item.BackgroundUrl = newBackgroundUrl;
    }

    private void CleanupOrphanedOverlay(MediaItem item)
    {
        var configPath = _config["StoragePaths:CustomArtwork"];
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
