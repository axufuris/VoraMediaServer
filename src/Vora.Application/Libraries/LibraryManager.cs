using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Libraries.Requests;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Application.Watchers;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Libraries;

public interface ILibraryManager
{
    Task<Guid> CreateLibraryAsync(CreateLibraryRequest request);
    Task<IEnumerable<LibrarySummaryVM>> GetLibrariesAsync(bool hasAllAccess, List<Guid> allowedLibs);
    Task<MediaLibraryVM?> GetLibraryByIdAsync(Guid id);
    Task UpdateLibraryAsync(Guid id, UpdateLibraryRequest request);
    Task TriggerLibraryFolderAndFileScanAsync(Guid libraryId);
    Task<ScanFileResult> TriggerFileScanAsync(Guid libraryId, string filePath);
    Task DeleteLibraryAsync(Guid id);
    Task ToggleWatchingAsync(Guid libraryId, bool enable);
}

public class LibraryManager : ILibraryManager
{
    private readonly ILibraryRepository _repository;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFolderWatcherService _folderWatcher;
    private readonly ITaskQueueManager _taskQueueManager;
    private readonly IClientNotifier _notifier;

    public LibraryManager(ILibraryRepository repository, IServiceProvider serviceProvider, IFolderWatcherService folderWatcher, ITaskQueueManager taskQueueManager, IClientNotifier notifier)
    {
        _repository = repository;
        _serviceProvider = serviceProvider;
        _folderWatcher = folderWatcher;
        _taskQueueManager = taskQueueManager;
        _notifier = notifier;
    }

    public async Task<Guid> CreateLibraryAsync(CreateLibraryRequest request)
    {
        var regex = string.IsNullOrWhiteSpace(request.ScannerRegex)
            ? DefaultScannerRegex(request.Type)
            : request.ScannerRegex;

        var library = new MediaLibrary
        {
            Name = request.Name,
            Type = request.Type,
            FolderPaths = request.FolderPaths,
            ScannerRegex = regex,
            ExcludeFilters = request.ExcludeFilters ?? new List<string>(),

            MetadataProviderId = string.IsNullOrEmpty(request.MetadataProviderId) ? "tmdb_metadata" : request.MetadataProviderId,
            ThirdPartyRating1ProviderId = request.ThirdPartyRating1ProviderId,
            ThirdPartyRating2ProviderId = request.ThirdPartyRating2ProviderId,
            ArtworkProviderId = request.ArtworkProviderId,

            FindExtras = request.FindExtras,
            OnlyShowTrailers = request.OnlyShowTrailers,
            EnableVideoPreviewThumbnails = request.EnableVideoPreviewThumbnails && Vora.Application.Thumbnails.VideoThumbnailManager.IsVideoBearingLibrary(request.Type),
            EnableCreditsDetection = request.EnableCreditsDetection,
            MinimumCollectionSize = request.MinimumCollectionSize,
            EnableRealTimeWatching = request.EnableRealTimeWatching,

            EpisodeSorting = (Domain.Enums.EpisodeSortOrder)request.EpisodeSorting,
            EpisodeOrder = (Domain.Enums.EpisodeOrdering)request.EpisodeOrder,
            UseSeasonTitles = request.UseSeasonTitles,
            SeasonsDisplay = (Domain.Enums.SeasonDisplayMode)request.SeasonsDisplay,
            EnableIntroDetection = request.EnableIntroDetection
        };

        var id = await _repository.CreateLibraryAsync(library);

        if (request.EnableRealTimeWatching)
        {
            _folderWatcher.StartWatching(id, request.FolderPaths);
        }

        if (request.FolderPaths != null && request.FolderPaths.Any())
        {
            _taskQueueManager.QueueLibraryAdded(id, library.Name);
        }

        return id;
    }

    public async Task<IEnumerable<LibrarySummaryVM>> GetLibrariesAsync(bool hasAllAccess, List<Guid> allowedLibs)
    {
        var summaries = await _repository.GetAllProjectedAsync(LibrarySummaryVM.Projection, hasAllAccess, allowedLibs);

        foreach (var summary in summaries) summary.IsBeingWatched = _folderWatcher.IsWatching(summary.Id);
        return summaries;
    }

    public async Task<MediaLibraryVM?> GetLibraryByIdAsync(Guid id)
    {
        var library = await _repository.GetProjectedByIdAsync(id, MediaLibraryVM.Projection);
        if (library == null) return null;

        library.IsBeingWatched = _folderWatcher.IsWatching(library.Id);
        return library;
    }

    private static string DefaultScannerRegex(LibraryType type) => type switch
    {
        LibraryType.Movie => @"^(?<Title>.*?(?=\s*\(\d{4}\)|\s*\{|\s*\[|$))(?:\s*\((?<Year>\d{4})\))?(?:\s*\{(?<Provider>imdb|tmdb|tvdb)-(?<ProviderId>[^}]+)\})?",
        LibraryType.TvShow => @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$",
        LibraryType.Music => @"^(?<Artist>[^_]+)_(?<Album>[^_]+)_(?:(?<Disc>\d{1,2})-)?(?<Track>\d{1,3})_(?<TrackTitle>.+)",
        _ => @"^(?<Title>.+)"
    };

    public async Task UpdateLibraryAsync(Guid id, UpdateLibraryRequest request)
    {
        var library = await _repository.GetForUpdateAsync(id);
        if (library == null) throw new InvalidOperationException("Library not found");

        var previousPaths = (library.FolderPaths ?? new List<string>()).ToList();
        var newPaths = request.FolderPaths ?? new List<string>();

        var addedPaths = newPaths.Except(previousPaths, StringComparer.OrdinalIgnoreCase).ToList();
        var removedPaths = previousPaths.Except(newPaths, StringComparer.OrdinalIgnoreCase).ToList();
        var pathsChanged = addedPaths.Any() || removedPaths.Any();

        bool artworkProviderChanged = library.ArtworkProviderId != request.ArtworkProviderId;
        var requestedThumbnails = request.EnableVideoPreviewThumbnails &&
            Vora.Application.Thumbnails.VideoThumbnailManager.IsVideoBearingLibrary(library.Type);
        bool thumbnailsTurnedOff = library.EnableVideoPreviewThumbnails && !requestedThumbnails;

        library.Name = request.Name;
        library.FolderPaths = newPaths;
        library.ExcludeFilters = request.ExcludeFilters ?? new List<string>();
        library.MetadataProviderId = request.MetadataProviderId;
        library.FindExtras = request.FindExtras;
        library.OnlyShowTrailers = request.OnlyShowTrailers;
        library.EnableVideoPreviewThumbnails = requestedThumbnails;
        library.EnableCreditsDetection = request.EnableCreditsDetection;
        library.MinimumCollectionSize = request.MinimumCollectionSize;

        library.EpisodeSorting = (Domain.Enums.EpisodeSortOrder)request.EpisodeSorting;
        library.EpisodeOrder = (Domain.Enums.EpisodeOrdering)request.EpisodeOrder;
        library.UseSeasonTitles = request.UseSeasonTitles;
        library.SeasonsDisplay = (Domain.Enums.SeasonDisplayMode)request.SeasonsDisplay;
        library.EnableIntroDetection = request.EnableIntroDetection;
        library.ThirdPartyRating1ProviderId = request.ThirdPartyRating1ProviderId;
        library.ThirdPartyRating2ProviderId = request.ThirdPartyRating2ProviderId;
        library.ArtworkProviderId = request.ArtworkProviderId;

        library.EnableRealTimeWatching = request.EnableRealTimeWatching;
        library.ScannerRegex = string.IsNullOrWhiteSpace(request.ScannerRegex)
            ? DefaultScannerRegex(library.Type)
            : request.ScannerRegex;

        await _repository.UpdateLibraryAsync(library);

        if (removedPaths.Any())
        {
            await _repository.CleanUpOrphanedMediaAsync(library.Id);
            await _notifier.NotifyLibraryUpdatedAsync(library.Id);
        }

        if (pathsChanged && newPaths.Any())
        {
            _taskQueueManager.QueueLibraryUpdated(library.Id, library.Name);
        }

        if (library.EnableRealTimeWatching)
        {
            _folderWatcher.StopWatching(library.Id);
            _folderWatcher.StartWatching(library.Id, newPaths);
        }
        else
        {
            _folderWatcher.StopWatching(library.Id);
        }

        if (artworkProviderChanged)
        {
            _taskQueueManager.QueueArtworkProviderSwap(library.Id, library.Name);
        }

        if (thumbnailsTurnedOff)
        {
            using var purgeScope = _serviceProvider.CreateScope();
            var thumbnailManager = purgeScope.ServiceProvider.GetRequiredService<Vora.Application.Thumbnails.IVideoThumbnailManager>();
            await thumbnailManager.PurgeLibraryThumbnailsAsync(library.Id);
        }
    }

    public async Task DeleteLibraryAsync(Guid id)
    {
        using var scope = _serviceProvider.CreateScope();
        var thumbnailManager = scope.ServiceProvider.GetRequiredService<Vora.Application.Thumbnails.IVideoThumbnailManager>();
        await thumbnailManager.PurgeLibraryThumbnailsAsync(id);

        await _repository.DeleteLibraryAsync(id);
    }

    public async Task TriggerLibraryFolderAndFileScanAsync(Guid libraryId)
    {
        var library = await _repository.GetProjectedByIdAsync(libraryId, l => new { l.Id, l.Name, l.Type });
        if (library == null) return;

        using var scope = _serviceProvider.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();

        var settings = await settingsRepo.GetSettingsAsync();
        var activeScannerId = settings.LocalMediaScannerProviderId;

        var scanners = scope.ServiceProvider.GetServices<ILocalMediaScannerProvider>();
        var scanner = scanners.FirstOrDefault(s => s.Id == activeScannerId) ?? scanners.FirstOrDefault();

        if (scanner == null) throw new InvalidOperationException("No Local Media Scanner plugins are installed!");

        if (library.Type == LibraryType.Movie)
            await scanner.ScanMovieLibraryAsync(library.Id);
        else if (library.Type == LibraryType.TvShow)
            await scanner.ScanTvShowLibraryAsync(library.Id);
        else if (library.Type == LibraryType.Music)
            await scanner.ScanMusicLibraryAsync(library.Id);

        await notifier.NotifyLibraryUpdatedAsync(library.Id);
    }

    public async Task<ScanFileResult> TriggerFileScanAsync(Guid libraryId, string filePath)
    {
        var library = await _repository.GetProjectedByIdAsync(libraryId, l => new { l.Id, l.Type });
        if (library == null) return ScanFileResult.None;

        using var scope = _serviceProvider.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();

        var settings = await settingsRepo.GetSettingsAsync();
        var activeScannerId = settings.LocalMediaScannerProviderId;

        var scanners = scope.ServiceProvider.GetServices<ILocalMediaScannerProvider>();
        var scanner = scanners.FirstOrDefault(s => s.Id == activeScannerId) ?? scanners.FirstOrDefault();
        if (scanner == null) return ScanFileResult.None;

        ScanFileResult result;
        if (library.Type == LibraryType.Movie)
        {
            var movieId = await scanner.ScanMovieFileAsync(libraryId, filePath);
            result = new ScanFileResult(movieId, null, false);
        }
        else if (library.Type == LibraryType.TvShow)
        {
            result = await scanner.ScanTvFileAsync(libraryId, filePath);
        }
        else
        {
            // Music (and anything else) — the watcher only fires for video
            // files today; nothing to single-file ingest here.
            result = ScanFileResult.None;
        }

        if (result.MediaItemId != null) await notifier.NotifyLibraryUpdatedAsync(libraryId);
        return result;
    }

    public async Task ToggleWatchingAsync(Guid libraryId, bool enable)
    {
        var library = await _repository.GetForUpdateAsync(libraryId);
        if (library == null) return;

        library.EnableRealTimeWatching = enable;
        await _repository.UpdateLibraryAsync(library);

        if (enable)
            _folderWatcher.StartWatching(libraryId, library.FolderPaths);
        else
            _folderWatcher.StopWatching(libraryId);
    }
}