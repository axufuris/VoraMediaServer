namespace Vora.Application.Media.ViewModels;

public class AlbumDetailVM
{
    public AlbumVM Album { get; set; } = new();
    public List<TrackVM> Tracks { get; set; } = new();

    // The album ARTIST's background, so a client can dress the album page when
    // the album has none of its own. Fanart's album coverage is thin and embedded
    // cover art never carries a background, so most albums have none while the
    // artist one tap away does.
    //
    // Deliberately a separate field rather than filling AlbumVM.BackgroundUrl.
    // The edit modal seeds its form from album.backgroundUrl and writes it
    // straight back, so substituting the artist's url there would stamp it onto
    // the album row the first time an admin opened the modal to change anything
    // at all — a display fallback silently becoming the album's own data, after
    // which it would stop following the artist and nothing would report it.
    //
    // Detail response only. AlbumVM is the list item for album grids, genre pages
    // and artist detail, where this would be payload on every row that nothing
    // renders.
    public string? ArtistBackgroundUrl { get; set; }
}
