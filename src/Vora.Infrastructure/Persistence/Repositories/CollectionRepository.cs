using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Collections;
using Vora.Application.Collections.Dtos;
using Vora.Application.Collections.ViewModels;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class CollectionRepository(VoraDbContext context) : ICollectionRepository
{
    public Task<T?> GetProjectedByIdAsync<T>(Guid id, Expression<Func<Collection, T>> projection, bool hasAllAccess = true, List<Guid>? allowedLibs = null)
    {
        var query = context.Collections.AsNoTracking().Where(c => c.Id == id);
        if (!hasAllAccess && allowedLibs != null)
        {
            query = query.Where(c => c.LibraryId == null || allowedLibs.Contains(c.LibraryId.Value));
        }
        return query.Select(projection).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<T>> GetAllProjectedAsync<T>(
        Expression<Func<Collection, T>> projection,
        Guid? libraryId = null,
        bool globalOnly = false,
        bool hasAllAccess = true,
        List<Guid>? allowedLibs = null)
    {
        var query = context.Collections.AsNoTracking();

        if (!hasAllAccess && allowedLibs != null)
        {
            query = query.Where(c => c.LibraryId == null || allowedLibs.Contains(c.LibraryId.Value));
        }

        if (libraryId.HasValue)
        {
            query = query.Where(c => c.LibraryId == libraryId.Value || c.LibraryId == null);
        }

        if (globalOnly)
        {
            query = query.Where(c => c.LibraryId == null);
        }

        return await query.Select(projection).ToListAsync();
    }

    public Task<List<CollectionScheduleDto>> GetContentSyncCollectionsAsync() =>
        context.Collections
            .AsNoTracking()
            .Where(c => !string.IsNullOrEmpty(c.ContentSyncProviderId) && !string.IsNullOrEmpty(c.ContentSyncExternalId))
            .Select(CollectionScheduleDto.Projection)
            .ToListAsync();

    public Task<List<CollectionScheduleDto>> GetAutoSyncCollectionsAsync() =>
        context.Collections
            .AsNoTracking()
            .Where(c => c.AutoSyncChronology && !string.IsNullOrEmpty(c.SortProviderId))
            .Select(CollectionScheduleDto.Projection)
            .ToListAsync();

    public async Task<IEnumerable<CollectionArtwork>> GetCollectionArtworkAsync(Guid collectionId) =>
        await context.Set<CollectionArtwork>()
            .AsNoTracking()
            .Where(a => a.CollectionId == collectionId)
            .OrderByDescending(a => a.VoteAverage)
            .ToListAsync();

    public Task<CollectionArtwork?> GetCollectionArtworkByIdAsync(Guid id) =>
        context.Set<CollectionArtwork>().FindAsync(id).AsTask();

    public async Task<HashSet<Guid>> GetCollectionMediaIdsAsync(Guid collectionId)
    {
        var ids = await context.Set<CollectionItem>()
            .AsNoTracking()
            .Where(ci => ci.CollectionId == collectionId)
            .Select(ci => ci.MediaItemId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    public async Task<List<CollectionItem>> GetCollectionItemsWithMediaAsync(Guid collectionId) =>
        await context.Set<CollectionItem>()
            .Include(ci => ci.MediaItem)
            .Where(ci => ci.CollectionId == collectionId)
            .ToListAsync();

    public Task<Dictionary<Guid, decimal>> GetCollectionItemSortOrdersAsync(Guid collectionId) =>
        context.Set<CollectionItem>()
            .AsNoTracking()
            .Where(ci => ci.CollectionId == collectionId)
            .ToDictionaryAsync(ci => ci.MediaItemId, ci => ci.SortOrder);

    public async Task<int> GetLibraryMinimumCollectionSizeAsync(Guid libraryId)
    {
        var library = await context.MediaLibraries.FindAsync(libraryId);
        return library?.MinimumCollectionSize ?? 1;
    }

    public Task<Dictionary<Guid, int>> GetAllLibraryMinimumSizesAsync() =>
        context.MediaLibraries.ToDictionaryAsync(l => l.Id, l => l.MinimumCollectionSize);

    public Task<Collection?> GetForUpdateAsync(Guid id) =>
        context.Collections.FirstOrDefaultAsync(c => c.Id == id);

    public Task<Collection?> GetCollectionByTmdbIdAsync(int tmdbId, Guid libraryId) =>
        context.Collections.FirstOrDefaultAsync(c => c.TmdbId == tmdbId && c.LibraryId == libraryId);

    public async Task<Guid> CreateCollectionAsync(Collection collection)
    {
        await context.Collections.AddAsync(collection);
        await context.SaveChangesAsync();
        return collection.Id;
    }

    public Task AddCollectionAsync(Collection collection) =>
        context.Collections.AddAsync(collection).AsTask();

    public async Task UpdateCollectionAsync(Collection collection)
    {
        if (context.Entry(collection).State == EntityState.Detached)
        {
            context.Collections.Update(collection);
        }
        await context.SaveChangesAsync();
    }

    public async Task UpdateCollectionItemsAsync(IEnumerable<CollectionItem> items)
    {
        context.Set<CollectionItem>().UpdateRange(items);
        await context.SaveChangesAsync();
    }

    public async Task UpdateCollectionItemOrdersAsync(Guid collectionId, List<Guid> orderedMediaItemIds)
    {
        var items = await context.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId)
            .ToListAsync();

        var indexLookup = new Dictionary<Guid, int>(orderedMediaItemIds.Count);
        for (var i = 0; i < orderedMediaItemIds.Count; i++)
        {
            indexLookup[orderedMediaItemIds[i]] = i + 1;
        }

        foreach (var item in items)
        {
            if (indexLookup.TryGetValue(item.MediaItemId, out var order))
            {
                item.SortOrder = order;
            }
        }

        await context.SaveChangesAsync();
    }

    public async Task AddItemsToCollectionAsync(List<CollectionItem> items)
    {
        await context.CollectionItems.AddRangeAsync(items);
        await context.SaveChangesAsync();
    }

    public async Task AddItemToCollectionAsync(Guid collectionId, Guid mediaItemId)
    {
        var exists = await context.Set<CollectionItem>()
            .AnyAsync(ci => ci.CollectionId == collectionId && ci.MediaItemId == mediaItemId);

        if (exists)
        {
            return;
        }

        var maxSortOrder = await context.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId)
            .MaxAsync(ci => (int?)ci.SortOrder);

        await context.Set<CollectionItem>().AddAsync(new CollectionItem
        {
            CollectionId = collectionId,
            MediaItemId = mediaItemId,
            SortOrder = (maxSortOrder ?? 0) + 1
        });
        await context.SaveChangesAsync();
    }

    public async Task RemoveItemFromCollectionAsync(Guid collectionId, Guid mediaItemId)
    {
        var item = await context.Set<CollectionItem>()
            .FirstOrDefaultAsync(ci => ci.CollectionId == collectionId && ci.MediaItemId == mediaItemId);

        if (item == null)
        {
            return;
        }

        context.Set<CollectionItem>().Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task DeleteCollectionAsync(Guid id)
    {
        var collection = await context.Collections.FindAsync(id);
        if (collection == null)
        {
            return;
        }
        context.Collections.Remove(collection);
        await context.SaveChangesAsync();
    }

    public async Task AddCollectionArtworkAsync(CollectionArtwork artwork)
    {
        await context.Set<CollectionArtwork>().AddAsync(artwork);
        await context.SaveChangesAsync();
    }

    public async Task DeleteCollectionArtworkAsync(Guid id)
    {
        var artwork = await context.Set<CollectionArtwork>().FindAsync(id);
        if (artwork == null)
        {
            return;
        }
        context.Set<CollectionArtwork>().Remove(artwork);
        await context.SaveChangesAsync();
    }

    public async Task ReplaceProviderArtworkAsync(Guid collectionId, IEnumerable<CollectionArtwork> newArtwork)
    {
        var existing = await context.Set<CollectionArtwork>()
            .Where(a => a.CollectionId == collectionId && !a.IsUserUploaded)
            .ToListAsync();

        context.Set<CollectionArtwork>().RemoveRange(existing);
        await context.Set<CollectionArtwork>().AddRangeAsync(newArtwork);
        await context.SaveChangesAsync();
    }

    public async Task AttachCollectionItemUserStatesAsync(IEnumerable<CollectionDetailsLibraryItemVM> items, Guid profileId)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return;
        }

        var itemIds = itemList.Select(i => i.Id).ToList();

        var directStates = await context.Set<UserMediaState>()
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && itemIds.Contains(s.MediaItemId))
            .ToDictionaryAsync(s => s.MediaItemId, s => s.IsPlayed);

        var tvShowIds = itemList.Where(i => i.Type == "TvShow").Select(i => i.Id).ToList();
        var seasonIds = itemList.Where(i => i.Type == "Season").Select(i => i.Id).ToList();

        var (tvStats, seasonStats) = await ResolveAggregatedPlayStatsAsync(profileId, tvShowIds, seasonIds);

        foreach (var item in itemList)
        {
            ApplyUserState(item, directStates, tvStats, seasonStats);
        }
    }

    private async Task<(Dictionary<Guid, (int Total, int Played)> TvStats, Dictionary<Guid, (int Total, int Played)> SeasonStats)> ResolveAggregatedPlayStatsAsync(
        Guid profileId,
        List<Guid> tvShowIds,
        List<Guid> seasonIds)
    {
        var tvStats = new Dictionary<Guid, (int, int)>();
        var seasonStats = new Dictionary<Guid, (int, int)>();

        if (tvShowIds.Count == 0 && seasonIds.Count == 0)
        {
            return (tvStats, seasonStats);
        }

        var episodeMappings = await context.Set<Episode>()
            .AsNoTracking()
            .Where(e => tvShowIds.Contains(e.Season.TvShowId) || seasonIds.Contains(e.SeasonId))
            .Select(e => new { e.Id, e.SeasonId, TvShowId = e.Season.TvShowId })
            .ToListAsync();

        if (episodeMappings.Count == 0)
        {
            return (tvStats, seasonStats);
        }

        var allEpisodeIds = episodeMappings.Select(e => e.Id).ToList();

        var playedEpisodeIds = await context.Set<UserMediaState>()
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsPlayed && allEpisodeIds.Contains(s.MediaItemId))
            .Select(s => s.MediaItemId)
            .ToListAsync();

        var playedSet = playedEpisodeIds.ToHashSet();
        var tvShowSet = tvShowIds.ToHashSet();
        var seasonSet = seasonIds.ToHashSet();

        if (tvShowSet.Count > 0)
        {
            tvStats = episodeMappings
                .Where(e => tvShowSet.Contains(e.TvShowId))
                .GroupBy(x => x.TvShowId)
                .ToDictionary(g => g.Key, g => (Total: g.Count(), Played: g.Count(x => playedSet.Contains(x.Id))));
        }

        if (seasonSet.Count > 0)
        {
            seasonStats = episodeMappings
                .Where(e => seasonSet.Contains(e.SeasonId))
                .GroupBy(x => x.SeasonId)
                .ToDictionary(g => g.Key, g => (Total: g.Count(), Played: g.Count(x => playedSet.Contains(x.Id))));
        }

        return (tvStats, seasonStats);
    }

    private static void ApplyUserState(
        CollectionDetailsLibraryItemVM item,
        Dictionary<Guid, bool> directStates,
        Dictionary<Guid, (int Total, int Played)> tvStats,
        Dictionary<Guid, (int Total, int Played)> seasonStats)
    {
        if (item.Type == "Movie" || item.Type == "Episode")
        {
            if (directStates.TryGetValue(item.Id, out var played))
            {
                item.IsPlayed = played;
            }
            return;
        }

        if (item.Type == "TvShow" && tvStats.TryGetValue(item.Id, out var tvStat))
        {
            item.UnplayedItemCount = tvStat.Total - tvStat.Played;
            item.IsPlayed = tvStat.Total > 0 && tvStat.Total == tvStat.Played;
            return;
        }

        if (item.Type == "Season" && seasonStats.TryGetValue(item.Id, out var seasonStat))
        {
            item.UnplayedItemCount = seasonStat.Total - seasonStat.Played;
            item.IsPlayed = seasonStat.Total > 0 && seasonStat.Total == seasonStat.Played;
        }
    }
}
