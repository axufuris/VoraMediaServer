using Microsoft.EntityFrameworkCore;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Recommendations;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence.Extensions;

namespace Vora.Infrastructure.Persistence.Repositories;

public class RecommendationRepository(VoraDbContext context) : IRecommendationRepository
{
    public async Task<List<LibraryItemVM>> GetHydratedMediaItemsAsync(
        List<Guid> localItemIds,
        List<string> externalTmdbIds,
        Guid? libraryId,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated)
    {
        var query = context.MediaItems
            .AsNoTracking()
            .ApplyAccessFilters(hasAllAccess, allowedLibs, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated);

        if (libraryId.HasValue)
        {
            query = query.Where(m => m.LibraryId == libraryId.Value);
        }

        return await query
            .Where(m => localItemIds.Contains(m.Id) || (m.TmdbId != null && externalTmdbIds.Contains(m.TmdbId)))
            .Select(LibraryItemVM.Projection)
            .ToListAsync();
    }

    public Task<List<Guid>> GetPlayedMediaIdsAsync(Guid profileId) =>
        context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsPlayed)
            .Select(s => s.MediaItemId)
            .ToListAsync();

    public async Task<List<(int Id, string Name)>> GetTopGenresAsync(List<Guid> playedMediaIds, int count)
    {
        var topGenres = await context.MediaItems
            .AsNoTracking()
            .Where(m => playedMediaIds.Contains(m.Id))
            .SelectMany(m => m.Genres)
            .GroupBy(g => new { g.Id, g.Name })
            .OrderByDescending(g => g.Count())
            .Take(count)
            .Select(g => new { g.Key.Id, g.Key.Name })
            .ToListAsync();

        return topGenres.Select(g => (g.Id, g.Name)).ToList();
    }

    public Task<List<Guid>> GetTopUnwatchedMediaByGenreAsync(int genreId, Guid profileId, Guid? libraryId, int count)
    {
        var query = context.MediaItems
            .AsNoTracking()
            .Where(m => m.MissingSince == null && m.Genres.Any(g => g.Id == genreId) && (m is Movie || m is TvShow));

        if (libraryId.HasValue)
        {
            query = query.Where(m => m.LibraryId == libraryId.Value);
        }

        query = query.Where(m =>
            !context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == m.Id && s.IsPlayed));

        return query
            .OrderByDescending(m => m.ThirdPartyRating1)
            .Take(count)
            .Select(m => m.Id)
            .ToListAsync();
    }
}
