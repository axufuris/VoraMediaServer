using Vora.Application.Media;
using Vora.Application.Search.ViewModels;

namespace Vora.Application.Search;

public interface ISearchManager
{
    Task<GlobalSearchVM> SearchAllAsync(
        string query,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        List<string> allowedMusicRatings,
        bool blockUnrated,
        int limitPerCategory = 10,
        CancellationToken cancellationToken = default);
}

public class SearchManager(ISearchRepository repository, IMusicManager musicManager) : ISearchManager
{
    private const int MinimumQueryLength = 3;

    public async Task<GlobalSearchVM> SearchAllAsync(
        string query,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        List<string> allowedMusicRatings,
        bool blockUnrated,
        int limitPerCategory = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < MinimumQueryLength)
        {
            return new GlobalSearchVM { Query = query };
        }

        var movies = await repository.SearchMediaAsync("Movie", query, limitPerCategory, hasAllAccess, allowedLibs, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated);
        cancellationToken.ThrowIfCancellationRequested();
        var tvShows = await repository.SearchMediaAsync("TvShow", query, limitPerCategory, hasAllAccess, allowedLibs, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated);
        cancellationToken.ThrowIfCancellationRequested();
        var actors = await repository.SearchActorsAsync(query, limitPerCategory);
        cancellationToken.ThrowIfCancellationRequested();
        var collections = await repository.SearchCollectionsAsync(query, limitPerCategory, hasAllAccess, allowedLibs);
        cancellationToken.ThrowIfCancellationRequested();

        var musicAccess = new MusicAccessFilter
        {
            HasAllLibraryAccess = hasAllAccess,
            AllowedLibraryIds = allowedLibs,
            HasAllRatings = hasAllRatings,
            AllowedRatings = allowedMusicRatings,
            BlockUnratedContent = blockUnrated
        };
        var music = await musicManager.SearchAsync(query, musicAccess, limitPerCategory);

        return new GlobalSearchVM
        {
            Query = query,
            Movies = movies,
            TvShows = tvShows,
            Actors = actors,
            Collections = collections,
            Music = music
        };
    }
}
