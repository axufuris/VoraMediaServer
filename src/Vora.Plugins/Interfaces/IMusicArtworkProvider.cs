using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IMusicArtworkProvider : IVoraPlugin
{
    Task<IReadOnlyList<MusicArtworkResult>> SearchAlbumArtworkAsync(string artistName, string albumTitle, CancellationToken cancellationToken);
    Task<IReadOnlyList<MusicArtworkResult>> SearchArtistArtworkAsync(string artistName, CancellationToken cancellationToken);
}
