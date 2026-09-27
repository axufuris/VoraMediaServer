using Microsoft.Extensions.Logging;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

// Keeps resolved MusicBrainz ids on the Artist and Album rows so a provider asks
// MusicBrainz once per artist ever, rather than once per artwork refresh. See
// IMusicBrainzIdCache for why that matters.
//
// A miss is not an error: an artist the scanner has not created yet, or a name
// the provider spells differently, simply has nowhere to keep the id. The
// provider still works, it just pays for the lookup again next time.
public class MusicBrainzIdCache : IMusicBrainzIdCache
{
    private readonly IMusicRepository _repository;
    private readonly ILogger<MusicBrainzIdCache> _logger;

    public MusicBrainzIdCache(IMusicRepository repository, ILogger<MusicBrainzIdCache> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<string?> GetArtistIdAsync(string artistName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return null;
        var artist = await _repository.FindArtistByNameAsync(artistName);
        return string.IsNullOrWhiteSpace(artist?.MusicBrainzId) ? null : artist.MusicBrainzId;
    }

    public async Task SetArtistIdAsync(string artistName, string musicBrainzId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(musicBrainzId)) return;

        var artist = await _repository.FindArtistByNameAsync(artistName);
        if (artist == null || artist.MusicBrainzId == musicBrainzId) return;

        artist.MusicBrainzId = musicBrainzId;
        await _repository.UpdateArtistAsync(artist);
        _logger.LogDebug("Stored MusicBrainz id for artist {Artist}.", artistName);
    }

    public async Task<string?> GetAlbumIdAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle)) return null;
        var album = await _repository.FindAlbumByArtistAndTitleAsync(artistName, albumTitle);
        return string.IsNullOrWhiteSpace(album?.MusicBrainzId) ? null : album.MusicBrainzId;
    }

    public async Task SetAlbumIdAsync(string artistName, string albumTitle, string musicBrainzId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle) || string.IsNullOrWhiteSpace(musicBrainzId)) return;

        var album = await _repository.FindAlbumByArtistAndTitleAsync(artistName, albumTitle);
        if (album == null || album.MusicBrainzId == musicBrainzId) return;

        album.MusicBrainzId = musicBrainzId;
        await _repository.UpdateAlbumAsync(album);
        _logger.LogDebug("Stored MusicBrainz release-group id for {Artist} - {Album}.", artistName, albumTitle);
    }
}
