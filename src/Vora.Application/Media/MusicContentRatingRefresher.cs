using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IMusicContentRatingRefresher
{
    Task<int> RateDueAlbumsAsync(CancellationToken cancellationToken);
}

// Fills Clean / Explicit for tracks whose files carry no advisory tag. Only
// ever fills a blank: a rating read from the file is never asked about, and a
// locked one is never touched.
//
// Per track, an ISRC is asked about first, because it names the exact recording
// and so tells the clean edit from the explicit original. A track without one,
// or one the provider does not know, falls back to every edition of its album,
// strictest answer wins (see MusicEditionMatcher). The album lookup is made at
// most once per album, and only if some track needs it.
public class MusicContentRatingRefresher : IMusicContentRatingRefresher
{
    // A provider that knew nothing about a track may have learned it since —
    // labels deliver catalogue late — so an unanswered track is asked again, but
    // rarely.
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(90);

    // Enough to cover a large library in a few nights. At a few calls per album,
    // a full run stays well inside an hour.
    public const int MaxAlbumsPerRun = 1000;

    // Deezer allows 50 requests every 5 seconds. One pause per request keeps a
    // run at under seven a second, with room for anything else calling it.
    private static readonly TimeSpan PauseBetweenRequests = TimeSpan.FromMilliseconds(150);

    private const string EnabledSettingKey = "is_enabled";
    private const string DisabledValue = "false";

    private readonly IMusicRepository _repository;
    private readonly IEnumerable<IMusicContentRatingProvider> _providers;
    private readonly ISystemSettingsRepository _settings;
    private readonly ITaskProgressReporter _progress;
    private readonly ILogger<MusicContentRatingRefresher> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;

    public MusicContentRatingRefresher(
        IMusicRepository repository,
        IEnumerable<IMusicContentRatingProvider> providers,
        ISystemSettingsRepository settings,
        ITaskProgressReporter progress,
        ILogger<MusicContentRatingRefresher> logger)
        : this(repository, providers, settings, progress, logger, Task.Delay)
    {
    }

    internal MusicContentRatingRefresher(
        IMusicRepository repository,
        IEnumerable<IMusicContentRatingProvider> providers,
        ISystemSettingsRepository settings,
        ITaskProgressReporter progress,
        ILogger<MusicContentRatingRefresher> logger,
        Func<TimeSpan, CancellationToken, Task> pause)
    {
        _repository = repository;
        _providers = providers;
        _settings = settings;
        _progress = progress;
        _logger = logger;
        _pause = pause;
    }

    public async Task<int> RateDueAlbumsAsync(CancellationToken cancellationToken)
    {
        var provider = await FirstEnabledProviderAsync();
        if (provider == null) return 0;

        var due = await _repository.GetAlbumsDueForContentRatingAsync(DateTime.UtcNow - RecheckAfter, MaxAlbumsPerRun);
        var rated = 0;

        for (var i = 0; i < due.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var album = due[i];

            // The count comes first so a narrow task row that truncates the
            // album name still shows how far through the run it is.
            _progress.Report($"Checking Clean / Explicit {i + 1}/{due.Count}: {album.ArtistName} - {album.AlbumTitle}");

            var tracks = (await _repository.GetAlbumTracksForUpdateAsync(album.AlbumId))
                .Where(t => t.ContentRating == null && !t.IsLocked(nameof(Track.ContentRating)))
                .ToList();

            var (albumRated, available) = await RateAlbumAsync(provider, album, tracks, cancellationToken);
            await _repository.SaveMusicChangesAsync(cancellationToken);
            rated += albumRated;

            // Stop rather than skip: a provider that is not answering is not
            // answering for anyone. What was not reached stays unasked and is
            // first in line next run.
            if (!available)
            {
                _logger.LogInformation("Stopping the music rating run: {Provider} is not answering.", provider.ProviderName);
                break;
            }
        }

        _progress.Report(null);

        if (rated > 0)
        {
            _logger.LogInformation("Rated {Rated} track(s) Clean or Explicit from {Provider}.", rated, provider.ProviderName);
        }

        return rated;
    }

    private async Task<(int Rated, bool Available)> RateAlbumAsync(
        IMusicContentRatingProvider provider, ContentRatingTarget album, List<Track> tracks, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderAlbumEdition>? editions = null;
        var rated = 0;

        foreach (var track in tracks)
        {
            var advisory = ProviderAdvisory.Unknown;

            if (!string.IsNullOrWhiteSpace(track.Isrc))
            {
                var exact = await provider.GetTrackAdvisoryByIsrcAsync(track.Isrc, cancellationToken);
                await _pause(PauseBetweenRequests, cancellationToken);
                if (exact.Outcome == ContentRatingLookupOutcome.Unavailable) return (rated, false);
                advisory = exact.Advisory;
            }

            if (advisory == ProviderAdvisory.Unknown)
            {
                if (editions == null)
                {
                    var lookup = await provider.GetAlbumEditionsAsync(album.ArtistName, album.AlbumTitle, cancellationToken);
                    await _pause(PauseBetweenRequests, cancellationToken);
                    if (lookup.Outcome == ContentRatingLookupOutcome.Unavailable) return (rated, false);
                    editions = MusicEditionMatcher.EditionsOf(album.ArtistName, album.AlbumTitle, lookup.Editions);
                }

                advisory = MusicEditionMatcher.Resolve(track.Title, track.DurationSeconds, editions);
            }

            if (Apply(track, advisory, provider.Id, DateTime.UtcNow)) rated++;
        }

        return (rated, true);
    }

    internal static bool Apply(Track track, ProviderAdvisory advisory, string providerId, DateTime checkedAt)
    {
        track.ContentRatingCheckedAt = checkedAt;

        var rating = advisory switch
        {
            ProviderAdvisory.Explicit => MusicContentRating.Explicit,
            ProviderAdvisory.Clean => MusicContentRating.Clean,
            _ => null
        };
        if (rating == null) return false;

        track.ContentRating = rating;
        track.ContentRatingProvider = providerId;
        return true;
    }

    private async Task<IMusicContentRatingProvider?> FirstEnabledProviderAsync()
    {
        foreach (var provider in _providers)
        {
            var enabled = await _settings.GetPluginSettingAsync(provider.Id, EnabledSettingKey);
            if (enabled != DisabledValue) return provider;
        }
        return null;
    }
}
