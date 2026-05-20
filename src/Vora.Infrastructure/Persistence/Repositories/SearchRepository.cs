using Microsoft.EntityFrameworkCore;
using Vora.Application.Search;
using Vora.Application.Search.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence.Extensions;

namespace Vora.Infrastructure.Persistence.Repositories;

public class SearchRepository(VoraDbContext context) : ISearchRepository
{
    public async Task<IEnumerable<MediaSearchResultVM>> SearchMediaAsync(
        string mediaType,
        string query,
        int limit,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated)
    {
        var dbQuery = context.MediaItems.AsNoTracking().AsQueryable();
        dbQuery = dbQuery.ApplyAccessFilters(hasAllAccess, allowedLibs, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated);

        if (!hasAllAccess)
        {
            dbQuery = dbQuery.Where(m => allowedLibs.Contains(m.LibraryId));
        }

        dbQuery = mediaType switch
        {
            "Movie" => dbQuery.OfType<Movie>(),
            "TvShow" => dbQuery.OfType<TvShow>(),
            _ => dbQuery
        };

        var searchLower = query.ToLower();
        return await dbQuery
            .Where(m => m.Title.ToLower().Contains(searchLower))
            .OrderBy(m => m.Title)
            .Take(limit)
            .Select(MediaSearchResultVM.Projection)
            .ToListAsync();
    }

    public async Task<IEnumerable<ActorSearchResultVM>> SearchActorsAsync(string query, int limit)
    {
        var searchLower = query.ToLower();
        return await context.Actors
            .AsNoTracking()
            .Where(a => a.Name.ToLower().Contains(searchLower))
            .OrderBy(a => a.Name)
            .Take(limit)
            .Select(ActorSearchResultVM.Projection)
            .ToListAsync();
    }

    public async Task<IEnumerable<CollectionSearchResultVM>> SearchCollectionsAsync(string query, int limit, bool hasAllAccess, List<Guid> allowedLibs)
    {
        var dbQuery = context.Collections.AsNoTracking().AsQueryable();

        if (!hasAllAccess)
        {
            dbQuery = dbQuery.Where(c => c.LibraryId == null || allowedLibs.Contains(c.LibraryId.Value));
        }

        var searchLower = query.ToLower();
        return await dbQuery
            .Where(c => c.Title.ToLower().Contains(searchLower))
            .OrderBy(c => c.Title)
            .Take(limit)
            .Select(CollectionSearchResultVM.Projection)
            .ToListAsync();
    }
}
