using Microsoft.Extensions.Logging;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Metadata.Local;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Metadata;

public interface IMediaMatchManager
{
    Task<IReadOnlyList<MediaMatchCandidateVM>> SearchAsync(Guid mediaItemId, string? query, int? year, CancellationToken cancellationToken = default);
    Task<MediaMatchResultVM> ApplyAsync(Guid mediaItemId, ApplyMediaMatchRequest request, CancellationToken cancellationToken = default);
    Task<MediaMatchFilesVM> GetFilesAsync(Guid mediaItemId);
    Task ClearAsync(Guid mediaItemId);
    Task RemoveMetadataAsync(Guid mediaItemId);
}

public class MediaMatchManager(
    IMediaRepository repository,
    IEnumerable<IMetadataProvider> providers,
    IMediaDedupeManager dedupeManager,
    ITaskQueueManager taskQueue,
    ILocalMediaReader localMedia,
    IMediaArtworkRepository artwork,
    ILogger<MediaMatchManager> logger) : IMediaMatchManager
{
    public const string MatchLock = "Match";
    public const string UnmatchedLock = "Unmatched";

    public static bool IsIdentityLock(string field) =>
        field.Equals(MatchLock, StringComparison.OrdinalIgnoreCase) || field.Equals(UnmatchedLock, StringComparison.OrdinalIgnoreCase);

    public static bool IdentityChosenByHand(IEnumerable<string> lockedFields) => lockedFields.Any(IsIdentityLock);
    private const string TmdbProviderId = "tmdb_metadata";
    private const string TvdbProviderId = "tvdb_metadata";

    public async Task<IReadOnlyList<MediaMatchCandidateVM>> SearchAsync(Guid mediaItemId, string? query, int? year, CancellationToken cancellationToken = default)
    {
        var item = await LoadMatchableAsync(mediaItemId);
        var isTvShow = item is TvShow;

        var idQuery = MediaMatchIds.FromQuery(query);
        if (idQuery != null)
        {
            return await LookUpByIdAsync(item, idQuery, isTvShow, cancellationToken);
        }

        var searchTerm = string.IsNullOrWhiteSpace(query) ? MediaMatchIds.CleanSearchTitle(item.Title) : query.Trim();
        if (searchTerm.Length == 0) return [];

        var provider = ResolveSearchProvider(item);
        if (provider == null) return [];

        var candidates = await SearchProviderAsync(provider, searchTerm, year, isTvShow, cancellationToken);
        if (candidates.Count == 0 && year.HasValue)
        {
            candidates = await SearchProviderAsync(provider, searchTerm, null, isTvShow, cancellationToken);
        }

        return candidates.Select(c => ToViewModel(c, provider.ProviderName)).ToList();
    }

    public async Task<MediaMatchFilesVM> GetFilesAsync(Guid mediaItemId)
    {
        var item = await LoadMatchableAsync(mediaItemId);
        if (item is not TvShow)
        {
            return new MediaMatchFilesVM
            {
                FileNames = item.MediaParts
                    .Select(p => MediaMatchFiles.FileName(p.FilePath))
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList()
            };
        }

        var episodePaths = await repository.GetEpisodeFilePathsForShowAsync(mediaItemId);
        return new MediaMatchFilesVM
        {
            FolderNames = episodePaths
                .Select(path => MediaMatchFiles.ShowFolderName(path, item.Library.FolderPaths))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList()
        };
    }

    public async Task<MediaMatchResultVM> ApplyAsync(Guid mediaItemId, ApplyMediaMatchRequest request, CancellationToken cancellationToken = default)
    {
        var source = MediaMatchIds.NormalizeSource(request.Source)
            ?? throw new ArgumentException($"Unknown match source '{request.Source}'.");
        var externalId = MediaMatchIds.NormalizeId(source, request.ExternalId)
            ?? throw new ArgumentException($"'{request.ExternalId}' is not a valid {MediaMatchIds.Label(source)} id.");

        var item = await LoadMatchableAsync(mediaItemId);
        var isTvShow = item is TvShow;

        var existing = await repository.FindOtherItemByExternalIdAsync(item.LibraryId, item.Id, isTvShow, source, externalId);

        item.TmdbId = null;
        item.ImdbId = null;
        item.TvdbId = null;
        MediaMatchIds.Assign(item, source, externalId);
        item.UnlockField(UnmatchedLock);
        item.LockField(MatchLock);

        if (existing != null)
        {
            item.TmdbId ??= existing.TmdbId;
            item.ImdbId ??= existing.ImdbId;
            item.TvdbId ??= existing.TvdbId;
        }

        await repository.UpdateMediaItemAsync(item);

        var targetId = item.Id;
        var mergedDuplicate = false;

        if (isTvShow && existing != null)
        {
            var merge = await dedupeManager.MergeDuplicateTvShowsAsync(item.LibraryId, cancellationToken);
            mergedDuplicate = merge.ShowsRemoved > 0;
            if (!await repository.MediaItemExistsAsync(item.Id))
            {
                targetId = existing.Id;
            }
        }

        taskQueue.QueueRefreshMatchedMediaItem(targetId, item.LibraryId, isTvShow);

        logger.LogInformation(
            "Matched {MediaType} {MediaItemId} to {Source} {ExternalId}; refreshing {TargetId} (merged duplicate: {Merged}).",
            isTvShow ? "show" : "movie", item.Id, MediaMatchIds.Label(source), externalId, targetId, mergedDuplicate);

        return new MediaMatchResultVM { MediaItemId = targetId, MergedDuplicate = mergedDuplicate };
    }

    public async Task ClearAsync(Guid mediaItemId)
    {
        var item = await LoadMatchableAsync(mediaItemId);
        if (!IdentityChosenByHand(item.LockedFields)) return;

        var naming = await localMedia.ReadNamingAsync(item.Id);

        item.UnlockField(MatchLock);
        item.UnlockField(UnmatchedLock);
        if (!item.IsLocked(nameof(item.TmdbId))) item.TmdbId = naming?.TaggedIds.TmdbId;
        if (!item.IsLocked(nameof(item.ImdbId))) item.ImdbId = naming?.TaggedIds.ImdbId;
        if (!item.IsLocked(nameof(item.TvdbId))) item.TvdbId = naming?.TaggedIds.TvdbId;

        if (naming != null)
        {
            if (!item.IsLocked(nameof(item.Title))) item.Title = naming.Title ?? item.Title;
            if (!item.IsLocked(nameof(item.ReleaseDate))) item.ReleaseDate = naming.Year is int year ? new DateOnly(year, 1, 1) : null;
        }

        item.LastMetadataRefresh = null;
        await repository.UpdateMediaItemAsync(item);

        if (item is TvShow)
        {
            await repository.ClearMetadataRefreshAsync(await repository.GetEpisodeIdsForShowAsync(item.Id));
        }

        taskQueue.QueueRefreshMediaItemMetadata(item.Id, item.Title, false, item.LibraryId);

        logger.LogInformation("Cleared the manual match on {MediaType} {MediaItemId}; matching it again from its files.",
            item is TvShow ? "show" : "movie", item.Id);
    }

    public async Task RemoveMetadataAsync(Guid mediaItemId)
    {
        var title = await LoadMatchableAsync(mediaItemId);
        var children = title is TvShow ? await repository.GetShowChildrenForMetadataResetAsync(title.Id) : [];
        var ids = children.Select(child => child.Id).Prepend(title.Id).ToList();
        var uploaded = await artwork.GetUploadedArtworkUrlsAsync(ids);
        var naming = await localMedia.ReadNamingAsync(title.Id);

        MetadataReset.ClearTitle(title, naming?.Title, naming?.Year, uploaded);
        title.UnlockField(MatchLock);
        title.LockField(UnmatchedLock);

        var episodeNames = new EpisodeFileNames(title.Library?.ScannerRegex);
        foreach (var season in children.OfType<Season>())
        {
            MetadataReset.ClearSeason(season, uploaded);
        }
        foreach (var episode in children.OfType<Episode>())
        {
            var placement = PlacementOf(episode, episodeNames);
            var fileTitle = placement?.Title ?? MetadataReset.EpisodeTitleFallback(title.Title, episode.Season.SeasonNumber, episode.EpisodeNumber);
            MetadataReset.ClearEpisode(episode, fileTitle, placement?.AirDate, uploaded);
        }

        var reset = children.Prepend(title).ToList();
        await repository.RemoveMediaCastMembersAsync(reset.SelectMany(item => item.Cast).ToList());
        await repository.RemoveMediaVideosAsync(reset.SelectMany(item => item.Videos).ToList());
        await repository.SaveChangesAsync();
        await artwork.ClearProviderArtworkAsync(ids);
        taskQueue.QueueRefreshMediaItemMetadata(title.Id, title.Title, false, title.LibraryId);

        logger.LogInformation("Removed the metadata of {MediaType} {MediaItemId} and {Children} seasons and episodes; it stays unmatched until it is matched again.",
            title is TvShow ? "show" : "movie", title.Id, children.Count);
    }

    private static EpisodePlacement? PlacementOf(Episode episode, EpisodeFileNames names)
    {
        var path = episode.MediaParts.Select(part => part.FilePath).FirstOrDefault(file => !string.IsNullOrWhiteSpace(file));
        return path == null
            ? null
            : names.Place(Path.GetFileNameWithoutExtension(path), Path.GetFileName(Path.GetDirectoryName(path)), () => null);
    }

    private async Task<MediaItem> LoadMatchableAsync(Guid mediaItemId)
    {
        var item = await repository.GetForMetadataSyncAsync(mediaItemId)
            ?? throw new KeyNotFoundException("Media item not found.");

        if (item is not Movie and not TvShow)
        {
            throw new InvalidOperationException("Only movies and TV shows can be matched.");
        }

        return item;
    }

    private IMetadataProvider? ResolveSearchProvider(MediaItem item)
    {
        var libraryProviderId = item.Library?.MetadataProviderId;
        var searchesById = libraryProviderId is TmdbProviderId or TvdbProviderId;
        return FindProvider(searchesById ? libraryProviderId : TmdbProviderId) ?? FindProvider(TmdbProviderId);
    }

    private async Task<IReadOnlyList<MediaMatchCandidateVM>> LookUpByIdAsync(MediaItem item, MediaMatchIdQuery idQuery, bool isTvShow, CancellationToken cancellationToken)
    {
        if (idQuery.IsTvShow.HasValue && idQuery.IsTvShow.Value != isTvShow) return [];

        var provider = idQuery.Source == MediaMatchIds.Tmdb ? FindProvider(TmdbProviderId) : ResolveSearchProvider(item);
        if (provider == null) return [];

        var result = isTvShow
            ? await provider.FetchTvShowMetadataByIdAsync(idQuery.ExternalId, idQuery.Source, cancellationToken)
            : await provider.FetchMovieMetadataByIdAsync(idQuery.ExternalId, idQuery.Source, cancellationToken);

        if (result == null || string.IsNullOrWhiteSpace(result.Title)) return [];

        return
        [
            new MediaMatchCandidateVM
            {
                Source = idQuery.Source,
                ExternalId = idQuery.ExternalId,
                Title = result.Title,
                Year = result.ReleaseDate?.Year,
                Overview = result.Overview,
                PosterUrl = result.PosterUrl,
                ProviderName = provider.ProviderName
            }
        ];
    }

    private static Task<IReadOnlyList<MetadataSearchCandidate>> SearchProviderAsync(IMetadataProvider provider, string query, int? year, bool isTvShow, CancellationToken cancellationToken) =>
        isTvShow
            ? provider.SearchTvShowCandidatesAsync(query, year, cancellationToken)
            : provider.SearchMovieCandidatesAsync(query, year, cancellationToken);

    private IMetadataProvider? FindProvider(string? providerId) =>
        providerId == null ? null : providers.FirstOrDefault(p => p.Id == providerId);

    private static MediaMatchCandidateVM ToViewModel(MetadataSearchCandidate candidate, string providerName) => new()
    {
        Source = candidate.Source,
        ExternalId = candidate.ExternalId,
        Title = candidate.Title,
        Year = candidate.Year,
        Overview = candidate.Overview,
        PosterUrl = candidate.PosterUrl,
        ProviderName = providerName
    };
}
