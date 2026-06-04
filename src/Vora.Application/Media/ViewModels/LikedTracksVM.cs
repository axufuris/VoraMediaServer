namespace Vora.Application.Media.ViewModels;

public class LikedTracksVM
{
    public int Count { get; set; }
    public List<ArtistTrackVM> Tracks { get; set; } = new();
}
