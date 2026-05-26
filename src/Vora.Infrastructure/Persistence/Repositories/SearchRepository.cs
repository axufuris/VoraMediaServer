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

        var searchPattern = $"%{query}%";
        return await dbQuery
            .Where(m => EF.Functions.ILike(m.Title, searchPattern))
            .OrderBy(m => m.Title)
            .Take(limit)
            .Select(MediaSearchResultVM.Projection)
            .ToListAsync();
    }

    public async Task<IEnumerable<ActorSearchResultVM>> SearchActorsAsync(string query, int limit)
    {
        var searchPattern = $"%{query}%";
        return await context.Actors
            .AsNoTracking()
            .Where(a => EF.Functions.ILike(a.Name, searchPattern))
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

        var searchPattern = $"%{query}%";
        return await dbQuery
            .Where(c => EF.Functions.ILike(c.Title, searchPattern))
            .OrderBy(c => c.Title)
            .Take(limit)
            .Select(CollectionSearchResultVM.Projection)
            .ToListAsync();
    }
}
