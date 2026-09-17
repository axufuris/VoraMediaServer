using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists.ViewModels;

public class CreatePlaylistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Mixed;
}