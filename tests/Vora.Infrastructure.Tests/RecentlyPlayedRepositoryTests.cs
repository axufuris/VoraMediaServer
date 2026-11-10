using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// The reads and writes behind For You's Recently Played row.
public class RecentlyPlayedRepositoryTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _otherLibraryId = Guid.NewGuid();
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _someoneElse = Guid.NewGuid();
    private Track _song = null!;
    private Track _hiddenSong = null!;

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("recently-played-" + Guid.NewGuid().ToString("N"))
            .Options);
        db.Set<MediaLibrary>().AddRange(
            new MediaLibrary { Id = _libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } },
            new MediaLibrary { Id = _otherLibraryId, Name = "Kids", Type = LibraryType.Music, FolderPaths = new List<string> { "/kids" } });
        db.Set<UserProfile>().AddRange(new UserProfile { Id = _me, Name = "Andy" }, new UserProfile { Id = _someoneElse, Name = "Sam" });
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Radiohead", LibraryId = _libraryId };
        var album = new Album { Id = Guid.NewGuid(), Title = "OK Computer", ArtistId = artist.Id, LibraryId = _libraryId, ArtworkUrl = "/ok.jpg" };
        _song = new Track { Id = Guid.NewGuid(), Title = "Karma Police", AlbumId = album.Id, LibraryId = _libraryId };
        _hiddenSong = new Track { Id = Guid.NewGuid(), Title = "Elsewhere", LibraryId = _otherLibraryId };
        db.AddRange(artist, album, _song, _hiddenSong);
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task A_play_keeps_what_it_was_played_from()
    {
        await using var db = NewContext();
        var mixId = Guid.NewGuid();

        await new MusicRepository(db).RecordPlayAsync(_me, _song.Id, 200, true, MusicPlaySource.Mix, mixId);

        var play = await db.TrackPlayHistory.SingleAsync(TestContext.Current.CancellationToken);
        play.Source.Should().Be(MusicPlaySource.Mix);
        play.SourceId.Should().Be(mixId);
    }

    [Fact]
    public async Task A_source_without_its_id_is_stored_as_none()
    {
        await using var db = NewContext();
        var repository = new MusicRepository(db);

        await repository.RecordPlayAsync(_me, _song.Id, 200, true, MusicPlaySource.Playlist, null);
        await repository.RecordPlayAsync(_me, _song.Id, 200, true, MusicPlaySource.Playlist, Guid.Empty);
        await repository.RecordPlayAsync(_me, _song.Id, 200, true);

        var plays = await db.TrackPlayHistory.ToListAsync(TestContext.Current.CancellationToken);
        plays.Should().HaveCount(3).And.OnlyContain(p => p.Source == MusicPlaySource.None && p.SourceId == null);
    }

    [Fact]
    public async Task Recent_plays_are_the_profiles_own_newest_first()
    {
        await using var db = NewContext();
        var playlistId = Guid.NewGuid();
        var at = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
        db.TrackPlayHistory.AddRange(
            new TrackPlayHistory { ProfileId = _me, TrackId = _song.Id, PlayedAt = at.AddHours(-2) },
            new TrackPlayHistory { ProfileId = _me, TrackId = _song.Id, PlayedAt = at, Source = MusicPlaySource.Playlist, SourceId = playlistId },
            new TrackPlayHistory { ProfileId = _someoneElse, TrackId = _song.Id, PlayedAt = at.AddHours(1) },
            new TrackPlayHistory { ProfileId = _me, TrackId = _song.Id, PlayedAt = at.AddHours(-5) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var plays = await new MusicRepository(db).GetRecentPlaysAsync(_me, 2);

        plays.Should().Equal(
            new RecentPlay(_song.Id, at, MusicPlaySource.Playlist, playlistId),
            new RecentPlay(_song.Id, at.AddHours(-2), MusicPlaySource.None, null));
    }

    [Fact]
    public async Task Tracks_by_id_come_with_their_album_and_only_from_allowed_libraries()
    {
        await using var db = NewContext();
        var access = new MusicAccessFilter { HasAllLibraryAccess = false, AllowedLibraryIds = new List<Guid> { _libraryId } };

        var tracks = await new MusicRepository(db).GetTracksByIdsAsync(new[] { _song.Id, _hiddenSong.Id }, access);

        tracks.Should().ContainSingle().Which.Album!.Title.Should().Be("OK Computer");
    }

    [Fact]
    public async Task Mixes_by_id_are_only_the_profiles_own()
    {
        await using var db = NewContext();
        var mine = new GeneratedMix { ProfileId = _me, Name = "Daily Mix 1" };
        var theirs = new GeneratedMix { ProfileId = _someoneElse, Name = "Daily Mix 1" };
        db.GeneratedMixes.AddRange(mine, theirs);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var mixes = await new MusicRecommendationRepository(db).GetMixesByIdsAsync(new[] { mine.Id, theirs.Id }, _me);

        mixes.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task Playlists_by_id_are_the_viewers_own_and_shared_ones()
    {
        await using var db = NewContext();
        var mine = new Playlist { ProfileId = _me, Name = "Gym", MediaType = PlaylistMediaType.Music };
        var shared = new Playlist { ProfileId = _someoneElse, Name = "Road Trip", MediaType = PlaylistMediaType.Music, IsShared = true };
        var privateToThem = new Playlist { ProfileId = _someoneElse, Name = "Secret", MediaType = PlaylistMediaType.Music };
        db.Playlists.AddRange(mine, shared, privateToThem);
        db.Add(new PlaylistItem { PlaylistId = mine.Id, MediaItemId = _song.Id, Order = 1 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await new PlaylistRepository(db).GetVisibleSummariesAsync(new[] { mine.Id, shared.Id, privateToThem.Id }, _me, PlaylistAccessFilter.Unrestricted);

        found.Select(p => p.Name).Should().BeEquivalentTo("Gym", "Road Trip");
        found.Single(p => p.Id == mine.Id).ItemCount.Should().Be(1);
    }
}
