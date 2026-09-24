using Vora.Application.Media.SmartPlaylists;

namespace Vora.Application.Playlists.ViewModels;

// Everything other profiles have shared, in one response, because the Shared
// tab shows both kinds side by side the way the owner's own list does. Kept as
// two lists rather than merged into a common shape so a client renders each
// with the tile it already has for that kind.
public class SharedPlaylistsVM
{
    public List<PlaylistSummaryVM> Manual { get; set; } = new();
    public List<SmartPlaylistSummaryVM> Smart { get; set; } = new();
}
