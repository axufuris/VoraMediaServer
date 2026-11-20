using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Playlists;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Media;

public class MusicResumeManagerTests
{
    private readonly IMusicRepository _music = Substitute.For<IMusicRepository>();
    private readonly IMusicRecommendationRepository _mixes = Substitute.For<IMusicRecommendationRepository>();
    private readonly IAiPlaylistRepository _ai = Substitute.For<IAiPlaylistRepository>();
    private readonly IPlaylistRepository _playlists = Substitute.For<IPlaylistRepository>();
    private readonly ISmartPlaylistRepository _smart = Substitute.For<ISmartPlaylistRepository>();
    private readonly IMusicResumeRepository _points = Substitute.For<IMusicResumeRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly MusicResumeManager _manager;

    private readonly Guid _profileId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 19, 20, 0, 0, DateTimeKind.Utc);
    private readonly List<MusicResumePoint> _stored = new();
    private readonly List<Track> _tracks = new();
    private readonly List<PlaylistSummaryVM> _playlistRows = new();
    private readonly HashSet<Guid> _liked = new();

    public MusicResumeManagerTests()
    {
        _manager = new MusicResumeManager(
            _points,
            _music,
            new RecentlyPlayedManager(_music, _mixes, _ai, _playlists, _smart),
            _notifier,
            NullLogger<MusicResumeManager>.Instance);

        _points.GetAsync(_profileId, Arg.Any<int>()).Returns(_ => _stored.OrderByDescending(p => p.UpdatedAt).ToList());
        _points.GetAsync(_profileId, Arg.Any<MusicPlaySource>(), Arg.Any<Guid>())
            .Returns(call => _stored.FirstOrDefault(p => p.Source == call.ArgAt<MusicPlaySource>(1) && p.SourceId == call.ArgAt<Guid>(2)));
        _music.GetTracksByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<MusicAccessFilter>())
            .Returns(call => _tracks.Where(t => call.Arg<IReadOnlyCollection<Guid>>().Contains(t.Id)).ToList());
        _music.GetLikedTrackIdsAsync(_profileId, Arg.Any<IEnumerable<Guid>>())
            .Returns(call => call.Arg<IEnumerable<Guid>>().Where(_liked.Contains).ToHashSet());
        _mixes.GetMixesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), _profileId).Returns(new List<GeneratedMix>());
        _ai.GetTrackArtAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new List<AiTrackCandidate>());
        _playlists.GetVisibleSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), _profileId, Arg.Any<PlaylistAccessFilter>())
            .Returns(call => _playlistRows.Where(p => call.Arg<IReadOnlyCollection<Guid>>().Contains(p.Id)).ToList());
    }

    private Track Song(string title, string album = "Album")
    {
        var albumRow = new Album { Id = Guid.NewGuid(), Title = album, ArtworkUrl = $"/art/{album}.jpg" };
        var track = new Track { Id = Guid.NewGuid(), Title = title, Artist = "Artist", AlbumId = albumRow.Id, Album = albumRow };
        _tracks.Add(track);
        return track;
    }

    private MusicResumePoint Point(MusicPlaySource source, Guid sourceId, int index, int minutesAgo, params Track[] queue)
    {
        var point = new MusicResumePoint
        {
            ProfileId = _profileId,
            Source = source,
            SourceId = sourceId,
            TrackIds = queue.Select(t => t.Id).ToList(),
            TrackIndex = index,
            UpdatedAt = _now.AddMinutes(-minutesAgo)
        };
        _stored.Add(point);
        return point;
    }

    private PlaylistSummaryVM Playlist(string name)
    {
        var playlist = new PlaylistSummaryVM { Id = Guid.NewGuid(), Name = name, ItemCount = 12, ImageUrl = "/cover.jpg" };
        _playlistRows.Add(playlist);
        return playlist;
    }

    private static List<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Fact]
    public async Task A_queue_is_saved_under_its_source_and_the_profiles_devices_are_told()
    {
        var sourceId = Guid.NewGuid();
        var ids = Ids(3);

        var outcome = await _manager.SaveAsync(_profileId, MusicPlaySource.Playlist, sourceId, ids, 1);

        outcome.Should().Be(MusicResumeSaveOutcome.Saved);
        await _points.Received(1).SaveAsync(_profileId, MusicPlaySource.Playlist, sourceId, Arg.Is<IReadOnlyList<Guid>>(l => l.SequenceEqual(ids)), 1);
        await _notifier.Received(1).NotifyMusicResumeUpdatedAsync(_profileId);
    }

    [Fact]
    public async Task A_queue_with_no_source_is_kept_under_the_empty_source_whatever_id_comes_with_it()
    {
        var outcome = await _manager.SaveAsync(_profileId, MusicPlaySource.None, Guid.NewGuid(), Ids(2), 0);

        outcome.Should().Be(MusicResumeSaveOutcome.Saved);
        await _points.Received(1).SaveAsync(_profileId, MusicPlaySource.None, Guid.Empty, Arg.Any<IReadOnlyList<Guid>>(), 0);
    }

    [Theory]
    [InlineData(MusicPlaySource.Playlist)]
    [InlineData(MusicPlaySource.SmartPlaylist)]
    [InlineData(MusicPlaySource.Mix)]
    [InlineData(MusicPlaySource.Album)]
    public async Task A_source_other_than_none_needs_its_id(MusicPlaySource source)
    {
        (await _manager.SaveAsync(_profileId, source, null, Ids(2), 0)).Should().Be(MusicResumeSaveOutcome.Invalid);
        (await _manager.SaveAsync(_profileId, source, Guid.Empty, Ids(2), 0)).Should().Be(MusicResumeSaveOutcome.Invalid);

        await _points.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyMusicResumeUpdatedAsync(default);
    }

    [Fact]
    public async Task An_unknown_source_is_invalid()
    {
        (await _manager.SaveAsync(_profileId, (MusicPlaySource)99, Guid.NewGuid(), Ids(2), 0)).Should().Be(MusicResumeSaveOutcome.Invalid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(40)]
    public async Task An_index_outside_the_queue_is_invalid(int index)
    {
        (await _manager.SaveAsync(_profileId, MusicPlaySource.Mix, Guid.NewGuid(), Ids(3), index)).Should().Be(MusicResumeSaveOutcome.Invalid);

        await _points.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task More_songs_than_the_server_accepts_is_invalid()
    {
        var outcome = await _manager.SaveAsync(_profileId, MusicPlaySource.Mix, Guid.NewGuid(), Ids(MusicResumeLimits.MaxAccepted + 1), 0);

        outcome.Should().Be(MusicResumeSaveOutcome.Invalid);
    }

    [Fact]
    public async Task An_empty_queue_clears_the_sources_point_and_tells_the_devices()
    {
        var sourceId = Guid.NewGuid();

        var outcome = await _manager.SaveAsync(_profileId, MusicPlaySource.Playlist, sourceId, new List<Guid>(), 0);

        outcome.Should().Be(MusicResumeSaveOutcome.Cleared);
        await _points.Received(1).RemoveAsync(_profileId, MusicPlaySource.Playlist, sourceId);
        await _points.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default!, default);
        await _notifier.Received(1).NotifyMusicResumeUpdatedAsync(_profileId);
    }

    [Theory]
    [InlineData(300, 299, 300, 0, 299)]
    [InlineData(500, 10, 300, 0, 10)]
    [InlineData(500, 200, 300, 150, 50)]
    [InlineData(500, 499, 300, 200, 299)]
    [InlineData(1000, 0, 300, 0, 0)]
    public async Task A_long_queue_keeps_a_window_that_starts_before_the_current_song_and_never_runs_off_the_end(int count, int index, int expectedLength, int expectedStart, int expectedIndex)
    {
        var ids = Ids(count);
        IReadOnlyList<Guid>? saved = null;
        var savedIndex = -1;
        await _points.SaveAsync(_profileId, MusicPlaySource.Album, Arg.Any<Guid>(), Arg.Do<IReadOnlyList<Guid>>(l => saved = l), Arg.Do<int>(i => savedIndex = i));

        await _manager.SaveAsync(_profileId, MusicPlaySource.Album, Guid.NewGuid(), ids, index);

        saved.Should().NotBeNull();
        saved!.Count.Should().Be(expectedLength);
        saved.Should().Equal(ids.Skip(expectedStart).Take(expectedLength));
        savedIndex.Should().Be(expectedIndex);
        saved[savedIndex].Should().Be(ids[index]);
    }

    [Fact]
    public async Task Failing_to_tell_the_devices_does_not_fail_the_save()
    {
        _notifier.NotifyMusicResumeUpdatedAsync(_profileId).Returns(Task.FromException(new InvalidOperationException("hub down")));

        var outcome = await _manager.SaveAsync(_profileId, MusicPlaySource.Mix, Guid.NewGuid(), Ids(2), 0);

        outcome.Should().Be(MusicResumeSaveOutcome.Saved);
    }

    [Fact]
    public async Task A_playlist_point_is_a_playlist_tile_with_the_current_song_under_the_title()
    {
        var one = Song("One");
        var two = Song("Two");
        var playlist = Playlist("Road trip");
        Point(MusicPlaySource.Playlist, playlist.Id, 1, 5, one, two);

        var tile = (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Single();

        tile.Kind.Should().Be(RecentlyPlayedKind.Playlist);
        tile.Id.Should().Be(playlist.Id);
        tile.Title.Should().Be("Road trip");
        tile.Subtitle.Should().Be("Two");
        tile.TrackCount.Should().Be(12);
        tile.ArtworkUrl.Should().Be("/cover.jpg");
        tile.Source.Should().Be(MusicPlaySource.Playlist);
        tile.SourceId.Should().Be(playlist.Id);
        tile.TrackIndex.Should().Be(1);
        tile.PlayedAt.Should().Be(_now.AddMinutes(-5));
        tile.Track.Should().BeNull();
    }

    [Fact]
    public async Task An_album_point_is_a_song_tile_with_the_album_under_the_title_and_still_names_the_album_as_its_source()
    {
        var one = Song("One", "OK Computer");
        var two = Song("Two", "OK Computer");
        var albumId = one.AlbumId!.Value;
        Point(MusicPlaySource.Album, albumId, 1, 1, one, two);

        var tile = (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Single();

        tile.Kind.Should().Be(RecentlyPlayedKind.Track);
        tile.Id.Should().Be(two.Id);
        tile.Title.Should().Be("Two");
        tile.Subtitle.Should().Be("OK Computer");
        tile.Track!.Id.Should().Be(two.Id);
        tile.Source.Should().Be(MusicPlaySource.Album);
        tile.SourceId.Should().Be(albumId);
    }

    [Fact]
    public async Task A_queue_with_no_source_is_a_song_tile_with_no_source_id()
    {
        var one = Song("One");
        Point(MusicPlaySource.None, Guid.Empty, 0, 1, one);

        var tile = (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Single();

        tile.Kind.Should().Be(RecentlyPlayedKind.Track);
        tile.Source.Should().Be(MusicPlaySource.None);
        tile.SourceId.Should().BeNull();
    }

    [Fact]
    public async Task Tiles_are_newest_first_and_a_limit_cuts_the_row()
    {
        var a = Song("A");
        var b = Song("B");
        var c = Song("C");
        Point(MusicPlaySource.None, Guid.Empty, 0, 30, a);
        Point(MusicPlaySource.Album, Guid.NewGuid(), 0, 10, b);
        Point(MusicPlaySource.Album, Guid.NewGuid(), 0, 20, c);

        (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Select(t => t.Title).Should().Equal("B", "C", "A");
        (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 2)).Select(t => t.Title).Should().Equal("B", "C");
    }

    [Fact]
    public async Task A_playlist_that_was_deleted_or_is_no_longer_visible_is_left_out()
    {
        var one = Song("One");
        var other = Song("Other");
        Point(MusicPlaySource.Playlist, Guid.NewGuid(), 0, 1, one);
        Point(MusicPlaySource.None, Guid.Empty, 0, 2, other);

        (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Select(t => t.Title).Should().Equal("Other");
    }

    [Fact]
    public async Task A_point_whose_current_song_is_no_longer_playable_is_left_out()
    {
        var gone = Song("Gone");
        var kept = Song("Kept");
        var playlist = Playlist("Mix tape");
        Point(MusicPlaySource.Playlist, playlist.Id, 0, 1, gone, kept);
        _tracks.Remove(gone);

        (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_point_with_an_index_outside_its_queue_is_left_out_rather_than_failing_the_row()
    {
        var one = Song("One");
        var other = Song("Other");
        Point(MusicPlaySource.None, Guid.Empty, 5, 1, one);
        Point(MusicPlaySource.Album, Guid.NewGuid(), 0, 2, other);

        (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Select(t => t.Title).Should().Equal("Other");
    }

    [Fact]
    public async Task Nothing_saved_is_an_empty_row()
    {
        (await _manager.GetItemsAsync(_profileId, PlaylistAccessFilter.Unrestricted, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_queue_comes_back_in_saved_order_starting_at_the_current_song()
    {
        var one = Song("One");
        var two = Song("Two");
        var three = Song("Three");
        var sourceId = Guid.NewGuid();
        Point(MusicPlaySource.Mix, sourceId, 2, 1, two, one, three);
        _liked.Add(one.Id);

        var queue = await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.Mix, sourceId);

        queue.Should().NotBeNull();
        queue!.Source.Should().Be(MusicPlaySource.Mix);
        queue.SourceId.Should().Be(sourceId);
        queue.Tracks.Select(t => t.Title).Should().Equal("Two", "One", "Three");
        queue.Index.Should().Be(2);
        queue.Tracks.Select(t => t.IsLiked).Should().Equal(false, true, false);
    }

    [Fact]
    public async Task A_song_that_is_gone_is_skipped_and_the_start_moves_to_the_next_one_that_is_left()
    {
        var one = Song("One");
        var gone = Song("Gone");
        var three = Song("Three");
        var four = Song("Four");
        var sourceId = Guid.NewGuid();
        Point(MusicPlaySource.Playlist, sourceId, 1, 1, one, gone, three, four);
        _tracks.Remove(gone);

        var queue = await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.Playlist, sourceId);

        queue!.Tracks.Select(t => t.Title).Should().Equal("One", "Three", "Four");
        queue.Index.Should().Be(1);
    }

    [Fact]
    public async Task The_start_falls_back_to_the_first_song_when_nothing_at_or_after_the_saved_one_is_left()
    {
        var one = Song("One");
        var two = Song("Two");
        var gone = Song("Gone");
        var sourceId = Guid.NewGuid();
        Point(MusicPlaySource.Playlist, sourceId, 2, 1, one, two, gone);
        _tracks.Remove(gone);

        var queue = await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.Playlist, sourceId);

        queue!.Tracks.Select(t => t.Title).Should().Equal("One", "Two");
        queue.Index.Should().Be(0);
    }

    [Fact]
    public async Task A_queue_with_no_source_has_no_source_id()
    {
        var one = Song("One");
        Point(MusicPlaySource.None, Guid.Empty, 0, 1, one);

        var queue = await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.None, Guid.NewGuid());

        queue!.Source.Should().Be(MusicPlaySource.None);
        queue.SourceId.Should().BeNull();
        queue.Tracks.Should().HaveCount(1);
    }

    [Fact]
    public async Task There_is_no_queue_without_a_point_a_source_id_or_any_playable_song()
    {
        var one = Song("One");
        var sourceId = Guid.NewGuid();
        Point(MusicPlaySource.Playlist, sourceId, 0, 1, one);
        _tracks.Clear();

        (await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.Playlist, Guid.NewGuid())).Should().BeNull();
        (await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.Playlist, null)).Should().BeNull();
        (await _manager.GetQueueAsync(_profileId, new MusicAccessFilter(), MusicPlaySource.Playlist, sourceId)).Should().BeNull();
    }

    [Fact]
    public void The_window_is_the_whole_queue_when_it_fits()
    {
        var ids = Ids(40);

        var (window, index) = MusicResumeManager.Window(ids, 7);

        window.Should().Equal(ids);
        index.Should().Be(7);
    }
}
