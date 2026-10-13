using Vora.Domain.Entities.Media;
using Vora.Plugins;
using Vora.Application.Media;
using Vora.Plugins.Providers.Local;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Metadata;

public interface IMetadataFetchService
{
    Task<(MetadataResult? Metadata, string ProviderId, string ProviderName)> GetTextMetadataAsync(MediaItem item);
    Task<ActorMetadataResult?> GetActorMetadataAsync(int tmdbId, int tvdbId);
    Task<((decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) Ratings, List<MediaArtwork> Artwork)> GetSecondaryDataAsync(MediaItem item, bool forceOverride, CancellationToken cancellationToken = default);
    Task<List<MediaArtwork>> GetArtworkAsync(MediaItem item);
    Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> GetRatingsAsync(MediaItem item, CancellationToken cancellationToken = default);
}

public class MetadataFetchService : IMetadataFetchService
{
    private const string TmdbMetadataProviderId = "tmdb_metadata";
    private const string TvdbMetadataProviderId = "tvdb_metadata";

    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly IEnumerable<IRatingsProvider> _ratingsProviders;
    private readonly IEnumerable<IArtworkProvider> _artworkProviders;
    private readonly IMediaRepository? _repository;

    public MetadataFetchService(
        IEnumerable<IMetadataProvider> providers,
        IEnumerable<IRatingsProvider> ratingsProviders,
        IEnumerable<IArtworkProvider> artworkProviders,
        IMediaRepository? repository = null)
    {
        _providers = providers;
        _ratingsProviders = ratingsProviders;
        _artworkProviders = artworkProviders;
        _repository = repository;
    }

    public async Task<(MetadataResult? Metadata, string ProviderId, string ProviderName)> GetTextMetadataAsync(MediaItem item)
    {
        var localProvider = _providers.FirstOrDefault(p => p.Id == "local_metadata");
        if (localProvider != null)
        {
            var localMetadata = await FetchMetadataForItemAsync(item, localProvider);
            if (localMetadata != null) return (localMetadata, localProvider.Id, localProvider.ProviderName);
        }

        var providerIdToUse = item.Library?.MetadataProviderId ?? "tmdb_metadata";
        var primaryProvider = _providers.FirstOrDefault(p => p.Id == providerIdToUse) ?? _providers.FirstOrDefault();

        if (primaryProvider == null) return (null, string.Empty, string.Empty);
        if (primaryProvider == localProvider) return (null, primaryProvider.Id, primaryProvider.ProviderName);

        var metadata = await FetchMetadataForItemAsync(item, primaryProvider);
        return (metadata, primaryProvider.Id, primaryProvider.ProviderName);
    }

    // Route by whichever id space the person came from. It used to take
    // _providers.FirstOrDefault(); registration order comes from assembly
    // reflection, so "first" was arbitrary — it could land on
    // LocalMetadataProvider, whose actor fetch returns null unconditionally and
    // silently enriched nobody, or hand a TMDB id to TVDB, which would resolve
    // it in TVDB's own people id space and answer about a different person.
    public async Task<ActorMetadataResult?> GetActorMetadataAsync(int tmdbId, int tvdbId)
    {
        var providerId = tmdbId > 0 ? TmdbMetadataProviderId
            : tvdbId > 0 ? TvdbMetadataProviderId
            : null;

        if (providerId == null) return null;

        var provider = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider == null) return null;

        return await provider.FetchActorMetadataAsync(tmdbId > 0 ? tmdbId : tvdbId);
    }

    public async Task<((decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) Ratings, List<MediaArtwork> Artwork)> GetSecondaryDataAsync(MediaItem item, bool forceOverride, CancellationToken cancellationToken = default)
    {
        var ratingsTask = FetchRatingsDataAsync(item, cancellationToken);

        var artworkProviderIdToUse = item.Library?.ArtworkProviderId ?? "tmdb_artwork";
        var needsArtworkRefresh = forceOverride || item.Artwork == null || !item.Artwork.Any() || !item.Artwork.Any(a => a.ProviderId == artworkProviderIdToUse);

        var artworkTask = needsArtworkRefresh
            ? FetchArtworkDataAsync(item)
            : Task.FromResult(new List<MediaArtwork>());

        await Task.WhenAll(ratingsTask, artworkTask);

        return (await ratingsTask, await artworkTask);
    }

    public async Task<List<MediaArtwork>> GetArtworkAsync(MediaItem item)
    {
        return await FetchArtworkDataAsync(item);
    }

    public async Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> GetRatingsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        return await FetchRatingsDataAsync(item, cancellationToken);
    }

    // Folder-name id tags (e.g. " [imdb-tt123]", or a malformed empty " [imdb-]")
    // and a trailing "(year)" both pollute a title search and can stop a title
    // from matching at all (the year is already passed as a separate param).
    // Strip them before falling back to a title lookup.
    private async Task<MetadataResult?> FetchMetadataForItemAsync(MediaItem item, IMetadataProvider provider)
    {
        if (provider.Id == "local_metadata")
        {
            var physicalPath = await GetLocalPathAsync(item);
            if (string.IsNullOrEmpty(physicalPath)) return null;
            if (item is Movie) return await provider.FetchMovieMetadataByIdAsync(physicalPath, "local");
            if (item is TvShow) return await provider.FetchTvShowMetadataByIdAsync(physicalPath, "local");
            if (item is Season seasonItem) return await provider.FetchSeasonMetadataAsync(physicalPath, "local", seasonItem.SeasonNumber);
            if (item is Episode ep) return await provider.FetchEpisodeMetadataAsync(physicalPath, ep.Season?.SeasonNumber ?? 1, ep.EpisodeNumber);

            return null;
        }

        if (item is Movie movie)
        {
            if (provider.Id == "tvdb_metadata" && !string.IsNullOrEmpty(movie.TvdbId))
            {
                var res = await provider.FetchMovieMetadataByIdAsync(movie.TvdbId, "tvdb");
                if (res != null) return res;
            }

            if (provider.Id == "tvdb_metadata" && !string.IsNullOrEmpty(movie.ImdbId))
            {
                var res = await provider.FetchMovieMetadataByIdAsync(movie.ImdbId, "imdb");
                if (res != null) return res;
            }

            if (!string.IsNullOrEmpty(movie.TmdbId))
            {
                var res = await provider.FetchMovieMetadataByIdAsync(movie.TmdbId, "tmdb");
                if (res != null) return res;
            }

            if (!string.IsNullOrEmpty(movie.ImdbId))
            {
                var res = await provider.FetchMovieMetadataByIdAsync(movie.ImdbId, "imdb");
                if (res != null) return res;
            }

            return await provider.FetchMovieMetadataAsync(MediaMatchIds.CleanSearchTitle(movie.Title), movie.ReleaseDate?.Year);
        }

        if (item is TvShow tvShow)
        {
            if (provider.Id == "tvdb_metadata" && !string.IsNullOrEmpty(tvShow.TvdbId))
            {
                var res = await provider.FetchTvShowMetadataByIdAsync(tvShow.TvdbId, "tvdb");
                if (res != null) return res;
            }

            if (provider.Id == "tvdb_metadata" && !string.IsNullOrEmpty(tvShow.ImdbId))
            {
                var res = await provider.FetchTvShowMetadataByIdAsync(tvShow.ImdbId, "imdb");
                if (res != null) return res;
            }

            if (!string.IsNullOrEmpty(tvShow.TmdbId))
            {
                var res = await provider.FetchTvShowMetadataByIdAsync(tvShow.TmdbId, "tmdb");
                if (res != null) return res;
            }

            if (!string.IsNullOrEmpty(tvShow.ImdbId))
            {
                var res = await provider.FetchTvShowMetadataByIdAsync(tvShow.ImdbId, "imdb");
                if (res != null) return res;
            }

            return await provider.FetchTvShowMetadataAsync(MediaMatchIds.CleanSearchTitle(tvShow.Title), tvShow.ReleaseDate?.Year);
        }

        if (item is Season season)
        {
            var show = season.TvShow;
            if (show == null) return null;

            if (provider.Id == "tvdb_metadata" && !string.IsNullOrEmpty(show.TvdbId))
                return await provider.FetchSeasonMetadataAsync(show.TvdbId, "tvdb", season.SeasonNumber);

            if (!string.IsNullOrEmpty(show.TmdbId))
                return await provider.FetchSeasonMetadataAsync(show.TmdbId, "tmdb", season.SeasonNumber);

            return null;
        }

        if (item is Episode episode)
        {
            if (provider.Id == "tvdb_metadata" && !string.IsNullOrEmpty(episode.Season?.TvShow?.TvdbId))
            {
                return await provider.FetchEpisodeMetadataAsync(episode.Season.TvShow.TvdbId, episode.Season.SeasonNumber, episode.EpisodeNumber);
            }

            if (!string.IsNullOrEmpty(episode.Season?.TvShow?.TmdbId))
            {
                return await provider.FetchEpisodeMetadataAsync(episode.Season.TvShow.TmdbId, episode.Season.SeasonNumber, episode.EpisodeNumber);
            }
        }

        return null;
    }

    private async Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> FetchRatingsDataAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        if (item.Library == null) return (null, null, null, null);

        var provider1 = !string.IsNullOrEmpty(item.Library.ThirdPartyRating1ProviderId)
            ? _ratingsProviders.FirstOrDefault(p => p.Id == item.Library.ThirdPartyRating1ProviderId) : null;

        var provider2 = !string.IsNullOrEmpty(item.Library.ThirdPartyRating2ProviderId)
            ? _ratingsProviders.FirstOrDefault(p => p.Id == item.Library.ThirdPartyRating2ProviderId) : null;

        // A provider that's configured but temporarily unavailable (e.g. its
        // rate-limit circuit breaker is open) is treated as "not consulted": we
        // skip the fetch AND return a null slot name. ApplyRatingsAsync only
        // clears a slot when its name is non-null, so a null name leaves any
        // existing rating in place instead of wiping it during the outage.
        var available1 = provider1 != null && provider1.IsCurrentlyAvailable;
        var available2 = provider2 != null && provider2.IsCurrentlyAvailable;

        Task<decimal?> task1 = available1
            ? provider1!.FetchRatingAsync(item.ImdbId, item.TmdbId, item.TvdbId, item.GetType().Name, cancellationToken)
            : Task.FromResult<decimal?>(null);

        Task<decimal?> task2 = available2
            ? provider2!.FetchRatingAsync(item.ImdbId, item.TmdbId, item.TvdbId, item.GetType().Name, cancellationToken)
            : Task.FromResult<decimal?>(null);

        await Task.WhenAll(task1, task2);

        return (await task1, available1 ? provider1!.RatingSourceName : null, await task2, available2 ? provider2!.RatingSourceName : null);
    }

    private async Task<List<MediaArtwork>> FetchArtworkDataAsync(MediaItem item)
    {
        var artworkEntities = new List<MediaArtwork>();
        if (item?.Library == null) return artworkEntities;

        var providerIdToUse = item.Library.ArtworkProviderId ?? "tmdb_artwork";
        var provider = _artworkProviders.FirstOrDefault(p => p.Id == providerIdToUse) ?? _artworkProviders.FirstOrDefault(p => p.Id == "tmdb_artwork");
        var localProvider = _artworkProviders.FirstOrDefault(p => p.Id == "local_artwork");
        var localPath = await GetLocalPathAsync(item);
        if (localProvider != null && localPath != null)
        {
            var localArtwork = await localProvider.GetArtworkAsync(item.TmdbId, item.TvdbId, item.ImdbId, item.GetType().Name, localPath, item.Title);
            if (item is Season localSeason)
            {
                var poster = LocalMediaAssets.SeasonPoster(localPath, localSeason.SeasonNumber);
                localArtwork = localArtwork.Where(artwork => artwork.Kind != Vora.Plugins.Dtos.ArtworkKind.Poster);
                if (poster != null) localArtwork = localArtwork.Prepend(new ArtworkResult
                {
                    Kind = Vora.Plugins.Dtos.ArtworkKind.Poster, Url = poster, Language = "Local"
                });
            }
            Add(localArtwork, localProvider.Id);
        }
        var localKinds = artworkEntities.Select(a => a.Kind).ToHashSet();
        if (provider != null && provider != localProvider && localKinds.Count < (item is Episode or Season ? 2 : 3))
        {
            var results = await provider.GetArtworkAsync(item.TmdbId, item.TvdbId, item.ImdbId, item.GetType().Name,
                localPath == null ? null : LocalMediaAssets.DirectoryPath(localPath), item.Title);
            Add(results.Where(r => !localKinds.Contains((Vora.Domain.Enums.ArtworkKind)r.Kind)), provider.Id);
        }

        return artworkEntities;

        void Add(IEnumerable<ArtworkResult> results, string providerId) => artworkEntities.AddRange(results.Select(r => new MediaArtwork
        {
            MediaItemId = item.Id, Url = r.Url, Kind = (Vora.Domain.Enums.ArtworkKind)r.Kind,
            Language = r.Language, Width = r.Width, Height = r.Height, VoteAverage = r.VoteAverage, ProviderId = providerId
        }));
    }

    private async Task<string?> GetLocalPathAsync(MediaItem item)
    {
        var path = item.MediaParts.FirstOrDefault()?.FilePath;
        if (item is TvShow show)
        {
            if (path == null && _repository != null)
                path = (await _repository.GetEpisodeFilePathsForShowAsync(show.Id)).FirstOrDefault();
            return path == null ? null : LocalMediaAssets.ShowDirectory(path);
        }
        if (item is Season season)
        {
            if (path == null && _repository != null)
            {
                var episodeIds = await _repository.GetEpisodeIdsForSeasonAsync(season.Id);
                if (episodeIds.Count > 0) path = (await _repository.GetMediaFilePathsAsync(episodeIds[0])).FirstOrDefault();
            }
            return path == null ? null : LocalMediaAssets.DirectoryPath(path);
        }
        return path;
    }
}
