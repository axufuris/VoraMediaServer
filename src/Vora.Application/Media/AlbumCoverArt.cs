using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public static class AlbumCoverArt
{
    public static string? Resolve(string? albumArtworkUrl, string? artistArtworkUrl)
    {
        if (!string.IsNullOrWhiteSpace(albumArtworkUrl)) return albumArtworkUrl;
        return string.IsNullOrWhiteSpace(artistArtworkUrl) ? null : artistArtworkUrl;
    }

    public static string? For(Album? album) =>
        album == null ? null : Resolve(album.ArtworkUrl, album.Artist?.ArtworkUrl);

    public static string? FirstFor(IEnumerable<Track> tracks)
    {
        var albums = tracks.Select(t => t.Album).ToList();
        return albums.Select(a => a?.ArtworkUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
            ?? albums.Select(For).FirstOrDefault(u => u != null);
    }
}
