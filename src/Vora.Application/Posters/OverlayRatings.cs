using Vora.Domain.Entities.Media;

namespace Vora.Application.Posters;

// The three ratings a poster overlay's ratings cluster draws, each in its own
// slot: the server (Vora) rating, then the library's Third Party Rating 1 and 2
// (docs/artwork-image-cache.md, "Ratings cluster").
public sealed record OverlayRatings(
    decimal? ServerAdminRating,
    string? Rating1Name,
    decimal? Rating1,
    string? Rating2Name,
    decimal? Rating2)
{
    public static OverlayRatings Of(MediaItem item) => new(
        item.ServerAdminRating,
        item.ThirdPartyRating1Name,
        item.ThirdPartyRating1,
        item.ThirdPartyRating2Name,
        item.ThirdPartyRating2);

    // A season's ratings with its show's standing in for the ones it doesn't
    // have, one rating at a time: a season with its own Rotten Tomatoes score but
    // no Vora rating draws its own Rotten Tomatoes score and the show's Vora
    // rating. Once the season has a score of its own, that one is drawn instead.
    // A show's score fills the same slot on the season, unless the season already
    // draws a score from that source in its other slot, so no source is drawn
    // twice.
    public OverlayRatings OrShows(OverlayRatings show)
    {
        var (name1, rating1) = Rating1.HasValue ? (Rating1Name, Rating1) : Borrow(show.Rating1Name, show.Rating1, Rating1Name, Rating1);
        var (name2, rating2) = Rating2.HasValue ? (Rating2Name, Rating2) : Borrow(show.Rating2Name, show.Rating2, Rating2Name, Rating2);
        return new OverlayRatings(ServerAdminRating ?? show.ServerAdminRating, name1, rating1, name2, rating2);
    }

    private (string? Name, decimal? Rating) Borrow(string? showName, decimal? showRating, string? ownName, decimal? ownRating)
    {
        var alreadyDrawn = (Rating1.HasValue && SameSource(Rating1Name, showName))
            || (Rating2.HasValue && SameSource(Rating2Name, showName));
        return showRating.HasValue && !alreadyDrawn ? (showName, showRating) : (ownName, ownRating);
    }

    private static bool SameSource(string? a, string? b) =>
        a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
