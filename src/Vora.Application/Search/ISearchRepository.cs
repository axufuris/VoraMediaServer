using Vora.Application.Search.ViewModels;

namespace Vora.Application.Search;

public interface ISearchRepository
{
    Task<IEnumerable<MediaSearchResultVM>> SearchMediaAsync(
        string mediaType,
        string query,
        int limit,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated);

    Task<IEnumerable<ActorSearchResultVM>> SearchActorsAsync(string query, int limit);
    Task<IEnumerable<CollectionSearchResultVM>> SearchCollectionsAsync(string query, int limit, bool hasAllAccess, List<Guid> allowedLibs);
}
