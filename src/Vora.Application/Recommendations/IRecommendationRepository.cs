using Vora.Application.Libraries.ViewModels;

namespace Vora.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<List<LibraryItemVM>> GetHydratedMediaItemsAsync(
        List<Guid> localItemIds,
        List<string> externalTmdbIds,
        Guid? libraryId,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated);

    Task<List<Guid>> GetPlayedMediaIdsAsync(Guid profileId);
    Task<List<(int Id, string Name)>> GetTopGenresAsync(List<Guid> playedMediaIds, int count);
    Task<List<Guid>> GetTopUnwatchedMediaByGenreAsync(int genreId, Guid profileId, Guid? libraryId, int count);
}
