using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Vora.Application.Ai;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Recommendations;
using Vora.Domain.Entities.Ai;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence.Extensions;

namespace Vora.Infrastructure.Persistence.Repositories;

public class RecommendationRepository(VoraDbContext context) : IRecommendationRepository
{
    // Looser than the AI rows' 0.80: a title is compared with other titles here,
    // not with a few words describing a mood.
    private const double SimilarDistanceThreshold = 0.70;

    public async Task<List<LibraryItemVM>> GetHydratedMediaItemsAsync(
        Guid profileId,
        List<Guid> localItemIds,
        List<string> externalTmdbIds,
        Guid? libraryId,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated)
    {
        var query = context.MediaItems
            .AsNoTracking()
            .ApplyAccessFilters(hasAllAccess, allowedLibs, allowedMovieRatings, allowedTvRatings, blockUnrated);

        if (libraryId.HasValue)
        {
            query = query.Where(m => m.LibraryId == libraryId.Value);
        }

        return await query
            .Where(m => localItemIds.Contains(m.Id) || (m.TmdbId != null && externalTmdbIds.Contains(m.TmdbId)))
            .Select(LibraryItemVM.ProjectionFor(profileId, context.UserMediaStates, context.UserMediaRatings))
            .ToListAsync();
    }

    public Task<List<WatchedTitle>> GetWatchedTitlesAsync(Guid profileId, Guid? libraryId) =>
        RecommendationQueries.WatchedTitlesAsync(context, profileId, libraryId);

    public async Task<List<TitleGenre>> GetGenresAsync(IReadOnlyCollection<Guid> titleIds)
    {
        if (titleIds.Count == 0) return new List<TitleGenre>();

        var rows = await context.MediaItems
            .AsNoTracking()
            .Where(m => titleIds.Contains(m.Id))
            .SelectMany(m => m.Genres.Select(g => new { TitleId = m.Id, GenreId = g.Id, g.Name }))
            .ToListAsync();
        return rows.Select(r => new TitleGenre(r.TitleId, r.GenreId, r.Name)).ToList();
    }

    public Task<List<Guid>> GetTopUnwatchedByGenreAsync(int genreId, Guid profileId, Guid? libraryId, int count) =>
        BestRated(Unwatched(profileId, libraryId).Where(m => m.Genres.Any(g => g.Id == genreId)), count);

    public Task<List<Guid>> GetTopUnwatchedAsync(Guid profileId, Guid? libraryId, int count) =>
        BestRated(Unwatched(profileId, libraryId), count);

    public async Task<List<Guid>> GetSimilarUnwatchedAsync(Guid titleId, Guid profileId, Guid? libraryId, int count)
    {
        var seed = await context.Set<MediaItemEmbedding>()
            .AsNoTracking()
            .Where(e => e.MediaItemId == titleId && e.Embedding != null)
            .Select(e => new { e.Embedding, e.Model })
            .FirstOrDefaultAsync();
        if (seed?.Embedding == null) return new List<Guid>();

        // Compared only with vectors the same model made. One with no model
        // recorded was made by OpenAI's, before models were recorded, which is
        // most titles on a server that has been running a while.
        var vector = seed.Embedding;
        var model = seed.Model;
        var legacyModel = AiConnection.OpenAiEmbeddingModel;
        var legacy = model == null || AiConnection.IsLegacyStamp(model);
        var query = context.Set<MediaItemEmbedding>()
            .AsNoTracking()
            .Where(e => e.Embedding != null && e.MediaItemId != titleId)
            .Where(e => legacy ? e.Model == null || e.Model == legacyModel : e.Model == model)
            .Where(MediaCapabilities.IsBrowsableTitle.On((MediaItemEmbedding e) => e.MediaItem))
            .Where(RecommendationQueries.Untouched(context, profileId).On((MediaItemEmbedding e) => e.MediaItem))
            .Where(e => e.MediaItem.MissingSince == null);

        if (libraryId.HasValue)
        {
            query = query.Where(e => e.MediaItem.LibraryId == libraryId.Value);
        }

        return VectorSearch.WithinDistance(await VectorSearch.WideAsync(context, count * 10, () => query
            .OrderBy(e => e.Embedding!.CosineDistance(vector))
            .Take(count)
            .Select(e => new VectorSearch.Hit(e.MediaItemId, e.Embedding!.CosineDistance(vector)))
            .ToListAsync()), SimilarDistanceThreshold);
    }

    private IQueryable<MediaItem> Unwatched(Guid profileId, Guid? libraryId)
    {
        var query = context.MediaItems
            .AsNoTracking()
            .Where(MediaCapabilities.IsBrowsableTitle)
            .Where(m => m.MissingSince == null)
            .Where(RecommendationQueries.Untouched(context, profileId));

        return libraryId.HasValue ? query.Where(m => m.LibraryId == libraryId.Value) : query;
    }

    // Unrated titles last. Postgres puts NULL first in a descending sort, so the
    // rows used to open with whatever had no score at all.
    private static Task<List<Guid>> BestRated(IQueryable<MediaItem> query, int count) =>
        query
            .OrderBy(m => m.ThirdPartyRating1 == null)
            .ThenByDescending(m => m.ThirdPartyRating1)
            .ThenBy(m => m.Title)
            .Take(count)
            .Select(m => m.Id)
            .ToListAsync();
}
