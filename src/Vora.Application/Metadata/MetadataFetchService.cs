using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Plugins;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;
using LocalMetadataMode = Vora.Domain.Enums.LocalMetadataMode;

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
    private const string TmdbArtworkProviderId = "tmdb_artwork";

    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly IEnumerable<IRatingsProvider> _ratingsProviders;
    private readonly IEnumerable<IArtworkProvider> _artworkProviders;
    private readonly ILocalMediaReader _localMedia;
    private readonly IVideoFrameArtworkService _frames;
    private readonly Dictionary<Guid, bool> _trustedShowDocuments = new();

    public MetadataFetchService(
        IEnumerable<IMetadataProvider> providers,
        IEnumerable<IRatingsProvider> ratingsProviders,
        IEnumerable<IArtworkProvider> artworkProviders,
        ILocalMediaReader localMedia,
        IVideoFrameArtworkService frames)
    {
        _providers = providers;
        _ratingsProviders = ratingsProviders;
        _artworkProviders = artworkProviders;
        _localMedia = localMedia;
        _frames = frames;
    }

    public async Task<(MetadataResult? Metadata, string ProviderId, string ProviderName)> GetTextMetadataAsync(MediaItem item)
    {
        var providerIdToUse = StaysLocal(item) ? LocalMetadataProvider.ProviderId : item.Library?.MetadataProviderId ?? TmdbMetadataProviderId;
        var primaryProvider = _providers.FirstOrDefault(p => p.Id == providerIdToUse) ?? _providers.FirstOrDefault();

        if (primaryProvider == null) return (null, string.Empty, string.Empty);

        var readsOnlyLocal = primaryProvider.Id == LocalMetadataProvider.ProviderId;
        var mode = readsOnlyLocal ? LocalMetadataMode.Prefer : item.Library?.LocalMetadataMode ?? LocalMetadataMode.Off;

        var local = await ReadTrustedDocumentAsync(item, mode);
        if (readsOnlyLocal) return (local?.Document, primaryProvider.Id, primaryProvider.ProviderName);

        if (local != null) AdoptIdentity(item, local);

        var namedOnlyByTheDocument = local != null && LocalIdentity.Ids(local.Document).IsEmpty && IdsOf(item).IsEmpty;
        var metadata = namedOnlyByTheDocument
            ? await FetchMetadataForItemAsync(item, primaryProvider, local?.Document.Title, local?.Document.ReleaseDate?.Year)
            : await FetchMetadataForItemAsync(item, primaryProvider);

        if (local == null || mode != LocalMetadataMode.Prefer) return (metadata, primaryProvider.Id, primaryProvider.ProviderName);

        return metadata == null
            ? (local.Document, LocalMetadataProvider.ProviderId, LocalMetadataProvider.LocalProviderName)
            : (LocalMetadataMerge.Overlay(metadata, local.Document), primaryProvider.Id, primaryProvider.ProviderName);
    }

    private async Task<LocalDocument?> ReadTrustedDocumentAsync(MediaItem item, LocalMetadataMode mode)
    {
        if (mode == LocalMetadataMode.Off) return null;

        if (item.CanBeMatchedToAProvider()) return Trusted(item, await _localMedia.ReadDocumentAsync(item.Id));

        if (mode != LocalMetadataMode.Prefer) return null;

        var show = item switch
        {
            Season season => season.TvShow,
            Episode episode => episode.Season?.TvShow,
            _ => null
        };
        if (show == null || !await ShowDocumentIsTrustedAsync(show)) return null;

        return await _localMedia.ReadDocumentAsync(item.Id);
    }

    private async Task<bool> ShowDocumentIsTrustedAsync(TvShow show)
    {
        if (_trustedShowDocuments.TryGetValue(show.Id, out var known)) return known;

        var local = await _localMedia.ReadDocumentAsync(show.Id);
        var trusted = local == null ? !HasFixedIdentity(show) : Trusted(show, local) != null;
        _trustedShowDocuments[show.Id] = trusted;
        return trusted;
    }

    private static LocalDocument? Trusted(MediaItem title, LocalDocument? local)
    {
        if (local == null) return null;

        if (HasFixedIdentity(title))
        {
            var declared = LocalIdentity.Ids(local.Document);
            var own = IdsOf(title);
            return LocalIdentity.SharesAnId(declared, own) && !LocalIdentity.Contradicts(declared, own) ? local : null;
        }

        return LocalIdentity.Trusted(local.Document, local.ParsedYear, local.TaggedIds) == null ? null : local;
    }

    private static bool HasFixedIdentity(MediaItem title) =>
        MediaMatchManager.IdentityChosenByHand(title.LockedFields)
        || title.IsLocked(nameof(title.TmdbId))
        || title.IsLocked(nameof(title.ImdbId))
        || title.IsLocked(nameof(title.TvdbId));

    private static ExternalIdSet IdsOf(MediaItem item) => ExternalIdSet.From(item.TmdbId, item.ImdbId, item.TvdbId);

    private static void AdoptIdentity(MediaItem item, LocalDocument local)
    {
        if (!item.CanBeMatchedToAProvider() || HasFixedIdentity(item)) return;

        var declared = LocalIdentity.Merge(local.TaggedIds, LocalIdentity.Ids(local.Document));
        if (declared.IsEmpty) return;

        if (LocalIdentity.Contradicts(declared, IdsOf(item)))
        {
            item.TmdbId = declared.TmdbId;
            item.ImdbId = declared.ImdbId;
            item.TvdbId = declared.TvdbId;
            return;
        }

        if (string.IsNullOrWhiteSpace(item.TmdbId)) item.TmdbId = declared.TmdbId;
        if (string.IsNullOrWhiteSpace(item.ImdbId)) item.ImdbId = declared.ImdbId;
        if (string.IsNullOrWhiteSpace(item.TvdbId)) item.TvdbId = declared.TvdbId;
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
        var artworkProviderIdToUse = StaysLocal(item) ? LocalArtworkProvider.ProviderId : item.Library?.ArtworkProviderId ?? TmdbArtworkProviderId;
        var needsArtworkRefresh = forceOverride || item.Artwork == null || !item.Artwork.Any() || !item.Artwork.Any(a => a.ProviderId == artworkProviderIdToUse);

        var localArtwork = needsArtworkRefresh ? await FindLocalArtworkAsync(item) : [];

        var ratingsTask = FetchRatingsDataAsync(item, cancellationToken);
        var artworkTask = needsArtworkRefresh
            ? FetchProviderArtworkAsync(item)
            : Task.FromResult(new List<MediaArtwork>());

        await Task.WhenAll(ratingsTask, artworkTask);

        return (await ratingsTask, [.. localArtwork, .. await artworkTask]);
    }

    public async Task<List<MediaArtwork>> GetArtworkAsync(MediaItem item)
    {
        var localArtwork = await FindLocalArtworkAsync(item);
        return [.. localArtwork, .. await FetchProviderArtworkAsync(item)];
    }

    public async Task<(decimal? Rating1, string? Name1, decimal? Rating2, string? Name2)> GetRatingsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        return await FetchRatingsDataAsync(item, cancellationToken);
    }

    // Folder-name id tags (e.g. " [imdb-tt123]", or a malformed empty " [imdb-]")
    // and a trailing "(year)" both pollute a title search and can stop a title
    // from matching at all (the year is already passed as a separate param).
    // Strip them before falling back to a title lookup.
    private async Task<MetadataResult?> FetchMetadataForItemAsync(MediaItem item, IMetadataProvider provider, string? searchTitle = null, int? searchYear = null)
    {
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

            var (movieQuery, movieYear) = MediaMatchIds.SearchTerms(searchTitle ?? movie.Title, searchYear ?? movie.ReleaseDate?.Year);
            return await provider.FetchMovieMetadataAsync(movieQuery, movieYear);
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

            var (showQuery, showYear) = MediaMatchIds.SearchTerms(searchTitle ?? tvShow.Title, searchYear ?? tvShow.ReleaseDate?.Year);
            return await provider.FetchTvShowMetadataAsync(showQuery, showYear);
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
        if (item.Library == null || StaysLocal(item)) return (null, null, null, null);

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

    private static bool UsesLocalArtworkProvider(MediaItem item) =>
        StaysLocal(item) || item.Library?.ArtworkProviderId == LocalArtworkProvider.ProviderId;

    private static bool StaysLocal(MediaItem item) =>
        (item.Library is { } library && !library.Type.LooksUpMetadataOnline())
        || TitleOf(item)?.IsLocked(MediaMatchManager.UnmatchedLock) == true;

    private static MediaItem? TitleOf(MediaItem item) => item switch
    {
        Episode episode => episode.Season?.TvShow,
        Season season => season.TvShow,
        _ => item
    };

    private async Task<List<MediaArtwork>> FindLocalArtworkAsync(MediaItem item)
    {
        if (item?.Library == null) return [];
        if (!item.Library.PreferLocalArtwork && !UsesLocalArtworkProvider(item)) return [];

        var found = await _localMedia.FindArtworkAsync(item.Id);
        if (!StaysLocal(item)) return found;

        var frames = await _frames.GetFrameArtworkAsync(item,
            needsPoster: found.All(a => a.Kind != Vora.Domain.Enums.ArtworkKind.Poster),
            needsBackdrop: found.All(a => a.Kind != Vora.Domain.Enums.ArtworkKind.Backdrop));
        return [.. found, .. frames];
    }

    private async Task<List<MediaArtwork>> FetchProviderArtworkAsync(MediaItem item)
    {
        var artworkEntities = new List<MediaArtwork>();
        if (item?.Library == null || UsesLocalArtworkProvider(item)) return artworkEntities;

        var providerIdToUse = item.Library.ArtworkProviderId ?? TmdbArtworkProviderId;
        var provider = _artworkProviders.FirstOrDefault(p => p.Id == providerIdToUse) ?? _artworkProviders.FirstOrDefault(p => p.Id == TmdbArtworkProviderId);
        if (provider == null) return artworkEntities;

        var localPath = item.MediaParts.FirstOrDefault()?.FilePath;
        if (!string.IsNullOrEmpty(localPath)) localPath = Path.GetDirectoryName(localPath);

        var results = await provider.GetArtworkAsync(item.TmdbId, item.TvdbId, item.ImdbId, item.GetType().Name, localPath, item.Title);

        artworkEntities.AddRange(results.Select(r => new MediaArtwork
        {
            MediaItemId = item.Id,
            Url = r.Url,
            Kind = (Vora.Domain.Enums.ArtworkKind)r.Kind,
            Language = r.Language,
            Width = r.Width,
            Height = r.Height,
            VoteAverage = r.VoteAverage,
            ProviderId = provider.Id
        }));

        return artworkEntities;
    }
}
