using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Playlists;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Media;

public class RecentlyPlayedManagerTests
{
    private readonly IMusicRepository _music = Substitute.For<IMusicRepository>();
    private readonly IMusicRecommendationRepository _mixes = Substitute.For<IMusicRecommendationRepository>();
    private readonly IAiPlaylistRepository _ai = Substitute.For<IAiPlaylistRepository>();
    private readonly IPlaylistRepository _playlists = Substitute.For<IPlaylistRepository>();
    private readonly ISmartPlaylistRepository _smart = Substitute.For<ISmartPlaylistRepository>();
    private readonly RecentlyPlayedManager _manager;

    private readonly Guid _profileId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
    private readonly List<RecentPlay> _plays = new();
    private readonly List<Track> _tracks = new();
    private readonly List<GeneratedMix> _mixRows = new();
    private readonly List<PlaylistSummaryVM> _playlistRows = new();

    public RecentlyPlayedManagerTests()
    {
        _manager = new RecentlyPlayedManager(_music, _mixes, _ai, _playlists, _smart);
        _music.GetRecentPlaysAsync(_profileId, Arg.Any<int>()).Returns(_ => _plays.OrderByDescending(p => p.PlayedAt).ToList());
        _music.GetTracksByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<MusicAccessFilter>())
            .Returns(call => _tracks.Where(t => call.Arg<IReadOnlyCollection<Guid>>().Contains(t.Id)).ToList());
        _music.GetLikedTrackIdsAsync(_profileId, Arg.Any<IEnumerable<Guid>>()).Returns(new HashSet<Guid>());
        _mixes.GetMixesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), _profileId)
            .Returns(call => _mixRows.Where(m => call.Arg<IReadOnlyCollection<Guid>>().Contains(m.Id)).ToList());
        _ai.GetTrackArtAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new List<AiTrackCandidate>());
        _playlists.GetVisibleSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), _profileId, Arg.Any<PlaylistAccessFilter>())
            .Returns(call => _playlistRows.Where(p => call.Arg<IReadOnlyCollection<Guid>>().Contains(p.Id)).ToList());
    }

    private Track Song(string title, string album)
    {
        var albumRow = new Album { Id = Guid.NewGuid(), Title = album, ArtworkUrl = $"/art/{album}.jpg" };
        var track = new Track { Id = Guid.NewGuid(), Title = title, Artist = "Artist", AlbumId = albumRow.Id, Album = albumRow };
        _tracks.Add(track);
        return track;
    }

    private GeneratedMix Mix(string name, GeneratedMixKind kind, params Guid[] trackIds)
    {
        var mix = new GeneratedMix { Id = Guid.NewGuid(), ProfileId = _profileId, Name = name, Kind = kind, TrackOrder = trackIds.ToList() };
        _mixRows.Add(mix);
        return mix;
    }

    private void Played(Guid trackId, int minutesAgo, MusicPlaySource source = MusicPlaySource.None, Guid? sourceId = null) =>
        _plays.Add(new RecentPlay(trackId, _now.AddMinutes(-minutesAgo), source, sourceId));

    private Task<List<RecentlyPlayedItemVM>> Row(int limit = 20) => _manager.GetAsync(_profileId, PlaylistAccessFilter.Unrestricted, limit);

    [Fact]
    public async Task A_playlist_is_one_tile_at_its_latest_play_and_songs_played_from_albums_are_songs()
    {
        var a = Song("Karma Police", "OK Computer");
        var b = Song("Airbag", "OK Computer");
        var c = Song("Creep", "Pablo Honey");
        var made = Mix("Late Night Drive", GeneratedMixKind.AiPlaylist, a.Id, b.Id);

        Played(a.Id, 5, MusicPlaySource.Mix, made.Id);
        Played(b.Id, 9, MusicPlaySource.Mix, made.Id);
        Played(c.Id, 20, MusicPlaySource.Album, c.AlbumId);
        Played(a.Id, 30, MusicPlaySource.Album, a.AlbumId);

        var row = await Row();

        row.Select(i => (i.Kind, i.Title)).Should().Equal(
            (RecentlyPlayedKind.Mix, "Late Night Drive"),
            (RecentlyPlayedKind.Track, "Creep"),
            (RecentlyPlayedKind.Track, "Karma Police"));
        row[0].Id.Should().Be(made.Id);
        row[0].Subtitle.Should().Be("Made for you by AI");
        row[0].PlayedAt.Should().Be(_now.AddMinutes(-5));
        row[0].TrackCount.Should().Be(2);
        row[1].Subtitle.Should().Be("Pablo Honey");
        row[1].ArtworkUrl.Should().Be("/art/Pablo Honey.jpg");
        row[1].Track!.AlbumTitle.Should().Be("Pablo Honey");
    }

    [Fact]
    public async Task A_song_heard_twice_on_its_own_is_one_tile()
    {
        var a = Song("Creep", "Pablo Honey");
        Played(a.Id, 1);
        Played(a.Id, 10);

        (await Row()).Should().ContainSingle().Which.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task Nothing_played_is_an_empty_row()
    {
        (await Row()).Should().BeEmpty();
        await _music.DidNotReceive().GetTracksByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<MusicAccessFilter>());
    }

    [Fact]
    public async Task Playlists_and_smart_playlists_are_tiles_too()
    {
        var a = Song("One", "Album");
        var b = Song("Two", "Album");
        var playlist = new PlaylistSummaryVM { Id = Guid.NewGuid(), Name = "Gym", ItemCount = 30, PosterUrls = new List<string> { "/1.jpg", "/2.jpg", "/3.jpg" } };
        _playlistRows.Add(playlist);
        var smart = new SmartPlaylist { Id = Guid.NewGuid(), ProfileId = _profileId, Name = "Unplayed Jazz", ArtworkUrl = "/jazz.jpg" };
        _smart.GetVisibleAsync(smart.Id, _profileId).Returns(smart);

        Played(a.Id, 1, MusicPlaySource.Playlist, playlist.Id);
        Played(b.Id, 2, MusicPlaySource.SmartPlaylist, smart.Id);

        var row = await Row();

        row.Select(i => (i.Kind, i.Title, i.Subtitle)).Should().Equal(
            (RecentlyPlayedKind.Playlist, "Gym", "Playlist"),
            (RecentlyPlayedKind.SmartPlaylist, "Unplayed Jazz", "Smart playlist"));
        row[0].ArtworkUrls.Should().Equal("/1.jpg", "/2.jpg", "/3.jpg");
        row[0].ArtworkUrl.Should().Be("/1.jpg");
        row[0].TrackCount.Should().Be(30);
        row[1].ArtworkUrl.Should().Be("/jazz.jpg");
    }

    [Fact]
    public async Task A_playlist_with_its_own_cover_shows_the_cover_and_no_mosaic()
    {
        var a = Song("One", "Album");
        var playlist = new PlaylistSummaryVM { Id = Guid.NewGuid(), Name = "Gym", ImageUrl = "/cover.jpg", PosterUrls = new List<string> { "/1.jpg", "/2.jpg" } };
        _playlistRows.Add(playlist);
        Played(a.Id, 1, MusicPlaySource.Playlist, playlist.Id);

        var tile = (await Row()).Single();

        tile.ArtworkUrl.Should().Be("/cover.jpg");
        tile.ArtworkUrls.Should().BeEmpty();
    }

    // A weekly AI playlist is replaced by next week's, and a playlist can be
    // deleted or unshared. Their tiles go, and older history fills the row.
    [Fact]
    public async Task Tiles_that_are_gone_are_left_out_and_older_history_fills_the_row()
    {
        var a = Song("One", "First");
        var b = Song("Two", "Second");
        var c = Song("Three", "Third");
        Played(a.Id, 1, MusicPlaySource.Mix, Guid.NewGuid());
        Played(a.Id, 2, MusicPlaySource.Playlist, Guid.NewGuid());
        Played(b.Id, 3);
        Played(c.Id, 4);

        var row = await Row(limit: 2);

        row.Select(i => i.Title).Should().Equal("Two", "Three");
    }

    [Fact]
    public async Task A_song_no_longer_allowed_is_left_out()
    {
        Played(Guid.NewGuid(), 1);
        var b = Song("Allowed", "Album");
        Played(b.Id, 2);

        (await Row()).Select(i => i.Title).Should().Equal("Allowed");
    }

    // Only a playlist or a mix is grouped; a play from an album is its song.
    [Fact]
    public async Task Album_plays_are_songs_and_a_source_without_an_id_is_ignored()
    {
        var a = Song("One", "Album");
        var b = Song("Two", "Album");
        var c = Song("Three", "Album");
        Played(a.Id, 1, MusicPlaySource.Album, a.AlbumId);
        Played(b.Id, 2, MusicPlaySource.Album, b.AlbumId);
        Played(c.Id, 3, MusicPlaySource.Mix);

        (await Row()).Select(i => (i.Kind, i.Title)).Should().Equal(
            (RecentlyPlayedKind.Track, "One"),
            (RecentlyPlayedKind.Track, "Two"),
            (RecentlyPlayedKind.Track, "Three"));
    }

    [Fact]
    public async Task The_row_is_cut_at_the_limit()
    {
        for (var i = 0; i < 30; i++) Played(Song($"Song {i}", "Album").Id, i);

        (await Row(limit: 5)).Select(i => i.Title).Should().Equal("Song 0", "Song 1", "Song 2", "Song 3", "Song 4");
        (await Row(limit: 500)).Should().HaveCount(30);
    }

    [Theory]
    [InlineData(GeneratedMixKind.AiPlaylist, "Made for you by AI")]
    [InlineData(GeneratedMixKind.Requested, "Your request")]
    [InlineData(GeneratedMixKind.Blend, "Blend")]
    [InlineData(GeneratedMixKind.Bridge, "Bridge")]
    [InlineData(GeneratedMixKind.DailyMix, "Made for You")]
    [InlineData(GeneratedMixKind.DiscoverMix, "Made for You")]
    [InlineData(GeneratedMixKind.ReleaseRadar, "Release Radar")]
    [InlineData(GeneratedMixKind.MoodMix, "Mood")]
    public void A_mix_tile_says_which_row_it_is_from(GeneratedMixKind kind, string caption) =>
        RecentlyPlayedManager.MixCaption(kind).Should().Be(caption);

    [Fact]
    public async Task An_ai_playlist_tile_has_the_same_mosaic_as_its_row_and_a_daily_mix_its_own_art()
    {
        var a = Song("One", "A");
        var b = Song("Two", "B");
        var request = Mix("Rainy Sunday", GeneratedMixKind.Requested, a.Id, b.Id);
        var daily = Mix("Daily Mix 1", GeneratedMixKind.DailyMix, a.Id, b.Id);
        daily.ArtworkUrl = "/daily.jpg";
        _ai.GetTrackArtAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new List<AiTrackCandidate>
        {
            new(a.Id, "artist a", "/a.jpg"),
            new(b.Id, "artist b", "/b.jpg"),
        });
        Played(a.Id, 1, MusicPlaySource.Mix, request.Id);
        Played(a.Id, 2, MusicPlaySource.Mix, daily.Id);

        var row = await Row();

        row[0].ArtworkUrls.Should().Equal("/a.jpg", "/b.jpg");
        row[0].MixKind.Should().Be(GeneratedMixKind.Requested);
        row[1].ArtworkUrl.Should().Be("/daily.jpg");
        row[1].ArtworkUrls.Should().BeEmpty();
    }
}
