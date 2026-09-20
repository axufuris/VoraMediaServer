namespace Vora.Application.Watchlist.ViewModels;

public class WatchlistItemVM
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime AddedAt { get; set; }

    // Set when the title is in the library, so the client can open the local
    // item instead of the external provider page.
    public Guid? MediaItemId { get; set; }
}

public class WatchlistStatusVM
{
    public bool InWatchlist { get; set; }
}
