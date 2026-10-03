using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class ForYouTrackPopularityTests
{
    private readonly IMusicRepository _music = Substitute.For<IMusicRepository>();
    private readonly IMusicRecommendationRepository _recommendations = Substitute.For<IMusicRecommendationRepository>();
    private readonly Guid _profileId = Guid.NewGuid();

    private static Track Song(string title, long? listeners, long? plays) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        GlobalListeners = listeners,
        GlobalPlays = plays,
        Album = new Album { Title = "Elephunk", ArtworkUrl = "/elephunk.jpg" }
    };

    [Fact]
    public async Task Recently_played_songs_carry_their_last_fm_figures()
    {
        _music.GetRecentlyPlayedTracksAsync(_profileId, Arg.Any<MusicAccessFilter>(), 12)
            .Returns(new List<Track> { Song("Hey Mama", 1_200_000, 9_800_000), Song("Unknown B-side", null, null) });
        _music.GetLikedTrackIdsAsync(_profileId, Arg.Any<IEnumerable<Guid>>()).Returns(new HashSet<Guid>());
        var manager = new MusicManager(
            _music,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            Array.Empty<IMusicArtworkProvider>(),
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions()),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);

        var tracks = await manager.GetRecentlyPlayedAsync(_profileId, MusicAccessFilter.Unrestricted, 12);

        tracks.Select(t => t.GlobalListeners).Should().Equal(1_200_000, null);
        tracks.Select(t => t.GlobalPlays).Should().Equal(9_800_000, null);
    }

    [Fact]
    public async Task Because_you_played_songs_carry_their_last_fm_figures()
    {
        var artistId = Guid.NewGuid();
        _recommendations.GetTopArtistsForProfileAsync(_profileId, Arg.Any<MusicAccessFilter>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new List<ArtistPlayScore> { new() { ArtistId = artistId, ArtistName = "Black Eyed Peas", Score = 10 } });
        _recommendations.GetGenresForArtistsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, List<string>>());
        _recommendations.GetTopTracksByArtistAsync(artistId, Arg.Any<MusicAccessFilter>(), _profileId, Arg.Any<int>(), Arg.Any<int>())
            .Returns(new List<Track> { Song("Pump It", 900_000, 7_000_000) });
        var manager = new MusicRecommendationManager(
            _recommendations,
            _music,
            Substitute.For<IUserRepository>(),
            Substitute.For<ISystemSettingsRepository>(),
            Substitute.For<IClientNotifier>(),
            Array.Empty<IListeningDataProvider>(),
            NullLogger<MusicRecommendationManager>.Instance);

        var rows = await manager.GetBecauseYouPlayedRowsAsync(_profileId, MusicAccessFilter.Unrestricted);

        var song = rows.Single().Tracks.Single();
        song.GlobalListeners.Should().Be(900_000);
        song.GlobalPlays.Should().Be(7_000_000);
    }
}
