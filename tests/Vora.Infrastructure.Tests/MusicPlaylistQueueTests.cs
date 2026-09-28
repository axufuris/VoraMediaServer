using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Playlists;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class MusicPlaylistQueueTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _me = Guid.NewGuid();
    private Track _songA = null!;
    private Track _songB = null!;
    private Movie _movie = null!;

    private sealed class NoImages : IPlaylistImageStore
    {
        public Task<string?> SaveAsync(Guid playlistId, byte[] bytes) => Task.FromResult<string?>(null);
        public void Delete(string? imageUrl) { }
    }

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("music-queue-" + Guid.NewGuid().ToString("N"))
            .Options);
        db.Set<MediaLibrary>().Add(new MediaLibrary { Id = _libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } });
        db.Set<UserProfile>().Add(new UserProfile { Id = _me, Name = "Andy" });
        var artist = new Artist { Id = Guid.NewGuid(), Name = "blink-182", LibraryId = _libraryId };
        var album = new Album { Id = Guid.NewGuid(), Title = "Enema of the State", ArtistId = artist.Id, LibraryId = _libraryId };
        _songA = new Track { Id = Guid.NewGuid(), Title = "Adam's Song", AlbumId = album.Id, TrackNumber = 1, LibraryId = _libraryId };
        _songB = new Track { Id = Guid.NewGuid(), Title = "All the Small Things", AlbumId = album.Id, TrackNumber = 2, LibraryId = _libraryId };
        _movie = new Movie { Id = Guid.NewGuid(), Title = "Twisters", LibraryId = _libraryId };
        db.AddRange(artist, album, _songA, _songB, _movie);
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Songs_in_a_playlist_carry_no_played_or_resume_state_but_films_keep_theirs()
    {
        await using var db = NewContext();
        var playlist = new Playlist { Id = Guid.NewGuid(), ProfileId = _me, Name = "Mixed", MediaType = PlaylistMediaType.Mixed };
        db.Playlists.Add(playlist);
        db.AddRange(
            new PlaylistItem { PlaylistId = playlist.Id, MediaItemId = _songA.Id, Order = 1 },
            new PlaylistItem { PlaylistId = playlist.Id, MediaItemId = _movie.Id, Order = 2 });
        db.UserMediaStates.AddRange(
            new UserMediaState { ProfileId = _me, MediaItemId = _songA.Id, IsPlayed = true, ResumePositionSeconds = 90 },
            new UserMediaState { ProfileId = _me, MediaItemId = _movie.Id, IsPlayed = true, ResumePositionSeconds = 600 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var details = await new PlaylistRepository(db).GetPlaylistDetailsAsync(playlist.Id, _me, PlaylistAccessFilter.Unrestricted);

        var song = details!.Items.Single(i => i.MediaItemId == _songA.Id);
        song.IsPlayed.Should().BeFalse();
        song.ResumePositionSeconds.Should().Be(0);
        var film = details.Items.Single(i => i.MediaItemId == _movie.Id);
        film.IsPlayed.Should().BeTrue();
        film.ResumePositionSeconds.Should().Be(600);
    }

    [Fact]
    public async Task A_queue_saved_as_a_playlist_keeps_its_order_and_drops_unknown_songs()
    {
        await using var db = NewContext();
        var manager = new PlaylistManager(new PlaylistRepository(db), new NoImages());

        var id = await manager.CreatePlaylistWithItemsAsync(_me, "Saturday queue", null, PlaylistMediaType.Music, new[] { _songB.Id, Guid.NewGuid(), _songA.Id });

        var items = await db.PlaylistItems.Where(i => i.PlaylistId == id).OrderBy(i => i.Order).Select(i => i.MediaItemId).ToListAsync(TestContext.Current.CancellationToken);
        items.Should().Equal(_songB.Id, _songA.Id);
    }
}
