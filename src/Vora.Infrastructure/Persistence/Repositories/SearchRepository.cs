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

        // Match each word independently (case-insensitive via ILIKE) so results
        // aren't tied to word order or exact spacing — e.g. "avatar water" finds
        // "Avatar: The Way of Water".
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new List<MediaSearchResultVM>();

        foreach (var token in tokens)
        {
            var pattern = $"%{token}%";
            dbQuery = dbQuery.Where(m => EF.Functions.ILike(m.Title, pattern));
        }

        return await dbQuery
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
