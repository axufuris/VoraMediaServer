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
}