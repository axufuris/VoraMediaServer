using Vora.Domain.Entities.Media;
using Vora.Plugins;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Metadata;

public interface IMetadataFetchService
{
    Task<(MetadataResult? Metadata, string ProviderId, string ProviderName)> GetTextMetadataAsync(MediaItem item);
    Task<ActorMetadataResult?> GetActorMetadataAsync(int tmdbId);
    Task<((decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) Ratings, List<MediaArtwork> Artwork)> GetSecondaryDataAsync(MediaItem item, bool forceOverride);
    Task<List<MediaArtwork>> GetArtworkAsync(MediaItem item);
    Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> GetRatingsAsync(MediaItem item);
}

public class MetadataFetchService : IMetadataFetchService
{
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly IEnumerable<IRatingsProvider> _ratingsProviders;
    private readonly IEnumerable<IArtworkProvider> _artworkProviders;

    public MetadataFetchService(
        IEnumerable<IMetadataProvider> providers,
        IEnumerable<IRatingsProvider> ratingsProviders,
        IEnumerable<IArtworkProvider> artworkProviders)
    {
        _providers = providers;
        _ratingsProviders = ratingsProviders;
        _artworkProviders = artworkProviders;
    }

    public async Task<(MetadataResult? Metadata, string ProviderId, string ProviderName)> GetTextMetadataAsync(MediaItem item)
    {
        var providerIdToUse = item.Library?.MetadataProviderId ?? "tmdb_metadata";
        var primaryProvider = _providers.FirstOrDefault(p => p.Id == providerIdToUse) ?? _providers.FirstOrDefault();

        if (primaryProvider == null) return (null, string.Empty, string.Empty);

        var metadata = await FetchMetadataForItemAsync(item, primaryProvider);
        return (metadata, primaryProvider.Id, primaryProvider.ProviderName);
    }

    public async Task<ActorMetadataResult?> GetActorMetadataAsync(int tmdbId)
    {
        var primaryProvider = _providers.FirstOrDefault();
        if (primaryProvider == null) return null;

        return await primaryProvider.FetchActorMetadataAsync(tmdbId);
    }

    public async Task<((decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) Ratings, List<MediaArtwork> Artwork)> GetSecondaryDataAsync(MediaItem item, bool forceOverride)
    {
        var ratingsTask = FetchRatingsDataAsync(item);

        var artworkProviderIdToUse = item.Library?.ArtworkProviderId ?? "tmdb_artwork";
        var needsArtworkRefresh = forceOverride || item.Artwork == null || !item.Artwork.Any() || !item.Artwork.Any(a => a.ProviderId == artworkProviderIdToUse);

        var artworkTask = needsArtworkRefresh
            ? FetchArtworkDataAsync(item)
            : Task.FromResult(new List<MediaArtwork>());

        await Task.WhenAll(ratingsTask, artworkTask);

        return (ratingsTask.Result, artworkTask.Result);
    }

    public async Task<List<MediaArtwork>> GetArtworkAsync(MediaItem item)
    {
        return await FetchArtworkDataAsync(item);
    }

    public async Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> GetRatingsAsync(MediaItem item)
    {
        return await FetchRatingsDataAsync(item);
    }

    private async Task<MetadataResult?> FetchMetadataForItemAsync(MediaItem item, IMetadataProvider provider)
    {
        if (provider.Id == "local_metadata")
        {
            var physicalPath = item.MediaParts.FirstOrDefault()?.FilePath;
            if (string.IsNullOrEmpty(physicalPath)) return null;

            var folderPath = Path.GetDirectoryName(physicalPath) ?? physicalPath;

            if (item is Movie) return await provider.FetchMovieMetadataByIdAsync(folderPath, "local");
            if (item is TvShow) return await provider.FetchTvShowMetadataByIdAsync(folderPath, "local");

            if (item is Episode ep) return await provider.FetchEpisodeMetadataAsync(folderPath, ep.Season?.SeasonNumber ?? 1, ep.EpisodeNumber);

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

            return await provider.FetchMovieMetadataAsync(movie.Title, movie.ReleaseDate?.Year);
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

            return await provider.FetchTvShowMetadataAsync(tvShow.Title, tvShow.ReleaseDate?.Year);
        }

        if (item is Season) return null;

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

    private async Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> FetchRatingsDataAsync(MediaItem item)
    {
        if (item.Library == null) return (null, null, null, null);

        var provider1 = !string.IsNullOrEmpty(item.Library.ThirdPartyRating1ProviderId)
            ? _ratingsProviders.FirstOrDefault(p => p.Id == item.Library.ThirdPartyRating1ProviderId) : null;

        var provider2 = !string.IsNullOrEmpty(item.Library.ThirdPartyRating2ProviderId)
            ? _ratingsProviders.FirstOrDefault(p => p.Id == item.Library.ThirdPartyRating2ProviderId) : null;

        Task<decimal?> task1 = provider1 != null
            ? provider1.FetchRatingAsync(item.ImdbId, item.TmdbId, item.TvdbId, item.GetType().Name)
            : Task.FromResult<decimal?>(null);

        Task<decimal?> task2 = provider2 != null
            ? provider2.FetchRatingAsync(item.ImdbId, item.TmdbId, item.TvdbId, item.GetType().Name)
            : Task.FromResult<decimal?>(null);

        await Task.WhenAll(task1, task2);

        return (task1.Result, provider1?.RatingSourceName, task2.Result, provider2?.RatingSourceName);
    }

    private async Task<List<MediaArtwork>> FetchArtworkDataAsync(MediaItem item)
    {
        var artworkEntities = new List<MediaArtwork>();
        if (item?.Library == null) return artworkEntities;

        var providerIdToUse = item.Library.ArtworkProviderId ?? "tmdb_artwork";
        var remoteProvider = _artworkProviders.FirstOrDefault(p => p.Id == providerIdToUse) ?? _artworkProviders.FirstOrDefault(p => p.Id == "tmdb_artwork");
        var localProvider = item.Library.UseLocalAssets ? _artworkProviders.FirstOrDefault(p => p.Id == "local_artwork") : null;

        var localPath = item.MediaParts.FirstOrDefault()?.FilePath;
        if (!string.IsNullOrEmpty(localPath)) localPath = Path.GetDirectoryName(localPath);

        var localTask = localProvider != null
            ? localProvider.GetArtworkAsync(item.TmdbId, item.TvdbId, item.ImdbId, item.GetType().Name, localPath, item.Title)
            : Task.FromResult(Enumerable.Empty<ArtworkResult>());

        var remoteTask = remoteProvider != null && remoteProvider.Id != "local_artwork"
            ? remoteProvider.GetArtworkAsync(item.TmdbId, item.TvdbId, item.ImdbId, item.GetType().Name, localPath, item.Title)
            : Task.FromResult(Enumerable.Empty<ArtworkResult>());

        await Task.WhenAll(localTask, remoteTask);

        if (localProvider != null)
        {
            artworkEntities.AddRange(localTask.Result.Select(r => new MediaArtwork
            {
                MediaItemId = item.Id,
                Url = r.Url,
                Kind = (Vora.Domain.Enums.ArtworkKind)r.Kind,
                Language = r.Language,
                Width = r.Width,
                Height = r.Height,
                VoteAverage = r.VoteAverage,
                ProviderId = localProvider.Id
            }));
        }

        if (remoteProvider != null && remoteProvider.Id != "local_artwork")
        {
            artworkEntities.AddRange(remoteTask.Result.Select(r => new MediaArtwork
            {
                MediaItemId = item.Id,
                Url = r.Url,
                Kind = (Vora.Domain.Enums.ArtworkKind)r.Kind,
                Language = r.Language,
                Width = r.Width,
                Height = r.Height,
                VoteAverage = r.VoteAverage,
                ProviderId = remoteProvider.Id
            }));
        }

        return artworkEntities;
    }
}