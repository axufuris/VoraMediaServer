using Microsoft.EntityFrameworkCore;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class WatchlistRepository(VoraDbContext context) : IWatchlistRepository
{
    public async Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId) =>
        await context.UserWatchlistItems
            .AsNoTracking()
            .Where(w => w.ProfileId == profileId)
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync();

    // Matches on either half of the identity, so a row added from Discovery is
    // found when the same title is toggled from the library and vice versa.
    public Task<UserWatchlistItem?> FindAsync(Guid profileId, string? externalId, string? providerId, Guid? mediaItemId)
    {
        var hasExternal = !string.IsNullOrWhiteSpace(externalId) && !string.IsNullOrWhiteSpace(providerId);
        if (!hasExternal && mediaItemId == null) return Task.FromResult<UserWatchlistItem?>(null);

        return context.UserWatchlistItems.FirstOrDefaultAsync(w =>
            w.ProfileId == profileId
            && ((hasExternal && w.ExternalId == externalId && w.ProviderId == providerId)
                || (mediaItemId != null && w.MediaItemId == mediaItemId)));
    }

    public async Task AddAsync(UserWatchlistItem item)
    {
        await context.UserWatchlistItems.AddAsync(item);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(UserWatchlistItem item)
    {
        context.UserWatchlistItems.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task SetMediaItemIdAsync(Guid watchlistItemId, Guid mediaItemId) =>
        await context.UserWatchlistItems
            .Where(w => w.Id == watchlistItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.MediaItemId, mediaItemId));

    public async Task SetReleaseDateAsync(Guid watchlistItemId, DateOnly releaseDate) =>
        await context.UserWatchlistItems
            .Where(w => w.Id == watchlistItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.ReleaseDate, releaseDate));

    public async Task<WatchlistTitle?> GetTitleAsync(Guid mediaItemId)
    {
        var showId = await context.Set<Episode>().AsNoTracking()
                .Where(e => e.Id == mediaItemId)
                .Select(e => (Guid?)e.Season.TvShowId)
                .FirstOrDefaultAsync()
            ?? await context.Set<Season>().AsNoTracking()
                .Where(s => s.Id == mediaItemId)
                .Select(s => (Guid?)s.TvShowId)
                .FirstOrDefaultAsync();
        var titleId = showId ?? mediaItemId;

        var title = await context.MediaItems.AsNoTracking()
            .Where(m => m.Id == titleId)
            .Select(m => new { Type = m is Movie ? "Movie" : m is TvShow ? "TvShow" : null, m.Title, m.PosterUrl })
            .FirstOrDefaultAsync();
        return title?.Type is null ? null : new WatchlistTitle(titleId, title.Type, title.Title, title.PosterUrl);
    }

    public Task<bool> IsFinishedAsync(Guid profileId, Guid titleId) =>
        context.MediaItems.AsNoTracking()
            .Where(m => m.Id == titleId)
            .Where(TitleWatchState.IsPlayed(profileId, context.UserMediaStates))
            .AnyAsync();
}
