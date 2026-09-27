using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

internal static class MusicIncludePaths
{
    public const string TrackAlbumArtist = nameof(Track.Album) + "." + nameof(Album.Artist);
}
