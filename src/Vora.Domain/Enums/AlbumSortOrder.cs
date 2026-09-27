namespace Vora.Domain.Enums;

public enum AlbumSortOrder
{
    RecentlyAdded = 0,
    Alphabetical = 1,

    // World-wide popularity from the listening provider. Albums it has no
    // figure for sort last rather than being dropped, so the grid stays the
    // whole library in a different order.
    Popular = 2
}
