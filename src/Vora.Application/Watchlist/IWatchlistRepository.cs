using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Watchlist;

public interface IWatchlistRepository
{
    Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId);
    Task<UserWatchlistItem?> FindAsync(Guid profileId, string? externalId, string? providerId, Guid? mediaItemId);
    Task AddAsync(UserWatchlistItem item);
    Task RemoveAsync(UserWatchlistItem item);
    Task SetMediaItemIdAsync(Guid watchlistItemId, Guid mediaItemId);

    // The movie or show a library item counts toward: an episode or a season
    // belongs to its show. Null for anything a watchlist can't hold.
    Task<WatchlistTitle?> GetTitleAsync(Guid mediaItemId);

    // Whether the profile has watched all of a movie or show, by the rule the
    // library's own watched check uses (TitleWatchState).
    Task<bool> IsFinishedAsync(Guid profileId, Guid titleId);
}

// Type is what a watchlist entry calls it: "Movie" or "TvShow".
public sealed record WatchlistTitle(Guid Id, string Type);
