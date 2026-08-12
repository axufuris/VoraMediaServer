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

    public Task<CollectionChronologyConfigDto?> GetChronologyConfigAsync(Guid collectionId) =>
        context.Collections
            .AsNoTracking()
            .Where(c => c.Id == collectionId)
            .Select(c => new CollectionChronologyConfigDto
            {
                Title = c.Title,
                SortProviderId = c.SortProviderId,
                ExternalListId = c.ExternalListId,
                ChronologyItemsSignature = c.ChronologyItemsSignature
            })
            .FirstOrDefaultAsync();

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

    public Task<List<CollectionMembershipCacheDto>> GetContentSyncMembershipsAsync() =>
        context.Collections
            .AsNoTracking()
            .Where(c => !string.IsNullOrEmpty(c.ContentSyncProviderId) && c.ContentSyncCacheJson != null)
            .Select(c => new CollectionMembershipCacheDto { Id = c.Id, ContentSyncCacheJson = c.ContentSyncCacheJson })
            .ToListAsync();

    public Task UpdateChronologySignatureAsync(Guid collectionId, string signature) =>
        context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ChronologyItemsSignature, signature)
                .SetProperty(c => c.ChronologySyncedAt, DateTime.UtcNow));

    public Task SetItemInUniverseYearAsync(Guid collectionId, Guid mediaItemId, double? year, bool locked) =>
        context.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId && ci.MediaItemId == mediaItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ci => ci.InUniverseYear, year)
                .SetProperty(ci => ci.InUniverseYearLocked, locked));

    public async Task ResetChronologyCacheAsync(Guid collectionId)
    {
        await context.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId)
            .ExecuteUpdateAsync(s => s.SetProperty(ci => ci.InUniverseYear, (double?)null));

        await context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ChronologyItemsSignature, string.Empty));
    }

    public Task TouchChronologySyncedAtAsync(Guid collectionId) =>
        context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ChronologySyncedAt, DateTime.UtcNow));

    public Task UpdateContentSyncCacheAsync(Guid collectionId, string cacheJson) =>
        context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ContentSyncCacheJson, cacheJson)
                .SetProperty(c => c.ContentSyncedAt, DateTime.UtcNow));

    public Task UpdateDescriptionAsync(Guid collectionId, string description) =>
        context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, description));

    public Task RemoveItemsFromCollectionAsync(Guid collectionId, IEnumerable<Guid> mediaItemIds)
    {
        var ids = mediaItemIds.ToList();
        if (ids.Count == 0) return Task.CompletedTask;
        return context.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId && ids.Contains(ci.MediaItemId))
            .ExecuteDeleteAsync();
    }

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
            .Where(ci => ci.CollectionId == collectionId && ci.MediaItem.MissingSince == null)
            .ToListAsync();

    public Task<Dictionary<Guid, decimal>> GetCollectionItemSortOrdersAsync(Guid collectionId) =>
        context.Set<CollectionItem>()
            .AsNoTracking()
            .Where(ci => ci.CollectionId == collectionId)
            .ToDictionaryAsync(ci => ci.MediaItemId, ci => ci.SortOrder);

    public async Task<Dictionary<Guid, (double? Year, bool Locked)>> GetCollectionItemChronologyAsync(Guid collectionId)
    {
        var rows = await context.Set<CollectionItem>()
            .AsNoTracking()
            .Where(ci => ci.CollectionId == collectionId)
            .Select(ci => new { ci.MediaItemId, ci.InUniverseYear, ci.InUniverseYearLocked })
            .ToListAsync();

        return rows.ToDictionary(r => r.MediaItemId, r => (r.InUniverseYear, r.InUniverseYearLocked));
    }

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
            SortOrder = (maxSortOrder ?? 0) + 1,
            ManuallyAdded = true
        });
        await context.SaveChangesAsync();
    }

    public async Task<HashSet<Guid>> GetManuallyAddedMediaIdsAsync(Guid collectionId)
    {
        var ids = await context.Set<CollectionItem>()
            .AsNoTracking()
            .Where(ci => ci.CollectionId == collectionId && ci.ManuallyAdded)
            .Select(ci => ci.MediaItemId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    public async Task<HashSet<Guid>> GetExcludedMediaIdsAsync(Guid collectionId)
    {
        var json = await context.Collections
            .AsNoTracking()
            .Where(c => c.Id == collectionId)
            .Select(c => c.ExcludedMediaIdsJson)
            .FirstOrDefaultAsync();
        return ParseExcluded(json);
    }

    public async Task AddExcludedMediaIdAsync(Guid collectionId, Guid mediaItemId)
    {
        var collection = await context.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection == null) return;

        var set = ParseExcluded(collection.ExcludedMediaIdsJson);
        if (set.Add(mediaItemId))
        {
            collection.ExcludedMediaIdsJson = System.Text.Json.JsonSerializer.Serialize(set);
            await context.SaveChangesAsync();
        }
    }

    public async Task RemoveExcludedMediaIdAsync(Guid collectionId, Guid mediaItemId)
    {
        var collection = await context.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection == null) return;

        var set = ParseExcluded(collection.ExcludedMediaIdsJson);
        if (set.Remove(mediaItemId))
        {
            collection.ExcludedMediaIdsJson = set.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(set);
            await context.SaveChangesAsync();
        }
    }

    private static HashSet<Guid> ParseExcluded(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<Guid>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<HashSet<Guid>>(json) ?? new HashSet<Guid>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new HashSet<Guid>();
        }
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
