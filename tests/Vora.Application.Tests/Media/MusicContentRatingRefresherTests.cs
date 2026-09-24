using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// The job fills blanks and nothing else: a file's own tag, a locked rating and
// an answer it cannot trust are all left alone. The ISRC path is exact; the
// album path is strictest-wins.
public class MusicContentRatingRefresherTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IMusicContentRatingProvider _provider = Substitute.For<IMusicContentRatingProvider>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ITaskProgressReporter _progress = Substitute.For<ITaskProgressReporter>();

    public MusicContentRatingRefresherTests()
    {
        _provider.Id.Returns("deezer_content_ratings");
        _provider.ProviderName.Returns("Deezer");
        _provider.GetAlbumEditionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AlbumEditionsLookup.NotFound);
        _provider.GetTrackAdvisoryByIsrcAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrackAdvisoryLookup.NotFound);
    }

    private MusicContentRatingRefresher Refresher() => new(
        _repository, new[] { _provider }, _settings, _progress,
        NullLogger<MusicContentRatingRefresher>.Instance, (_, _) => Task.CompletedTask);

    private List<Track> GivenAlbum(string artist, string album, params Track[] tracks)
    {
        var target = new ContentRatingTarget(Guid.NewGuid(), artist, album);
        _repository.GetAlbumsDueForContentRatingAsync(Arg.Any<DateTime>(), Arg.Any<int>()).Returns(new List<ContentRatingTarget> { target });
        var list = tracks.ToList();
        _repository.GetAlbumTracksForUpdateAsync(target.AlbumId).Returns(list);
        return list;
    }

    private static Track Track(string title, int duration = 324, string? isrc = null, string? rating = null) =>
        new() { Id = Guid.NewGuid(), Title = title, DurationSeconds = duration, Isrc = isrc, ContentRating = rating };

    private void Editions(params ProviderAlbumEdition[] editions) =>
        _provider.GetAlbumEditionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AlbumEditionsLookup { Outcome = ContentRatingLookupOutcome.Found, Editions = editions });

    private static ProviderAlbumEdition Edition(ProviderAdvisory advisory) => new()
    {
        Title = "The Eminem Show",
        ArtistName = "Eminem",
        Tracks = new[] { new ProviderEditionTrack { Title = "White America", DurationSeconds = 324, Advisory = advisory } }
    };

    // The exact recording beats the album: this file is the clean edit even
    // though the explicit edition of its album exists.
    [Fact]
    public async Task An_isrc_answer_is_taken_over_the_album_editions()
    {
        var track = Track("White America", isrc: "USIR10211126");
        GivenAlbum("Eminem", "The Eminem Show", track);
        _provider.GetTrackAdvisoryByIsrcAsync("USIR10211126", Arg.Any<CancellationToken>())
            .Returns(new TrackAdvisoryLookup { Outcome = ContentRatingLookupOutcome.Found, Advisory = ProviderAdvisory.Clean });
        Editions(Edition(ProviderAdvisory.Explicit), Edition(ProviderAdvisory.Clean));

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        track.ContentRating.Should().Be("Clean");
        track.ContentRatingProvider.Should().Be("deezer_content_ratings");
        await _provider.DidNotReceive().GetAlbumEditionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_an_isrc_the_strictest_edition_wins()
    {
        var track = Track("White America");
        GivenAlbum("Eminem", "The Eminem Show", track);
        Editions(Edition(ProviderAdvisory.Clean), Edition(ProviderAdvisory.Explicit));

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        track.ContentRating.Should().Be("Explicit");
    }

    [Fact]
    public async Task An_isrc_the_provider_does_not_know_falls_back_to_the_album()
    {
        var track = Track("White America", isrc: "USIR99999999");
        GivenAlbum("Eminem", "The Eminem Show", track);
        Editions(Edition(ProviderAdvisory.Explicit));

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        track.ContentRating.Should().Be("Explicit");
    }

    [Fact]
    public async Task The_album_is_looked_up_once_however_many_tracks_need_it()
    {
        GivenAlbum("Eminem", "The Eminem Show", Track("White America"), Track("Business", 251), Track("Soldier", 226));
        Editions(Edition(ProviderAdvisory.Explicit));

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        await _provider.Received(1).GetAlbumEditionsAsync("Eminem", "The Eminem Show", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_tagged_or_locked_rating_is_never_asked_about_or_changed()
    {
        var tagged = Track("White America", isrc: "USIR10211052", rating: "Clean");
        var locked = Track("Business", 251, isrc: "USIR10211053");
        locked.LockField(nameof(Vora.Domain.Entities.Media.Track.ContentRating));
        GivenAlbum("Eminem", "The Eminem Show", tagged, locked);
        Editions(Edition(ProviderAdvisory.Explicit));

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        tagged.ContentRating.Should().Be("Clean");
        locked.ContentRating.Should().BeNull();
        await _provider.DidNotReceive().GetTrackAdvisoryByIsrcAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Stamped so it is not asked again every night, and left unrated rather
    // than guessed at.
    [Fact]
    public async Task A_track_no_one_knows_is_stamped_checked_and_left_unrated()
    {
        var track = Track("White America");
        GivenAlbum("Eminem", "The Eminem Show", track);

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        track.ContentRating.Should().BeNull();
        track.ContentRatingCheckedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task An_unavailable_provider_stops_the_run_and_stamps_nothing()
    {
        var first = Track("White America", isrc: "USIR10211052");
        var second = Track("Business", 251, isrc: "USIR10211053");
        GivenAlbum("Eminem", "The Eminem Show", first, second);
        _provider.GetTrackAdvisoryByIsrcAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TrackAdvisoryLookup.Unavailable);

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        first.ContentRatingCheckedAt.Should().BeNull();
        second.ContentRatingCheckedAt.Should().BeNull();
        await _provider.Received(1).GetTrackAdvisoryByIsrcAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_disabled_plugin_does_no_work()
    {
        _settings.GetPluginSettingAsync("deezer_content_ratings", "is_enabled").Returns("false");

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        await _repository.DidNotReceive().GetAlbumsDueForContentRatingAsync(Arg.Any<DateTime>(), Arg.Any<int>());
    }

    // The task row shows how far through the run it is and which album it is on.
    [Fact]
    public async Task Progress_names_each_album_with_its_place_in_the_run()
    {
        var first = new ContentRatingTarget(Guid.NewGuid(), "Eminem", "The Eminem Show");
        var second = new ContentRatingTarget(Guid.NewGuid(), "Luke Bryan", "Crash My Party");
        _repository.GetAlbumsDueForContentRatingAsync(Arg.Any<DateTime>(), Arg.Any<int>()).Returns(new List<ContentRatingTarget> { first, second });
        _repository.GetAlbumTracksForUpdateAsync(Arg.Any<Guid>()).Returns(new List<Track>());

        await Refresher().RateDueAlbumsAsync(TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _progress.Report("Checking Clean / Explicit 1/2: Eminem - The Eminem Show");
            _progress.Report("Checking Clean / Explicit 2/2: Luke Bryan - Crash My Party");
            _progress.Report(null);
        });
    }
}
