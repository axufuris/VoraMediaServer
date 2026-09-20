namespace Vora.Application.Media.ViewModels;

public class ArtistDetailVM
{
    public ArtistVM Artist { get; set; } = new();
    public List<AlbumVM> Albums { get; set; } = new();
}
