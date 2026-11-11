using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// The gate has to sit on the server. The web player is one of several clients
// that post plays — the TV and phone clients post to the same endpoint — and if
// each decided for itself what counts, one history table would hold four
// different definitions of a play and every row downstream would mean whatever
// the client that wrote it thought it meant.
public class RecordTrackPlayGateTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly MusicManager _manager;

    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _trackId = Guid.NewGuid();

    public RecordTrackPlayGateTests()
    {
        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Array.Empty<IMusicArtworkProvider>(),
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            _notifier,
            Options.Create(new StoragePathsOptions()),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);
    }

    private void GivenTrackOfLength(int? seconds) =>
        _repository.GetTrackByIdAsync(_trackId, Arg.Any<MusicAccessFilter>())
            .Returns(new Track { Id = _trackId, Title = "Liar", Artist = "Britney Spears", DurationSeconds = seconds });

    private Task<RecordPlayOutcome> Play(int secondsListened, bool completed = false) =>
        _manager.RecordTrackPlayAsync(_profileId, _trackId, secondsListened, completed);

    // The case that started this: thirty seconds of a two-minute song used to be
    // stored as a play worth exactly as much as hearing the whole thing.
    [Fact]
    public async Task Thirty_seconds_of_a_two_minute_song_is_not_recorded()
    {
        GivenTrackOfLength(120);

        var outcome = await Play(30);

        outcome.Should().Be(RecordPlayOutcome.BelowThreshold);
        await _repository.DidNotReceive().RecordPlayAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Half_of_a_two_minute_song_is_recorded()
    {
        GivenTrackOfLength(120);

        var outcome = await Play(60);

        outcome.Should().Be(RecordPlayOutcome.Recorded);
        await _repository.Received(1).RecordPlayAsync(_profileId, _trackId, 60, false);
    }

    [Fact]
    public async Task Four_minutes_of_a_long_track_is_recorded_without_reaching_halfway()
    {
        GivenTrackOfLength(1200);

        await Play(240);

        await _repository.Received(1).RecordPlayAsync(_profileId, _trackId, 240, false);
    }

    // A player that reports the track as finished is trusted over its own
    // position, which can lag or stop updating just before the end.
    [Fact]
    public async Task A_completed_track_is_always_recorded()
    {
        GivenTrackOfLength(120);

        await Play(5, completed: true);

        await _repository.Received(1).RecordPlayAsync(_profileId, _trackId, 5, true);
    }

    // The client applies the same rule first, so this is the case where a client
    // posts anyway — an older build, another platform, or something calling the
    // endpoint directly.
    [Fact]
    public async Task A_client_that_ignores_the_rule_does_not_get_its_play_stored()
    {
        GivenTrackOfLength(240);

        await Play(1);

        await _repository.DidNotReceive().RecordPlayAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task A_track_whose_length_was_never_scanned_falls_back_to_thirty_seconds()
    {
        GivenTrackOfLength(null);

        await Play(29);
        await _repository.DidNotReceive().RecordPlayAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<bool>());

        await Play(30);
        await _repository.Received(1).RecordPlayAsync(_profileId, _trackId, 30, false);
    }

    // An unknown track cannot be measured, and silently dropping the play would
    // be indistinguishable from the gate rejecting it.
    [Fact]
    public async Task A_track_that_no_longer_exists_is_not_recorded()
    {
        _repository.GetTrackByIdAsync(_trackId, Arg.Any<MusicAccessFilter>()).Returns((Track?)null);

        var outcome = await Play(30);

        // Distinct from BelowThreshold on purpose: a client told only "not
        // recorded" would go looking in its own threshold logic for a problem
        // that is actually a bad track id.
        outcome.Should().Be(RecordPlayOutcome.UnknownTrack);
        await _repository.DidNotReceive().RecordPlayAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    // Recently Played groups a playlist's or a mix's plays into one tile, so
    // what the client says the song was played from has to reach the row.
    [Fact]
    public async Task What_the_song_was_played_from_is_recorded_with_the_play()
    {
        GivenTrackOfLength(200);
        var mixId = Guid.NewGuid();

        await _manager.RecordTrackPlayAsync(_profileId, _trackId, 200, true, MusicPlaySource.Mix, mixId);

        await _repository.Received(1).RecordPlayAsync(_profileId, _trackId, 200, true, MusicPlaySource.Mix, mixId);
    }

    // The profile's devices reload Recently Played when told, so a playlist
    // being played shows up on the page without leaving it.
    [Fact]
    public async Task A_recorded_play_tells_the_profiles_devices()
    {
        GivenTrackOfLength(120);

        await Play(60);

        await _notifier.Received(1).NotifyMusicHistoryUpdatedAsync(_profileId);
    }

    [Fact]
    public async Task A_play_that_is_not_recorded_tells_no_one()
    {
        GivenTrackOfLength(120);
        await Play(30);

        _repository.GetTrackByIdAsync(_trackId, Arg.Any<MusicAccessFilter>()).Returns((Track?)null);
        await Play(120, completed: true);

        await _notifier.DidNotReceive().NotifyMusicHistoryUpdatedAsync(Arg.Any<Guid>());
    }

    // The play is stored; a hub that fails must not turn that into an error.
    [Fact]
    public async Task A_play_is_still_recorded_when_the_devices_cannot_be_told()
    {
        GivenTrackOfLength(120);
        _notifier.NotifyMusicHistoryUpdatedAsync(_profileId).Returns(Task.FromException(new InvalidOperationException("hub down")));

        var outcome = await Play(60);

        outcome.Should().Be(RecordPlayOutcome.Recorded);
    }
}
