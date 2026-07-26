using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Actors;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Metadata;

public interface IMetadataManager
{
    Task TriggerLibraryMetadataRefreshAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    Task TriggerActorMetadataRefreshAsync();
    Task TriggerLibraryRatingsRefreshAsync(Guid libraryId, string? name = null, bool forceOverride = false);
    Task TriggerMediaItemArtworkRefreshAsync(Guid mediaItemId, bool forceOverride = false);
    Task TriggerMediaItemMetadataRefreshAsync(Guid mediaItemId, bool forceOverride = false);
    Task TriggerLibraryArtworkRefreshAsync(Guid libraryId, bool forceOverride = false);
    Task TriggerMediaItemRatingsRefreshAsync(Guid mediaItemId, bool forceOverride = false);
    Task RefreshMetadataAsync(Guid mediaItemId, bool forceOverride = false);
}

public class MetadataManager : IMetadataManager
{
    private const int ActorBatchSize = 50;
    private const int NotificationBatchSize = 10;

    private readonly IMediaRepository _repository;
    private readonly IActorRepository _actorRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClientNotifier _notifier;
    private readonly IMetadataFetchService _fetchService;
    private readonly IMetadataMappingService _mappingService;
    private readonly Vora.Plugins.Interfaces.ITaskProgressReporter _progress;
    private readonly ILogger<MetadataManager> _logger;

    public MetadataManager(
        IMediaRepository repository,
        IActorRepository actorRepository,
        IServiceScopeFactory scopeFactory,
        IClientNotifier notifier,
        IMetadataFetchService fetchService,
        IMetadataMappingService mappingService,
        Vora.Plugins.Interfaces.ITaskProgressReporter progress,
        ILogger<MetadataManager> logger)
    {
        _repository = repository;
        _actorRepository = actorRepository;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _fetchService = fetchService;
        _mappingService = mappingService;
        _progress = progress;
        _logger = logger;
    }

    public async Task TriggerActorMetadataRefreshAsync()
    {
        var ids = await _actorRepository.GetActorIdsMissingMetadataAsync(ActorBatchSize);
        foreach (var id in ids)
        {
            try
            {
                var actor = await _actorRepository.GetActorByIdAsync(id);
                if (actor == null || actor.IsCustom) continue;

                var metadata = await _fetchService.GetActorMetadataAsync(actor.TmdbId);
                if (metadata == null) continue;

                actor.Biography = metadata.Biography;
                actor.Birthday = metadata.Birthday;
                actor.Deathday = metadata.Deathday;
                actor.PlaceOfBirth = metadata.PlaceOfBirth;
                actor.ImdbId = metadata.ImdbId;
                actor.HomePage = metadata.HomePage;

                await _actorRepository.UpdateActorAsync(actor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Actor metadata refresh failed for {ActorId}.", id);
            }
        }
    }

    public async Task TriggerLibraryMetadataRefreshAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        var ids = forceOverride
            ? await _repository.GetAllProjectedAsync(n => n.Id, libraryId)
            : await _repository.GetMediaIdsMissingMetadataAsync(libraryId);

        await ProcessLibraryItemsAsync(libraryId, ids, "metadata", async id =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
            await scopedManager.RefreshMetadataAsync(id, forceOverride);
        });
    }

    public async Task TriggerLibraryRatingsRefreshAsync(Guid libraryId, string? name = null, bool forceOverride = false)
    {
        // Non-force runs only fetch items still missing their primary rating, so
        // re-runs fill in items skipped when the provider's daily quota tripped
        // — instead of re-spending the quota on already-rated items every pass.
        var ids = forceOverride
            ? await _repository.GetAllProjectedAsync(n => n.Id, libraryId)
            : await _repository.GetMediaIdsMissingRatingsAsync(libraryId);

        await ProcessLibraryItemsAsync(libraryId, ids, "ratings", id => RefreshRatingsAsync(id, forceOverride));
    }

    public async Task TriggerLibraryArtworkRefreshAsync(Guid libraryId, bool forceOverride = false)
    {
        var ids = await _repository.GetAllProjectedAsync(n => n.Id, libraryId);
        await ProcessLibraryItemsAsync(libraryId, ids, "artwork", id => RefreshArtworkAsync(id, forceOverride));
    }

    public async Task TriggerMediaItemMetadataRefreshAsync(Guid mediaItemId, bool forceOverride = false)
    {
        var itemInfo = await _repository.GetProjectedAsync(mediaItemId, m => new { m.Id, Type = m.GetType().Name });
        if (itemInfo == null) return;

        if (itemInfo.Type == nameof(Season))
        {
            var season = await _repository.GetForBasicUpdateAsync(mediaItemId) as Season;
            if (season != null) await TriggerMediaItemMetadataRefreshAsync(season.TvShowId, forceOverride);
            return;
        }

        await RefreshMetadataAsync(mediaItemId, forceOverride);

        if (itemInfo.Type == nameof(TvShow))
        {
            var tvShow = await _repository.GetForMetadataSyncAsync(mediaItemId) as TvShow;
            if (tvShow != null)
            {
                foreach (var season in tvShow.Seasons)
                {
                    await RefreshArtworkAsync(season.Id, forceOverride);
                    await RefreshRatingsAsync(season.Id, forceOverride);
                }
            }
            var episodeIds = await _repository.GetEpisodeIdsForShowAsync(mediaItemId);
            foreach (var epId in episodeIds) await RefreshMetadataAsync(epId, forceOverride);
        }
    }

    public async Task TriggerMediaItemArtworkRefreshAsync(Guid mediaItemId, bool forceOverride = false)
    {
        var itemInfo = await _repository.GetProjectedAsync(mediaItemId, m => new { m.Id, Type = m.GetType().Name });
        if (itemInfo == null) return;

        await RefreshArtworkAsync(mediaItemId, forceOverride);

        if (itemInfo.Type == nameof(TvShow))
        {
            await RefreshChildSeasonsAsync(mediaItemId, forceOverride, RefreshArtworkAsync);
        }
    }

    public async Task TriggerMediaItemRatingsRefreshAsync(Guid mediaItemId, bool forceOverride = false)
    {
        var itemInfo = await _repository.GetProjectedAsync(mediaItemId, m => new { m.Id, Type = m.GetType().Name });
        if (itemInfo == null) return;

        await RefreshRatingsAsync(mediaItemId, forceOverride);

        if (itemInfo.Type == nameof(TvShow))
        {
            await RefreshChildSeasonsAsync(mediaItemId, forceOverride, RefreshRatingsAsync);
        }
    }

    public async Task RefreshMetadataAsync(Guid mediaItemId, bool forceOverride = false)
    {
        var item = await _repository.GetForMetadataSyncAsync(mediaItemId);
        if (item == null) return;

        if (!forceOverride && item.LastMetadataRefresh.HasValue) return;

        var textFetch = await _fetchService.GetTextMetadataAsync(item);

        if (textFetch.Metadata != null)
        {
            await _mappingService.ApplyTextMetadataAsync(item, textFetch.Metadata, forceOverride, textFetch.ProviderId, textFetch.ProviderName);
            await _repository.UpdateMediaItemAsync(item);
        }
        else if (item is Season season)
        {
            season.LastMetadataRefresh = DateTime.UtcNow;
            await _repository.UpdateMediaItemAsync(season);
        }

        var secondaryFetch = await _fetchService.GetSecondaryDataAsync(item, forceOverride);

        var updatedSecondary = await _mappingService.ApplySecondaryDataAsync(item, secondaryFetch.Ratings, secondaryFetch.Artwork, forceOverride);

        if (updatedSecondary)
        {
            await _repository.UpdateMediaItemAsync(item);
        }

        await NotifyItemAndParentsAsync(item);
    }

    private async Task RefreshArtworkAsync(Guid mediaItemId, bool forceOverride = false)
    {
        var item = await _repository.GetForMetadataSyncAsync(mediaItemId);
        if (item?.Library == null) return;

        var artworkEntities = await _fetchService.GetArtworkAsync(item);

        var updated = await _mappingService.ApplyArtworkAsync(item, artworkEntities, forceOverride);
        if (updated)
        {
            await _repository.UpdateMediaItemAsync(item);
        }

        await NotifyItemAndParentsAsync(item);
    }

    private async Task RefreshRatingsAsync(Guid mediaItemId, bool forceOverride = false)
    {
        var item = await _repository.GetForMetadataSyncAsync(mediaItemId);
        if (item == null) return;

        var ratingsData = await _fetchService.GetRatingsAsync(item);

        var updated = await _mappingService.ApplyRatingsAsync(item, ratingsData, forceOverride);
        if (updated)
        {
            await _repository.UpdateMediaItemAsync(item);
        }

        await NotifyItemAndParentsAsync(item);
    }

    private async Task RefreshChildSeasonsAsync(Guid showId, bool forceOverride, Func<Guid, bool, Task> refreshFn)
    {
        var tvShow = await _repository.GetForMetadataSyncAsync(showId) as TvShow;
        if (tvShow == null) return;

        foreach (var season in tvShow.Seasons)
        {
            await refreshFn(season.Id, forceOverride);
        }
    }

    private async Task ProcessLibraryItemsAsync(Guid libraryId, IEnumerable<Guid> ids, string operation, Func<Guid, Task> refreshFn)
    {
        var idList = ids as IReadOnlyList<Guid> ?? ids.ToList();
        var total = idList.Count;
        var titles = await _repository.GetDisplayTitlesByIdsAsync(idList);
        var label = ProgressLabel(operation);

        var count = 0;
        foreach (var id in idList)
        {
            count++;
            var title = titles.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t) ? t : "…";
            _progress.Report($"{label} — {title} ({count}/{total})");

            try
            {
                await refreshFn(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Library {Operation} refresh failed for {MediaItemId}.", operation, id);
            }

            if (count % NotificationBatchSize == 0)
            {
                await _notifier.NotifyLibraryUpdatedAsync(libraryId);
            }
        }

        await _notifier.NotifyLibraryUpdatedAsync(libraryId);
    }

    private static string ProgressLabel(string operation) => operation switch
    {
        "metadata" => "Fetching metadata",
        "artwork" => "Fetching artwork",
        "ratings" => "Fetching ratings",
        _ => $"Processing {operation}"
    };

    private async Task NotifyItemAndParentsAsync(MediaItem item)
    {
        await _notifier.NotifyMediaItemUpdatedAsync(item.Id);

        if (item is Episode episode && episode.Season != null)
        {
            await _notifier.NotifyMediaItemUpdatedAsync(episode.SeasonId);
            await _notifier.NotifyMediaItemUpdatedAsync(episode.Season.TvShowId);
        }
        else if (item is Season season)
        {
            await _notifier.NotifyMediaItemUpdatedAsync(season.TvShowId);
        }
    }
}
