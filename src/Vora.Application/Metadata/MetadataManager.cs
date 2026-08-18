using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Actors;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Metadata;

public interface IMetadataManager
{
    Task TriggerLibraryMetadataRefreshAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task TriggerLibraryEnrichmentAsync(Guid libraryId, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task TriggerActorMetadataRefreshAsync(CancellationToken cancellationToken = default);
    Task TriggerMediaTvdbResolutionAsync(CancellationToken cancellationToken = default);
    Task TriggerLibraryRatingsRefreshAsync(Guid libraryId, string? name = null, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task TriggerMediaItemArtworkRefreshAsync(Guid mediaItemId, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task TriggerMediaItemMetadataRefreshAsync(Guid mediaItemId, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task TriggerLibraryArtworkRefreshAsync(Guid libraryId, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task TriggerMediaItemRatingsRefreshAsync(Guid mediaItemId, bool forceOverride = false, CancellationToken cancellationToken = default);
    Task RefreshMetadataAsync(Guid mediaItemId, bool forceOverride = false, bool notify = true);
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
    private readonly ISystemSettingsRepository _settingsRepository;
    private readonly IEnumerable<Vora.Plugins.Interfaces.IMetadataProvider> _metadataProviders;
    private readonly Vora.Plugins.Interfaces.ITaskProgressReporter _progress;
    private readonly ILogger<MetadataManager> _logger;

    private const string TvdbMetadataProviderId = "tvdb_metadata";
    private const string TmdbMetadataProviderId = "tmdb_metadata";

    public MetadataManager(
        IMediaRepository repository,
        IActorRepository actorRepository,
        IServiceScopeFactory scopeFactory,
        IClientNotifier notifier,
        IMetadataFetchService fetchService,
        IMetadataMappingService mappingService,
        ISystemSettingsRepository settingsRepository,
        IEnumerable<Vora.Plugins.Interfaces.IMetadataProvider> metadataProviders,
        Vora.Plugins.Interfaces.ITaskProgressReporter progress,
        ILogger<MetadataManager> logger)
    {
        _repository = repository;
        _actorRepository = actorRepository;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _fetchService = fetchService;
        _mappingService = mappingService;
        _settingsRepository = settingsRepository;
        _metadataProviders = metadataProviders;
        _progress = progress;
        _logger = logger;
    }

    public async Task TriggerActorMetadataRefreshAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _actorRepository.GetActorIdsMissingMetadataAsync(ActorBatchSize);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    public async Task TriggerLibraryMetadataRefreshAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        var ids = forceOverride
            ? await _repository.GetAllProjectedAsync(n => n.Id, libraryId)
            : await _repository.GetMediaIdsMissingMetadataAsync(libraryId);

        await ProcessLibraryItemsAsync(libraryId, ids, "metadata", async id =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
            await scopedManager.RefreshMetadataAsync(id, forceOverride);
        }, cancellationToken);
    }

    public async Task TriggerLibraryRatingsRefreshAsync(Guid libraryId, string? name = null, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        // Non-force runs fetch items still missing any configured rating slot, so
        // re-runs fill in items skipped when the provider's daily quota tripped
        // — instead of re-spending the quota on already-rated items every pass.
        var ids = forceOverride
            ? await _repository.GetAllProjectedAsync(n => n.Id, libraryId)
            : await _repository.GetMediaIdsMissingRatingsAsync(libraryId);

        await ProcessLibraryItemsAsync(libraryId, ids, "ratings", id => RefreshRatingsAsync(id, forceOverride), cancellationToken);
    }

    public async Task TriggerLibraryArtworkRefreshAsync(Guid libraryId, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        // Non-force runs only fetch items missing a poster, so adding a folder (or
        // the nightly scan) doesn't re-pull artwork for the whole library — only
        // genuinely new items. Force still refreshes everything.
        var ids = forceOverride
            ? await _repository.GetAllProjectedAsync(n => n.Id, libraryId)
            : await _repository.GetMediaIdsMissingArtworkAsync(libraryId);
        await ProcessLibraryItemsAsync(libraryId, ids, "artwork", id => RefreshArtworkAsync(id, forceOverride), cancellationToken);
    }

    public async Task TriggerLibraryEnrichmentAsync(Guid libraryId, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        // Enrich each item fully (metadata → artwork → ratings) before moving to
        // the next, so posters fill in progressively during a first scan instead
        // of after three separate whole-library phases. The set of work is
        // identical to running those phases — same guard sets, same leaf refresh
        // calls — only the grouping (by item vs by operation) differs.
        HashSet<Guid> metaSet, artSet, ratSet;
        List<Guid> ordered;

        if (forceOverride)
        {
            ordered = (await _repository.GetAllProjectedAsync(n => n.Id, libraryId)).ToList();
            metaSet = ordered.ToHashSet();
            artSet = metaSet;
            ratSet = metaSet;
        }
        else
        {
            var metaIds = (await _repository.GetMediaIdsMissingMetadataAsync(libraryId)).ToList();
            metaSet = metaIds.ToHashSet();
            artSet = (await _repository.GetMediaIdsMissingArtworkAsync(libraryId)).ToHashSet();
            ratSet = (await _repository.GetMediaIdsMissingRatingsAsync(libraryId)).ToHashSet();

            ordered = new List<Guid>(metaIds);
            var seen = new HashSet<Guid>(metaIds);
            foreach (var id in artSet) if (seen.Add(id)) ordered.Add(id);
            foreach (var id in ratSet) if (seen.Add(id)) ordered.Add(id);
        }

        await ProcessLibraryItemsAsync(libraryId, ordered, "enrich", async id =>
        {
            if (forceOverride || metaSet.Contains(id))
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
                await scopedManager.RefreshMetadataAsync(id, forceOverride);
            }
            if (forceOverride || artSet.Contains(id)) await RefreshArtworkAsync(id, forceOverride);
            if (forceOverride || ratSet.Contains(id)) await RefreshRatingsAsync(id, forceOverride);
        }, cancellationToken);
    }

    public async Task TriggerMediaItemMetadataRefreshAsync(Guid mediaItemId, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        var itemInfo = await _repository.GetProjectedAsync(mediaItemId, m => new { m.Id, Type = m.GetType().Name });
        if (itemInfo == null) return;

        if (itemInfo.Type == nameof(Season))
        {
            var season = await _repository.GetForBasicUpdateAsync(mediaItemId) as Season;
            if (season != null) await TriggerMediaItemMetadataRefreshAsync(season.TvShowId, forceOverride, cancellationToken);
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
                    cancellationToken.ThrowIfCancellationRequested();
                    await RefreshArtworkAsync(season.Id, forceOverride);
                    await RefreshRatingsAsync(season.Id, forceOverride);
                }
            }
            var episodeIds = await _repository.GetEpisodeIdsForShowAsync(mediaItemId);
            // Enrich episodes on THIS scope so they share the provider's per-show
            // episode-list cache: the bulk episode list is fetched once and every
            // episode reads from it. (Enriching each episode in its own scope threw
            // that cache away and re-fetched the whole list per episode — a ~Nx
            // request storm that saturated the provider and stalled the scan.) A
            // failing episode is isolated so it can't abort the rest of the show.
            foreach (var epId in episodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Suppress the per-episode SignalR fan-out (episode + season +
                    // show, ×N episodes re-notifies the same show/seasons over and
                    // over). One notification for the show after the loop is enough
                    // during a bulk scan; the episode counts refresh with it.
                    await RefreshMetadataAsync(epId, forceOverride, notify: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Episode metadata refresh failed for {EpisodeId}.", epId);
                }
            }

            if (episodeIds.Count > 0) await _notifier.NotifyMediaItemUpdatedAsync(mediaItemId);
        }
    }

    public async Task TriggerMediaItemArtworkRefreshAsync(Guid mediaItemId, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        var itemInfo = await _repository.GetProjectedAsync(mediaItemId, m => new { m.Id, Type = m.GetType().Name });
        if (itemInfo == null) return;

        await RefreshArtworkAsync(mediaItemId, forceOverride);

        if (itemInfo.Type == nameof(TvShow))
        {
            await RefreshChildSeasonsAsync(mediaItemId, forceOverride, RefreshArtworkAsync, cancellationToken);
        }
    }

    public async Task TriggerMediaItemRatingsRefreshAsync(Guid mediaItemId, bool forceOverride = false, CancellationToken cancellationToken = default)
    {
        var itemInfo = await _repository.GetProjectedAsync(mediaItemId, m => new { m.Id, Type = m.GetType().Name });
        if (itemInfo == null) return;

        await RefreshRatingsAsync(mediaItemId, forceOverride);

        if (itemInfo.Type == nameof(TvShow))
        {
            await RefreshChildSeasonsAsync(mediaItemId, forceOverride, RefreshRatingsAsync, cancellationToken);
        }
    }

    public async Task RefreshMetadataAsync(Guid mediaItemId, bool forceOverride = false, bool notify = true)
    {
        var item = await _repository.GetForMetadataSyncAsync(mediaItemId);
        if (item == null) return;

        var seasonNeedsPoster = item is Season && string.IsNullOrEmpty(item.PosterUrl);
        var showHasSeasonMissingArtwork = item is TvShow show
            && show.Seasons.Any(s => s.MissingSince == null && string.IsNullOrEmpty(s.PosterUrl));

        if (!forceOverride && item.LastMetadataRefresh.HasValue && !seasonNeedsPoster && !showHasSeasonMissingArtwork) return;

        var textFetch = await _fetchService.GetTextMetadataAsync(item);

        if (textFetch.Metadata != null)
        {
            await _mappingService.ApplyTextMetadataAsync(item, textFetch.Metadata, forceOverride, textFetch.ProviderId, textFetch.ProviderName);
            await TryResolveMovieTvdbIdAsync(item, forceOverride);
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

        if (notify) await NotifyItemAndParentsAsync(item);
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

        // Stamp "checked" only when every configured provider was actually consulted
        // this pass. FetchRatingsDataAsync returns a null slot name when a provider
        // was skipped (its rate-limit breaker was open), so a null name means "not
        // consulted" — don't stamp then, so the item is retried once quota frees up.
        // A non-null name with a null rating means the provider genuinely has no
        // score, and the timestamp lets the sweep stop re-asking every night.
        var wants1 = !string.IsNullOrEmpty(item.Library?.ThirdPartyRating1ProviderId);
        var wants2 = !string.IsNullOrEmpty(item.Library?.ThirdPartyRating2ProviderId);
        var consulted1 = !wants1 || ratingsData.Name1 != null;
        var consulted2 = !wants2 || ratingsData.Name2 != null;
        if (consulted1 && consulted2)
        {
            item.RatingsCheckedAt = DateTime.UtcNow;
            updated = true;
        }

        if (updated)
        {
            await _repository.UpdateMediaItemAsync(item);
        }

        await NotifyItemAndParentsAsync(item);
    }

    private async Task RefreshChildSeasonsAsync(Guid showId, bool forceOverride, Func<Guid, bool, Task> refreshFn, CancellationToken cancellationToken = default)
    {
        var tvShow = await _repository.GetForMetadataSyncAsync(showId) as TvShow;
        if (tvShow == null) return;

        foreach (var season in tvShow.Seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await refreshFn(season.Id, forceOverride);
        }
    }

    // Movies come from TMDB and never carry a TVDB id, so the TVDB artwork
    // provider (which needs one) can't contribute. When the admin opts in, look
    // the movie up on TVDB — by IMDb id first, then title/year — and store the
    // resolved TVDB id so TVDB posters/backdrops become available.
    private async Task TryResolveMovieTvdbIdAsync(MediaItem item, bool forceOverride)
    {
        if (item is not Movie movie) return;
        if (!string.IsNullOrWhiteSpace(movie.TvdbId)) return;
        if (movie.IsLocked(nameof(movie.TvdbId)) && !forceOverride) return;

        var settings = await _settingsRepository.GetSettingsAsync();
        if (!settings.ResolveMovieTvdbIds) return;

        await ResolveTvdbIdForMovieAsync(movie);
    }

    private async Task ResolveTvdbIdForMovieAsync(Movie movie)
    {
        var provider = _metadataProviders.FirstOrDefault(p => p.Id == TvdbMetadataProviderId);
        if (provider == null) return;

        try
        {
            Vora.Plugins.Dtos.MetadataResult? result = null;
            if (!string.IsNullOrWhiteSpace(movie.ImdbId))
            {
                result = await provider.FetchMovieMetadataByIdAsync(movie.ImdbId, "imdb");
            }
            if (string.IsNullOrWhiteSpace(result?.TvdbId) && !string.IsNullOrWhiteSpace(movie.Title))
            {
                result = await provider.FetchMovieMetadataAsync(movie.Title, movie.ReleaseDate?.Year);
            }

            if (!string.IsNullOrWhiteSpace(result?.TvdbId))
            {
                movie.TvdbId = result.TvdbId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TVDB id resolution failed for movie {MediaItemId}.", movie.Id);
        }
    }

    // Shows carry their TVDB id in TMDB's external_ids (now mapped), so for an
    // existing show we just re-read its TMDB metadata rather than TVDB-searching.
    private async Task ResolveTvdbIdForShowAsync(TvShow show)
    {
        if (string.IsNullOrWhiteSpace(show.TmdbId)) return;
        var provider = _metadataProviders.FirstOrDefault(p => p.Id == TmdbMetadataProviderId);
        if (provider == null) return;

        try
        {
            var result = await provider.FetchTvShowMetadataByIdAsync(show.TmdbId, "tmdb");
            if (!string.IsNullOrWhiteSpace(result?.TvdbId))
            {
                show.TvdbId = result.TvdbId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TVDB id resolution failed for show {MediaItemId}.", show.Id);
        }
    }

    // Triggered explicitly by the admin "Resolve now" button, so it runs
    // regardless of the persisted toggle (which only gates the automatic,
    // during-refresh path) — otherwise clicking before saving silently no-ops.
    public async Task TriggerMediaTvdbResolutionAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _repository.GetMediaIdsMissingTvdbIdAsync();
        var total = ids.Count;
        var count = 0;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            var item = await _repository.GetForBasicUpdateAsync(id);
            if (item == null || !string.IsNullOrWhiteSpace(item.TvdbId) || item.IsLocked(nameof(item.TvdbId))) continue;

            _progress.Report($"Resolving TVDB ids — {item.Title} ({count}/{total})");

            if (item is Movie movie) await ResolveTvdbIdForMovieAsync(movie);
            else if (item is TvShow show) await ResolveTvdbIdForShowAsync(show);

            if (!string.IsNullOrWhiteSpace(item.TvdbId))
            {
                await _repository.UpdateMediaItemAsync(item);
            }
        }

        _progress.Report(null);
    }

    private async Task ProcessLibraryItemsAsync(Guid libraryId, IEnumerable<Guid> ids, string operation, Func<Guid, Task> refreshFn, CancellationToken cancellationToken = default)
    {
        var idList = ids as IReadOnlyList<Guid> ?? ids.ToList();
        var total = idList.Count;
        var titles = await _repository.GetDisplayTitlesByIdsAsync(idList);
        var label = ProgressLabel(operation);

        var count = 0;
        foreach (var id in idList)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        "enrich" => "Fetching details",
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
