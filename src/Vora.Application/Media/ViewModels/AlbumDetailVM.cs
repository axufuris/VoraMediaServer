namespace Vora.Application.Media.ViewModels;

public class AlbumDetailVM
{
    public AlbumVM Album { get; set; } = new();
    public List<TrackVM> Tracks { get; set; } = new();
}
