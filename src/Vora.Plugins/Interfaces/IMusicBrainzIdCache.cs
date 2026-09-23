namespace Vora.Plugins.Interfaces;

// Fanart.tv's music API is keyed by MusicBrainz id — an artist's for artist
// artwork, a release group's for album artwork — so a provider cannot ask for
// anything without first resolving one. Resolving means a MusicBrainz search,
// against an API that allows roughly one request per second and answers a burst
// with 500s.
//
// Without somewhere to keep the answer, every artwork refresh re-resolved every
// id, so the cost scaled with the size of the library on every pass instead of
// once per artist ever. A library-wide refresh over a few hundred artists could
// not finish inside the rate limit, and the failures cascaded: no id means no
// Fanart call at all, so backgrounds and banners simply never arrived.
//
// Implemented against the Artist and Album rows, so the id survives a restart.
// A provider is expected to read first, and write back only what it resolves.
public interface IMusicBrainzIdCache
{
    Task<string?> GetArtistIdAsync(string artistName, CancellationToken cancellationToken);

    Task SetArtistIdAsync(string artistName, string musicBrainzId, CancellationToken cancellationToken);

    Task<string?> GetAlbumIdAsync(string artistName, string albumTitle, CancellationToken cancellationToken);

    Task SetAlbumIdAsync(string artistName, string albumTitle, string musicBrainzId, CancellationToken cancellationToken);
}
