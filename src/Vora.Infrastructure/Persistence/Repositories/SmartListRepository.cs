using System.Linq.Expressions;
using Vora.Application.Media;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Collections;
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
        List<string>? allowedMovieRatings = null,
        List<string>? allowedTvRatings = null,
        bool blockUnrated = false)
    {
        var query = context.MediaItems.AsNoTracking().AsQueryable();
        query = query.ApplyAccessFilters(hasAllAccess, allowedLibs ?? new List<Guid>(), allowedMovieRatings ?? new List<string>(), allowedTvRatings ?? new List<string>(), blockUnrated);

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

        var poolSize = Math.Max(maxItems * 10, 100);
        if (sortBy == SmartListSortBy.DateAddedDesc && TvLevelCount(rules) > 1)
        {
            var keptTvIds = await OneTilePerShowAsync(query, maxItems, poolSize, profileId);
            query = query.Where(MediaCapabilities.IsPartOfATvShow.Not().Or(m => keptTvIds.Contains(m.Id)));
        }
        else if (rules?.MediaTypes != null && rules.MediaTypes.Contains("Episode"))
        {
            var episodePool = await LoadTvPoolAsync(query.Where(m => m is Episode), poolSize, profileId);
            var keptEpisodeIds = episodePool
                .GroupBy(e => e.ShowId)
                .Select(g => RecentlyAddedTv.PickEpisode(g.ToList()).Id)
                .ToHashSet();

            query = query.Where(m => !(m is Episode) || keptEpisodeIds.Contains(m.Id));
        }

        var items = await query
            .Select(LibraryItemVM.Projection)
            .Take(maxItems)
            .ToListAsync();

        await AttachTrackArtworkAsync(items);
        return items;
    }

    private static int TvLevelCount(SmartListRulesDto? rules) =>
        rules?.MediaTypes == null ? 0 : new[] { "TvShow", "Season", "Episode" }.Count(rules.MediaTypes.Contains);

    private async Task<List<Guid>> OneTilePerShowAsync(IQueryable<MediaItem> query, int maxItems, int poolSize, Guid? profileId)
    {
        var kept = new List<Guid>();
        var showsSeen = new List<Guid>();

        while (kept.Count < maxItems)
        {
            var pool = await LoadTvPoolAsync(query.Where(OutsideShows(showsSeen.ToList())), poolSize, profileId);
            if (pool.Count == 0) break;

            kept.AddRange(RecentlyAddedTv.OnePerShow(pool));
            showsSeen.AddRange(pool.Select(c => c.ShowId).Distinct());
            if (pool.Count < poolSize) break;
        }

        return kept;
    }

    private static Expression<Func<MediaItem, bool>> OutsideShows(List<Guid> showIds) =>
        m => !(m is TvShow && showIds.Contains(m.Id))
            && !(m is Season && showIds.Contains(((Season)m).TvShowId))
            && !(m is Episode && showIds.Contains(((Episode)m).Season.TvShowId));

    private async Task<List<TvCandidate>> LoadTvPoolAsync(IQueryable<MediaItem> query, int poolSize, Guid? profileId)
    {
        var rows = await query
            .Where(MediaCapabilities.IsPartOfATvShow)
            .Take(poolSize)
            .Select(m => new
            {
                m.Id,
                IsShow = m is TvShow,
                IsSeason = m is Season,
                ShowId = m is TvShow ? m.Id : m is Season ? ((Season)m).TvShowId : ((Episode)m).Season.TvShowId,
                SeasonId = m is Season ? m.Id : m is Episode ? ((Episode)m).SeasonId : (Guid?)null,
                SeasonNumber = m is Season ? ((Season)m).SeasonNumber : m is Episode ? ((Episode)m).Season.SeasonNumber : 0,
                EpisodeNumber = m is Episode ? ((Episode)m).EpisodeNumber : 0,
                m.AddedAt,
                m.LastContentAddedAt,
                Played = profileId.HasValue && context.Set<UserMediaState>()
                    .Any(s => s.ProfileId == profileId.Value && s.MediaItemId == m.Id && s.IsPlayed)
            })
            .ToListAsync();

        return rows.Select(r => new TvCandidate(
            r.Id,
            r.IsShow ? TvLevel.Show : r.IsSeason ? TvLevel.Season : TvLevel.Episode,
            r.ShowId,
            r.SeasonId,
            r.SeasonNumber,
            r.EpisodeNumber,
            r.AddedAt,
            r.LastContentAddedAt,
            r.Played)).ToList();
    }

    private async Task AttachTrackArtworkAsync(List<LibraryItemVM> items)
    {
        var trackIds = items.Where(i => i.Type == "Track" && i.PosterUrl == null).Select(i => i.Id).ToList();
        if (trackIds.Count == 0)
        {
            return;
        }

        var artwork = await context.Set<Track>()
            .AsNoTracking()
            .Where(t => trackIds.Contains(t.Id) && t.Album != null)
            .Select(t => new { t.Id, ArtworkUrl = t.Album != null ? t.Album.ArtworkUrl : null })
            .ToDictionaryAsync(t => t.Id, t => t.ArtworkUrl);

        foreach (var item in items)
        {
            if (artwork.TryGetValue(item.Id, out var url) && url != null)
            {
                item.PosterUrl = url;
            }
        }
    }

    public async Task<List<SmartListClientVM>> GetActiveClientListsAsync(bool isAdmin, DateOnly today)
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
                l.Source,
                l.ActiveStartMonth,
                l.ActiveStartDay,
                l.ActiveEndMonth,
                l.ActiveEndDay,
                l.CollectionId,
                CollectionStartDate = l.Collection != null ? l.Collection.VisibleStartDate : null,
                CollectionEndDate = l.Collection != null ? l.Collection.VisibleEndDate : null,
                CollectionEveryYear = l.Collection != null && l.Collection.VisibleEveryYear
            })
            .ToListAsync();

        return lists
            .Where(l => IsListActive(l.ActiveStartMonth, l.ActiveStartDay, l.ActiveEndMonth, l.ActiveEndDay, today)
                && CollectionSeason.IsOpen(l.CollectionStartDate, l.CollectionEndDate, l.CollectionEveryYear, today))
            .Select(l => new SmartListClientVM { Id = l.Id, Title = l.Title, DisplayOrder = l.DisplayOrder, Source = l.Source })
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

    public async Task<HashSet<string>> GetDefaultKeysAsync()
    {
        var keys = await context.SmartLists
            .AsNoTracking()
            .Where(l => l.DefaultKey != null)
            .Select(l => l.DefaultKey ?? string.Empty)
            .ToListAsync();
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<int> GetMaxDisplayOrderAsync() =>
        await context.SmartLists.AnyAsync()
            ? await context.SmartLists.MaxAsync(l => l.DisplayOrder)
            : -1;

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
            return query.Where(MediaCapabilities.IsBrowsableTitle);
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
            query = query.Where(MediaCapabilities.IsBrowsableTitle);
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
                return query.OrderByDescending(m => m.ReleaseDate.HasValue).ThenByDescending(m => m.ReleaseDate);
            case SmartListSortBy.ReleaseDateAsc:
                return query.OrderByDescending(m => m.ReleaseDate.HasValue).ThenBy(m => m.ReleaseDate);
            case SmartListSortBy.TopRated:
                return query.OrderByDescending(m => m.ThirdPartyRating1.HasValue).ThenByDescending(m => m.ThirdPartyRating1);
            case SmartListSortBy.Random:
                return query.OrderBy(m => EF.Functions.Random());
            case SmartListSortBy.TitleAsc:
                return query.OrderBy(m => m.SortTitle ?? m.Title).ThenBy(m => m.Id);
            case SmartListSortBy.MostWatched:
                if (!profileId.HasValue) return query.OrderByDescending(m => m.LastContentAddedAt ?? m.AddedAt);
                var pid = profileId.Value;
                return query.OrderByDescending(m =>
                    context.StreamSessions.Count(s => s.UserProfileId == pid && s.MediaItemId == m.Id) +
                    context.Set<Episode>().Where(e => e.Season.TvShowId == m.Id)
                        .SelectMany(e => context.StreamSessions.Where(s => s.UserProfileId == pid && s.MediaItemId == e.Id))
                        .Count());
            default:
                return query.OrderByDescending(m => m.LastContentAddedAt ?? m.AddedAt);
        }
    }

    private static bool IsListActive(int? startMonth, int? startDay, int? endMonth, int? endDay, DateOnly today)
    {
        if (!startMonth.HasValue || !startDay.HasValue || !endMonth.HasValue || !endDay.HasValue)
        {
            return true;
        }

        return CollectionSeason.IsOpen(
            new DateTime(2000, startMonth.Value, startDay.Value),
            new DateTime(2000, endMonth.Value, endDay.Value),
            everyYear: true,
            today);
    }
}
