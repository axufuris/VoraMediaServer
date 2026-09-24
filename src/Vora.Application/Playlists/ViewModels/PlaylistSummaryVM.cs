using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists.ViewModels;

public class PlaylistSummaryVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ItemCount { get; set; }
    public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Mixed;
    public List<string> PosterUrls { get; set; } = new();
    public List<string> BackdropUrls { get; set; } = new();

    public bool IsShared { get; set; }

    // Whether the profile asking owns it. A shared playlist opened by anyone
    // else is read-only, and a client needs this to hide the edit controls
    // rather than letting them fail against an owner-only endpoint.
    public bool IsOwner { get; set; }
    public string OwnerName { get; set; } = string.Empty;
}