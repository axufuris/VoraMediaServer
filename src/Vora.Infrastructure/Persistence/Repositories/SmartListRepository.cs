using Microsoft.EntityFrameworkCore;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.SmartLists;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Extensions;

namespace Vora.Infrastructure.Persistence.Repositories;

public class SmartListRepository(VoraDbContext context) : ISmartListRepository
{
    public async Task<List<LibraryItemVM>> GetSmartListItemsAsync(
        Guid? profileId,
        Guid? libraryId,
        SmartListRulesDto? rules,
        SmartListSortBy sortBy,
        int maxItems,
        Guid? collectionId = null,
        bool hasAllAccess = true,
        List<Guid>? allowedLibs = null,
        bool hasAllRatings = true,
        List<string>? allowedMovieRatings = null,
        List<string>? allowedTvRatings = null,
        bool blockUnrated = false)
    {
        var query = context.MediaItems.AsNoTracking().AsQueryable();
        query = query.ApplyAccessFilters(hasAllAccess, allowedLibs ?? new List<Guid>(), hasAllRatings, allowedMovieRatings ?? new List<string>(), allowedTvRatings ?? new List<string>(), blockUnrated);

        if (!hasAllAccess && allowedLibs != null)
        {
            query = query.Where(m => allowedLibs.Contains(m.LibraryId));
        }

        if (libraryId.HasValue)
        {
            query = query.Where(m => m.LibraryId == libraryId.Value);
        }

        query = ApplyContentFilter(query, rules, collectionId, profileId);
        query = ApplyMostWatchedFilter(query, sortBy, profileId);
        query = ApplySortOrder(query, sortBy, profileId);

        // A show should appear at most once in a list row. When the list surfaces
        // episodes, collapse each show to a single episode — the first unwatched
        // one (by season/episode), or its earliest if all are watched. Only the
        // top of the sorted pool is inspected so this stays cheap on large TV
        // libraries; non-episode items pass through untouched.
        if (rules?.MediaTypes != null && rules.MediaTypes.Contains("Episode"))
        {
            var poolSize = Math.Max(maxItems * 10, 100);
            var episodePool = await query
                .Where(m => m is Episode)
                .Take(poolSize)
                .Select(m => new
                {
                    m.Id,
                    ShowId = ((Episode)m).Season.TvShowId,
                    Season = ((Episode)m).Season.SeasonNumber,
                    Episode = ((Episode)m).EpisodeNumber,
                    Played = profileId.HasValue && context.Set<UserMediaState>()
                        .Any(s => s.ProfileId == profileId.Value && s.MediaItemId == m.Id && s.IsPlayed)
                })
                .ToListAsync();

            var keptEpisodeIds = episodePool
                .GroupBy(e => e.ShowId)
                .Select(g => (g.Where(e => !e.Played).OrderBy(e => e.Season).ThenBy(e => e.Episode).FirstOrDefault()
                              ?? g.OrderBy(e => e.Season).ThenBy(e => e.Episode).First()).Id)
                .ToHashSet();

            query = query.Where(m => !(m is Episode) || keptEpisodeIds.Contains(m.Id));
        }

        return await query
            .Select(LibraryItemVM.Projection)
            .Take(maxItems)
            .ToListAsync();
    }

    public async Task<List<SmartListClientVM>> GetActiveClientListsAsync(bool isAdmin)
    {
        var lists = await context.Set<SmartList>()
            .AsNoTracking()
            .Where(l => l.ShowOnHomepage && (isAdmin || l.ShowToFriends))
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new
            {
                l.Id,
                l.Title,
                l.DisplayOrder,
                l.ActiveStartMonth,
                l.ActiveStartDay,
                l.ActiveEndMonth,
                l.ActiveEndDay,
                l.CollectionId,
                CollectionStartDate = l.Collection != null ? l.Collection.VisibleStartDate : null,
                CollectionEndDate = l.Collection != null ? l.Collection.VisibleEndDate : null
            })
            .ToListAsync();

        var now = DateTime.UtcNow;

        return lists
            .Where(l => IsListActive(l.CollectionId, l.CollectionStartDate, l.CollectionEndDate,
                                     l.ActiveStartMonth, l.ActiveStartDay, l.ActiveEndMonth, l.ActiveEndDay, now))
            .Select(l => new SmartListClientVM { Id = l.Id, Title = l.Title, DisplayOrder = l.DisplayOrder })
            .ToList();
    }

    public Task<List<SmartListAdminVM>> GetAllAdminListsAsync() =>
        context.Set<SmartList>()
            .AsNoTracking()
            .OrderBy(l => l.DisplayOrder)
            .Select(SmartListAdminVM.Projection)
            .ToListAsync();

    public Task<SmartList?> GetListByIdAsync(Guid id) =>
        context.SmartLists.FindAsync(id).AsTask();

    public async Task CreateListAsync(SmartList list)
    {
        context.SmartLists.Add(list);
        await context.SaveChangesAsync();
    }

    public async Task UpdateListAsync(SmartList list)
    {
        context.SmartLists.Update(list);
        await context.SaveChangesAsync();
    }

    public async Task<bool> DeleteListAsync(Guid id)
    {
        var rows = await context.SmartLists.Where(s => s.Id == id).ExecuteDeleteAsync();
        return rows > 0;
    }

    public async Task ReorderListsAsync(List<Guid> orderedListIds)
    {
        var lists = await context.SmartLists.ToListAsync();

        for (var i = 0; i < orderedListIds.Count; i++)
        {
            var list = lists.FirstOrDefault(l => l.Id == orderedListIds[i]);
            if (list != null)
            {
                list.DisplayOrder = i;
            }
        }

        await context.SaveChangesAsync();
    }

    public async Task AttachLibraryItemUserStatesAsync(IEnumerable<LibraryItemVM> items, Guid profileId)
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
        var tvStats = await ResolveTvShowPlayStatsAsync(tvShowIds, profileId);

        foreach (var item in itemList)
        {
            ApplyUserState(item, directStates, tvStats);
        }
    }

    private IQueryable<MediaItem> ApplyContentFilter(IQueryable<MediaItem> query, SmartListRulesDto? rules, Guid? collectionId, Guid? profileId)
    {
        if (collectionId.HasValue)
        {
            var collectionMediaIds = context.CollectionItems
                .Where(ci => ci.CollectionId == collectionId.Value)
                .Select(ci => ci.MediaItemId);

            return query.Where(m => collectionMediaIds.Contains(m.Id));
        }

        if (rules == null)
        {
            return query.Where(m => m is Movie || m is TvShow);
        }

        if (rules.GenreIds != null && rules.GenreIds.Any())
        {
            query = query.Where(m => m.Genres.Any(g => rules.GenreIds.Contains(g.Id)));
        }

        if (rules.Decade.HasValue)
        {
            var startYear = rules.Decade.Value;
            var endYear = startYear + 9;
            query = query.Where(m => m.ReleaseDate.HasValue
                && m.ReleaseDate.Value.Year >= startYear
                && m.ReleaseDate.Value.Year <= endYear);
        }

        if (rules.MediaTypes != null && rules.MediaTypes.Any())
        {
            query = query.Where(m =>
                (rules.MediaTypes.Contains("Movie") && m is Movie) ||
                (rules.MediaTypes.Contains("TvShow") && m is TvShow) ||
                (rules.MediaTypes.Contains("Season") && m is Season) ||
                (rules.MediaTypes.Contains("Episode") && m is Episode) ||
                (rules.MediaTypes.Contains("Track") && m is Track));
        }
        else
        {
            query = query.Where(m => m is Movie || m is TvShow);
        }

        if (!string.IsNullOrEmpty(rules.ContentRating))
        {
            query = query.Where(m => m.ContentRating == rules.ContentRating);
        }

        if (rules.UnwatchedOnly == true && profileId.HasValue)
        {
            var pid = profileId.Value;
            query = query.Where(m => !context.UserMediaStates.Any(s => s.ProfileId == pid && s.MediaItemId == m.Id && s.IsPlayed));
        }

        return query;
    }

    private IQueryable<MediaItem> ApplyMostWatchedFilter(IQueryable<MediaItem> query, SmartListSortBy sortBy, Guid? profileId)
    {
        if (sortBy != SmartListSortBy.MostWatched)
        {
            return query;
        }

        if (!profileId.HasValue)
        {
            return query.Where(m => false);
        }

        var userSessions = context.StreamSessions.Where(s => s.UserProfileId == profileId.Value);
        return query.Where(m =>
            userSessions.Any(s => s.MediaItemId == m.Id) ||
            context.Set<Episode>().Any(e => e.Season.TvShowId == m.Id && userSessions.Any(s => s.MediaItemId == e.Id)));
    }

    private IQueryable<MediaItem> ApplySortOrder(IQueryable<MediaItem> query, SmartListSortBy sortBy, Guid? profileId)
    {
        switch (sortBy)
        {
            case SmartListSortBy.ReleaseDateDesc:
                return query.OrderByDescending(m => m.ReleaseDate);
            case SmartListSortBy.ReleaseDateAsc:
                return query.OrderBy(m => m.ReleaseDate);
            case SmartListSortBy.TopRated:
                return query.OrderByDescending(m => m.ThirdPartyRating1);
            case SmartListSortBy.Random:
                return query.OrderBy(m => EF.Functions.Random());
            case SmartListSortBy.MostWatched:
                if (!profileId.HasValue) return query.OrderByDescending(m => m.AddedAt);
                var pid = profileId.Value;
                return query.OrderByDescending(m =>
                    context.StreamSessions.Count(s => s.UserProfileId == pid && s.MediaItemId == m.Id) +
                    context.Set<Episode>().Where(e => e.Season.TvShowId == m.Id)
                        .SelectMany(e => context.StreamSessions.Where(s => s.UserProfileId == pid && s.MediaItemId == e.Id))
                        .Count());
            default:
                return query.OrderByDescending(m => m.AddedAt);
        }
    }

    private async Task<Dictionary<Guid, (int Total, int Played)>> ResolveTvShowPlayStatsAsync(List<Guid> tvShowIds, Guid profileId)
    {
        if (tvShowIds.Count == 0)
        {
            return new Dictionary<Guid, (int, int)>();
        }

        var episodeMappings = await context.Set<Episode>()
            .AsNoTracking()
            .Where(e => tvShowIds.Contains(e.Season.TvShowId))
            .Select(e => new { e.Id, TvShowId = e.Season.TvShowId })
            .ToListAsync();

        if (episodeMappings.Count == 0)
        {
            return new Dictionary<Guid, (int, int)>();
        }

        var allEpisodeIds = episodeMappings.Select(e => e.Id).ToList();

        var playedEpisodeIds = await context.Set<UserMediaState>()
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsPlayed && allEpisodeIds.Contains(s.MediaItemId))
            .Select(s => s.MediaItemId)
            .ToListAsync();

        var playedSet = playedEpisodeIds.ToHashSet();

        return episodeMappings
            .GroupBy(x => x.TvShowId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Played: g.Count(x => playedSet.Contains(x.Id))));
    }

    private static void ApplyUserState(LibraryItemVM item, Dictionary<Guid, bool> directStates, Dictionary<Guid, (int Total, int Played)> tvStats)
    {
        if (item.Type == "Movie" || item.Type == "Episode")
        {
            if (directStates.TryGetValue(item.Id, out var played))
            {
                item.IsPlayed = played;
            }
            return;
        }

        if (item.Type == "TvShow" && tvStats.TryGetValue(item.Id, out var stats))
        {
            item.UnplayedItemCount = stats.Total - stats.Played;
            item.IsPlayed = stats.Total > 0 && stats.Total == stats.Played;
        }
    }

    private static bool IsListActive(
        Guid? collectionId,
        DateTime? collectionStart,
        DateTime? collectionEnd,
        int? startMonth,
        int? startDay,
        int? endMonth,
        int? endDay,
        DateTime now)
    {
        if (collectionId.HasValue)
        {
            if (collectionStart.HasValue && now < collectionStart.Value) return false;
            if (collectionEnd.HasValue && now > collectionEnd.Value) return false;
            return true;
        }

        if (!startMonth.HasValue || !startDay.HasValue || !endMonth.HasValue || !endDay.HasValue)
        {
            return true;
        }

        var startDate = new DateTime(2000, startMonth.Value, startDay.Value);
        var endDate = new DateTime(2000, endMonth.Value, endDay.Value);
        var current = new DateTime(2000, now.Month, now.Day);

        return startDate > endDate
            ? current >= startDate || current <= endDate
            : current >= startDate && current <= endDate;
    }
}
