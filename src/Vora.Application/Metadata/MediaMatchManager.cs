using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Metadata;

public interface IMediaMatchManager
{
    Task<IReadOnlyList<MediaMatchCandidateVM>> SearchAsync(Guid mediaItemId, string? query, int? year, CancellationToken cancellationToken = default);
    Task<MediaMatchResultVM> ApplyAsync(Guid mediaItemId, ApplyMediaMatchRequest request, CancellationToken cancellationToken = default);
}

public class MediaMatchManager(
    IMediaRepository repository,
    IEnumerable<IMetadataProvider> providers,
    IMediaDedupeManager dedupeManager,
    ITaskQueueManager taskQueue,
    ILogger<MediaMatchManager> logger) : IMediaMatchManager
{
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
