using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Vora.Application.Ai;
using Vora.Application.Collections;
using Vora.Domain.Entities.Ai;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class CollectionThemeRepository(VoraDbContext context) : ICollectionThemeRepository
{
    public Task<CollectionTheme?> GetAsync(Guid collectionId) =>
        context.CollectionThemes.AsNoTracking().FirstOrDefaultAsync(t => t.CollectionId == collectionId);

    public async Task SaveAsync(CollectionTheme theme, bool forgetVerdicts)
    {
        var stored = await context.CollectionThemes.FirstOrDefaultAsync(t => t.CollectionId == theme.CollectionId);
        if (stored == null)
        {
            context.CollectionThemes.Add(theme);
        }
        else
        {
            stored.Theme = theme.Theme;
            stored.Profile = theme.Profile;
            stored.Keywords = theme.Keywords;
            stored.Embedding = theme.Embedding;
            stored.EmbeddingModel = theme.EmbeddingModel;
            stored.UpdatedAt = DateTime.UtcNow;
        }

        if (forgetVerdicts)
        {
            context.CollectionThemeVerdicts.RemoveRange(
                await context.CollectionThemeVerdicts.Where(v => v.CollectionId == theme.CollectionId).ToListAsync());
        }

        await context.SaveChangesAsync();
    }

    public Task<Dictionary<Guid, bool>> GetVerdictsAsync(Guid collectionId) =>
        context.CollectionThemeVerdicts.AsNoTracking()
            .Where(v => v.CollectionId == collectionId)
            .ToDictionaryAsync(v => v.MediaItemId, v => v.Belongs);

    public async Task SaveVerdictsAsync(Guid collectionId, IReadOnlyDictionary<Guid, bool> verdicts)
    {
        var ids = verdicts.Keys.ToList();
        var stored = await context.CollectionThemeVerdicts
            .Where(v => v.CollectionId == collectionId && ids.Contains(v.MediaItemId))
            .ToDictionaryAsync(v => v.MediaItemId);

        foreach (var (mediaItemId, belongs) in verdicts)
        {
            if (stored.TryGetValue(mediaItemId, out var verdict))
            {
                verdict.Belongs = belongs;
                verdict.JudgedAt = DateTime.UtcNow;
            }
            else
            {
                context.CollectionThemeVerdicts.Add(new CollectionThemeVerdict { CollectionId = collectionId, MediaItemId = mediaItemId, Belongs = belongs });
            }
        }

        await context.SaveChangesAsync();
    }

    public Task<List<Guid>> FindByKeywordsAsync(IReadOnlyCollection<string> keywords, Guid? libraryId)
    {
        var wanted = keywords.ToList();
        var query = context.MediaItems.AsNoTracking()
            .Where(MediaCapabilities.IsBrowsableTitle)
            .Where(m => m.MissingSince == null && m.Keywords != null && wanted.Any(k => m.Keywords.Contains(k)));

        if (libraryId.HasValue) query = query.Where(m => m.LibraryId == libraryId.Value);

        return query.Select(m => m.Id).ToListAsync();
    }

    public Task<List<Guid>> FindNearestAsync(float[] vector, string embeddingModel, Guid? libraryId, int limit) =>
        VectorSearch.WideAsync(context, limit, () => NearestTitles(context, vector, embeddingModel, libraryId, limit).ToListAsync());

    internal static IQueryable<Guid> NearestTitles(VoraDbContext context, float[] vector, string embeddingModel, Guid? libraryId, int limit)
    {
        var target = new Pgvector.Vector(vector);
        var unstampedCounts = AiConnection.IsLegacyStamp(embeddingModel);

        var query = context.Set<MediaItemEmbedding>().AsNoTracking()
            .Where(e => e.Embedding != null)
            .Where(e => e.Model == embeddingModel || (unstampedCounts && e.Model == null))
            .Where(MediaCapabilities.IsBrowsableTitle.On((MediaItemEmbedding e) => e.MediaItem))
            .Where(e => e.MediaItem.MissingSince == null);

        if (libraryId.HasValue) query = query.Where(e => e.MediaItem.LibraryId == libraryId.Value);

        return query
            .OrderBy(e => EF.Property<Pgvector.Vector>(e, nameof(MediaItemEmbedding.Embedding)).CosineDistance(target))
            .Take(limit)
            .Select(e => e.MediaItemId);
    }

    public Task<List<ThemeCandidate>> GetCandidatesAsync(IReadOnlyCollection<Guid> mediaItemIds)
    {
        var ids = mediaItemIds.ToList();
        return context.MediaItems.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new ThemeCandidate(
                m.Id,
                m.Title,
                m.ReleaseDate.HasValue ? m.ReleaseDate.Value.Year : null,
                m is TvShow,
                m.Overview,
                m.Genres.Select(g => g.Name).ToList()))
            .ToListAsync();
    }
}
