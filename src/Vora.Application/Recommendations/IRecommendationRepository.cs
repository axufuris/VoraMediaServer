using Vora.Application.Libraries.ViewModels;

namespace Vora.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<List<LibraryItemVM>> GetHydratedMediaItemsAsync(
        Guid profileId,
        List<Guid> localItemIds,
        List<string> externalTmdbIds,
        Guid? libraryId,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated);

    // The movies and shows the profile has watched or rated, in the library when
    // one is given. A show counts once, as soon as any of its episodes is played.
    Task<List<WatchedTitle>> GetWatchedTitlesAsync(Guid profileId, Guid? libraryId);
    Task<List<TitleGenre>> GetGenresAsync(IReadOnlyCollection<Guid> titleIds);

    // Titles the profile hasn't started or rated, best rated first.
    Task<List<Guid>> GetTopUnwatchedByGenreAsync(int genreId, Guid profileId, Guid? libraryId, int count);
    Task<List<Guid>> GetTopUnwatchedAsync(Guid profileId, Guid? libraryId, int count);

    // The unstarted titles nearest to this one in the stored embeddings, nearest
    // first. Empty when the title has no embedding yet.
    Task<List<Guid>> GetSimilarUnwatchedAsync(Guid titleId, Guid profileId, Guid? libraryId, int count);
}

public sealed record WatchedTitle(Guid Id, string Title, int? Year, DateTime? LastWatchedAt, decimal? Rating, DateTime? RatedAt);

public sealed record TitleGenre(Guid TitleId, int GenreId, string GenreName);
