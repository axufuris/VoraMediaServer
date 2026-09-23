using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
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
    private readonly MusicManager _manager;

    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _trackId = Guid.NewGuid();

    public RecordTrackPlayGateTests()
    {
        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            Array.Empty<IMusicArtworkProvider>(),
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions()),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);
    }

    private void GivenTrackOfLength(int? seconds) =>
        _repository.GetTrackByIdAsync(_trackId, Arg.Any<MusicAccessFilter>())
            .Returns(new Track { Id = _trackId, Title = "Liar", Artist = "Britney Spears", DurationSeconds = seconds });

    private Task Play(int secondsListened, bool completed = false) =>
        _manager.RecordTrackPlayAsync(_profileId, _trackId, secondsListened, completed);

    // The case that started this: thirty seconds of a two-minute song used to be
    // stored as a play worth exactly as much as hearing the whole thing.
    [Fact]
    public async Task Thirty_seconds_of_a_two_minute_song_is_not_recorded()
    {
        GivenTrackOfLength(120);

        await Play(30);

        await _repository.DidNotReceive().RecordPlayAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Half_of_a_two_minute_song_is_recorded()
    {
        GivenTrackOfLength(120);

        await Play(60);

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

        await Play(30);

        await _repository.DidNotReceive().RecordPlayAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<bool>());
    }
}
