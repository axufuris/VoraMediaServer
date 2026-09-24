using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// The refresher exists to keep calls down: fetch once, keep it, only ask again
// once it is stale. Every test here is about when it asks and when it does not,
// because that is the behaviour that would quietly regress into a call per
// artist per night.
public class MusicPopularityRefresherTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IListeningDataProvider _provider = Substitute.For<IListeningDataProvider>();

    private MusicPopularityRefresher Refresher(bool withProvider = true) => new(
        _repository,
        withProvider ? new[] { _provider } : Array.Empty<IListeningDataProvider>(),
        NullLogger<MusicPopularityRefresher>.Instance,
        (_, _) => Task.CompletedTask);

    private Artist GivenDue(string name, params (string Album, string[] Tracks)[] catalogue)
    {
        var artist = new Artist { Id = Guid.NewGuid(), Name = name };
        foreach (var (albumTitle, tracks) in catalogue)
        {
            var album = new Album { Id = Guid.NewGuid(), Title = albumTitle, ArtistId = artist.Id, Artist = artist };
            foreach (var t in tracks) album.Tracks.Add(new Track { Id = Guid.NewGuid(), Title = t, AlbumId = album.Id });
            artist.Albums.Add(album);
        }
        _repository.GetArtistCatalogForUpdateAsync(artist.Id).Returns(artist);
        return artist;
    }

    private void Due(params Artist[] artists) =>
        _repository.GetArtistsDueForPopularityRefreshAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(artists.Select(a => new PopularityRefreshTarget(a.Id, a.Name)).ToList());

    private void Answers(string artist, ArtistPopularity popularity) =>
        _provider.GetArtistPopularityAsync(artist, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(popularity);

    [Fact]
    public async Task Only_stale_artists_are_asked_about()
    {
        Due();

        await Refresher().RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        await _repository.Received(1).GetArtistsDueForPopularityRefreshAsync(
            Arg.Is<DateTime>(d => d < DateTime.UtcNow - TimeSpan.FromDays(29)), Arg.Any<int>());
        await _provider.DidNotReceive().GetArtistPopularityAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_listening_provider_means_no_work_at_all()
    {
        await Refresher(withProvider: false).RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        await _repository.DidNotReceive().GetArtistsDueForPopularityRefreshAsync(Arg.Any<DateTime>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Found_figures_are_stored_and_the_artist_is_stamped()
    {
        var artist = GivenDue("Luke Bryan", ("Crash My Party", new[] { "Play It Again", "Drink a Beer" }));
        Due(artist);
        Answers("Luke Bryan", new ArtistPopularity
        {
            Outcome = PopularityLookupOutcome.Found,
            Listeners = 2_140_000,
            Plays = 98_000_000,
            TopTracks = new[] { new NamedPopularity { Name = "Play It Again", Listeners = 700_000, Plays = 9_000_000 } },
            TopAlbums = new[] { new NamedPopularity { Name = "Crash My Party", Plays = 4_100_000 } },
        });

        await Refresher().RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        artist.GlobalListeners.Should().Be(2_140_000);
        artist.PopularityRefreshedAt.Should().NotBeNull();
        artist.Albums.Single().GlobalPlays.Should().Be(4_100_000);
        var tracks = artist.Albums.Single().Tracks.ToList();
        tracks.Single(t => t.Title == "Play It Again").GlobalListeners.Should().Be(700_000);
        tracks.Single(t => t.Title == "Drink a Beer").GlobalListeners.Should().BeNull("it is not among the artist's top tracks");
        await _repository.Received(1).SaveMusicChangesAsync(Arg.Any<CancellationToken>());
    }

    // The call-budget case. Without the stamp an artist Last.fm has never heard of
    // looks never-refreshed, and is asked about again every single night.
    [Fact]
    public async Task An_unknown_artist_is_stamped_so_it_is_not_asked_about_again_tomorrow()
    {
        var artist = GivenDue("Some Local Band");
        Due(artist);
        Answers("Some Local Band", ArtistPopularity.NotFound);

        await Refresher().RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        artist.PopularityRefreshedAt.Should().NotBeNull();
        artist.GlobalListeners.Should().BeNull();
        await _repository.Received(1).SaveMusicChangesAsync(Arg.Any<CancellationToken>());
    }

    // The opposite case: a blip must not cost a month of data, and must not burn
    // the rest of the batch on a service that is not answering.
    [Fact]
    public async Task An_unavailable_provider_stamps_nothing_and_stops_the_batch()
    {
        var first = GivenDue("First");
        var second = GivenDue("Second");
        Due(first, second);
        Answers("First", ArtistPopularity.Unavailable);

        var refreshed = await Refresher().RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        refreshed.Should().Be(0);
        first.PopularityRefreshedAt.Should().BeNull("it must be retried next run");
        await _provider.DidNotReceive().GetArtistPopularityAsync("Second", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveMusicChangesAsync(Arg.Any<CancellationToken>());
    }

    // A refresh is a snapshot. A track that fell out of the top fifty goes back
    // to null rather than keeping last month's number beside this month's.
    [Fact]
    public async Task A_track_that_dropped_out_of_the_top_list_is_cleared()
    {
        var artist = GivenDue("Luke Bryan", ("Crash My Party", new[] { "Old Hit" }));
        artist.Albums.Single().Tracks.Single().GlobalListeners = 500_000;
        Due(artist);
        Answers("Luke Bryan", new ArtistPopularity { Outcome = PopularityLookupOutcome.Found, Listeners = 1 });

        await Refresher().RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        artist.Albums.Single().Tracks.Single().GlobalListeners.Should().BeNull();
    }

    // Differences in case, whitespace and curly quotes are not different songs,
    // but a bracketed version is — the piano version must not inherit the hit.
    [Fact]
    public async Task Names_match_through_typography_but_not_through_versions()
    {
        var artist = GivenDue("Luke Bryan", ("Welcome To Farm Tour", new[] { "Kansas - (piano version)", "Can’t  Take It" }));
        Due(artist);
        Answers("Luke Bryan", new ArtistPopularity
        {
            Outcome = PopularityLookupOutcome.Found,
            TopTracks = new[]
            {
                new NamedPopularity { Name = "Kansas", Listeners = 80_000 },
                new NamedPopularity { Name = "can't take it", Listeners = 12_000 },
            },
        });

        await Refresher().RefreshDueArtistsAsync(TestContext.Current.CancellationToken);

        var tracks = artist.Albums.Single().Tracks.ToList();
        tracks.Single(t => t.Title.StartsWith("Kansas")).GlobalListeners.Should().BeNull();
        tracks.Single(t => t.Title.StartsWith("Can")).GlobalListeners.Should().Be(12_000);
    }
}
