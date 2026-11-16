using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Devices;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Streaming;

namespace Vora.Application.Tests.Streaming;

// A title comes off the watchlist when playback takes it past its finish line.
public class WatchlistOnFinishTests
{
    private const double Duration = 7200;

    private readonly IStreamRepository _repository = Substitute.For<IStreamRepository>();
    private readonly IWatchlistManager _watchlist = Substitute.For<IWatchlistManager>();
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _mediaId = Guid.NewGuid();

    private StreamManager Manager()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        return new StreamManager(
            _repository,
            new BestPathDecisionManager(settings),
            settings,
            Substitute.For<IStreamingTokenSigner>(),
            Substitute.For<IClientNotifier>(),
            Substitute.For<ITranscodeService>(),
            Options.Create(new StoragePathsOptions()),
            Substitute.For<IClientLocationResolver>(),
            Substitute.For<IMediaAnalyzerService>(),
            Substitute.For<IHardwareCapabilityService>(),
            _watchlist);
    }

    private StreamSession Session(double position, Guid? extraId = null)
    {
        var session = new StreamSession { MediaItemId = _mediaId, UserProfileId = _profileId, CurrentPosition = position, ExtraId = extraId };
        _repository.GetSessionAsync(session.Id).Returns(session);
        return session;
    }

    [Fact]
    public async Task The_ping_that_crosses_the_finish_line_checks_the_watchlist()
    {
        var session = Session(position: Duration * 0.85);

        await Manager().PingSessionAsync(session.Id, Duration * 0.91, Duration, isPaused: false);

        await _watchlist.Received(1).RemoveIfFinishedAsync(_profileId, _mediaId);
    }

    [Fact]
    public async Task A_ping_before_the_finish_line_leaves_the_watchlist_alone()
    {
        var session = Session(position: 600);

        await Manager().PingSessionAsync(session.Id, 610, Duration, isPaused: false);

        await _watchlist.DidNotReceive().RemoveIfFinishedAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task The_pings_after_it_have_nothing_more_to_check()
    {
        var session = Session(position: Duration * 0.92);

        await Manager().PingSessionAsync(session.Id, Duration * 0.93, Duration, isPaused: false);

        await _watchlist.DidNotReceive().RemoveIfFinishedAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task Watching_a_trailer_never_finishes_the_film()
    {
        var session = Session(position: Duration * 0.85, extraId: Guid.NewGuid());

        await Manager().PingSessionAsync(session.Id, Duration * 0.95, Duration, isPaused: false);

        await _watchlist.DidNotReceive().RemoveIfFinishedAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }
}
