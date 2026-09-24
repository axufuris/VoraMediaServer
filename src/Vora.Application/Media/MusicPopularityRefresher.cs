using Microsoft.Extensions.Logging;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IMusicPopularityRefresher
{
    Task<int> RefreshDueArtistsAsync(CancellationToken cancellationToken);
}

// World-wide popularity changes slowly, so it is fetched once and kept, and only
// asked for again once it has gone stale. The first run covers the library; after
// that each run touches roughly a thirtieth of it.
//
// Per artist, one provider round trip covers the artist, its albums and its
// tracks. Asking per track would cost one call per track in the library — tens of
// thousands for a large one — where this costs three per artist.
public class MusicPopularityRefresher : IMusicPopularityRefresher
{
    // Popularity is the slowest-moving thing Vora asks a provider about.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    // A safety valve for a very large library, not the expected case: a
    // 161-artist library is covered in one run.
    public const int MaxArtistsPerRun = 500;

    // Enough to match most of an artist's catalogue, small enough that the
    // payload stays modest. Tracks outside an artist's top fifty stay null.
    public const int TopTracksPerArtist = 50;
    public const int TopAlbumsPerArtist = 50;

    // Last.fm allows about five requests a second averaged over five minutes. A
    // refresh makes three in quick succession, so pausing between artists keeps
    // the average comfortably under that.
    private static readonly TimeSpan PauseBetweenArtists = TimeSpan.FromMilliseconds(750);

    private readonly IMusicRepository _repository;
    private readonly IEnumerable<IListeningDataProvider> _providers;
    private readonly ILogger<MusicPopularityRefresher> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;

    public MusicPopularityRefresher(
        IMusicRepository repository,
        IEnumerable<IListeningDataProvider> providers,
        ILogger<MusicPopularityRefresher> logger)
        : this(repository, providers, logger, Task.Delay)
    {
    }

    internal MusicPopularityRefresher(
        IMusicRepository repository,
        IEnumerable<IListeningDataProvider> providers,
        ILogger<MusicPopularityRefresher> logger,
        Func<TimeSpan, CancellationToken, Task> pause)
    {
        _repository = repository;
        _providers = providers;
        _logger = logger;
        _pause = pause;
    }

    public async Task<int> RefreshDueArtistsAsync(CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault();
        if (provider == null) return 0;

        var due = await _repository.GetArtistsDueForPopularityRefreshAsync(DateTime.UtcNow - StaleAfter, MaxArtistsPerRun);
        if (due.Count == 0) return 0;

        var refreshed = 0;
        foreach (var target in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var popularity = await provider.GetArtistPopularityAsync(target.ArtistName, TopTracksPerArtist, TopAlbumsPerArtist, cancellationToken);

            // Stop rather than skip. An unavailable provider is almost always
            // unavailable for everyone — not configured, offline, rate-limited —
            // so carrying on spends the rest of the batch asking something that
            // is not answering. The artist stays unrefreshed and is first in line
            // next run.
            if (popularity.Outcome == PopularityLookupOutcome.Unavailable)
            {
                _logger.LogInformation(
                    "Stopping the music popularity refresh after {Refreshed} of {Due} artists: {Provider} is not answering.",
                    refreshed, due.Count, provider.ProviderName);
                break;
            }

            var artist = await _repository.GetArtistCatalogForUpdateAsync(target.ArtistId);
            if (artist == null) continue;

            Apply(artist, popularity, DateTime.UtcNow);
            await _repository.SaveMusicChangesAsync(cancellationToken);
            refreshed++;

            await _pause(PauseBetweenArtists, cancellationToken);
        }

        // No client notification. Popularity is not something anyone watches
        // change, and one event per artist would have every open client refetch
        // a hundred times in a row; the numbers are simply there on next load.
        if (refreshed > 0)
        {
            _logger.LogInformation("Refreshed music popularity for {Refreshed} artist(s).", refreshed);
        }

        return refreshed;
    }

    // Written as one snapshot of the whole catalogue. Values from the previous
    // refresh are cleared first, so a track that has dropped out of the artist's
    // top fifty goes back to null instead of keeping a number from a month ago
    // beside fresher ones it would be sorted against.
    internal static void Apply(Artist artist, ArtistPopularity popularity, DateTime refreshedAt)
    {
        artist.PopularityRefreshedAt = refreshedAt;

        if (popularity.Outcome != PopularityLookupOutcome.Found) return;

        artist.GlobalListeners = popularity.Listeners;
        artist.GlobalPlays = popularity.Plays;

        var albumsByName = ByKey(popularity.TopAlbums);
        var tracksByName = ByKey(popularity.TopTracks);

        foreach (var album in artist.Albums)
        {
            album.GlobalPlays = albumsByName.TryGetValue(MusicNameKey.Normalize(album.Title), out var a) ? a.Plays : null;

            foreach (var track in album.Tracks)
            {
                if (tracksByName.TryGetValue(MusicNameKey.Normalize(track.Title), out var t))
                {
                    track.GlobalListeners = t.Listeners;
                    track.GlobalPlays = t.Plays;
                }
                else
                {
                    track.GlobalListeners = null;
                    track.GlobalPlays = null;
                }
            }
        }
    }

    // First occurrence wins. A provider list is ranked, so if two entries
    // normalise to the same name the higher-ranked one is the one that counts.
    private static Dictionary<string, NamedPopularity> ByKey(IEnumerable<NamedPopularity> items)
    {
        var map = new Dictionary<string, NamedPopularity>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = MusicNameKey.Normalize(item.Name);
            if (key.Length > 0) map.TryAdd(key, item);
        }
        return map;
    }
}
