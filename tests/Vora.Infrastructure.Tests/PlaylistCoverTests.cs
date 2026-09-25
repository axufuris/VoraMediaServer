using Microsoft.EntityFrameworkCore;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Playlists;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// A playlist's cover: the owner's upload, or a mosaic of the items' artwork.
// Only the owner can change it, and a file is only removed once no playlist
// points at it - a copy shares its source's cover.
public class PlaylistCoverTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();

    private sealed class FakeImageStore : IPlaylistImageStore
    {
        private int _saved;
        public List<string> Deleted { get; } = new();

        public Task<string?> SaveAsync(Guid playlistId, byte[] bytes) =>
            Task.FromResult<string?>(bytes.Length == 0 ? null : $"/api/artwork/custom/playlist_{++_saved}.png");

        public void Delete(string? imageUrl)
        {
            if (imageUrl != null) Deleted.Add(imageUrl);
        }
    }

    private readonly FakeImageStore _store = new();
    private static readonly byte[] Image = { 1, 2, 3 };

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("covers-" + Guid.NewGuid().ToString("N"))
            .Options);
        db.Set<UserProfile>().AddRange(new UserProfile { Id = _owner, Name = "Andy" }, new UserProfile { Id = _viewer, Name = "Sam" });
        db.SaveChanges();
        return db;
    }

    private Guid AddPlaylist(VoraDbContext db, bool shared = false)
    {
        var playlist = new Playlist { ProfileId = _owner, Name = "Road Trip", MediaType = PlaylistMediaType.Music, IsShared = shared };
        db.Playlists.Add(playlist);
        db.SaveChanges();
        return playlist.Id;
    }

    private PlaylistManager Manager(VoraDbContext db) => new(new PlaylistRepository(db), _store);

    [Fact]
    public async Task The_owner_sets_a_cover_and_every_list_shows_it()
    {
        using var db = NewContext();
        var id = AddPlaylist(db);

        var url = await Manager(db).SetImageAsync(id, _owner, Image);

        url.Should().NotBeNull();
        var summary = (await new PlaylistRepository(db).GetPlaylistsAsync(_owner, PlaylistAccessFilter.Unrestricted)).Single();
        summary.ImageUrl.Should().Be(url);
    }

    [Fact]
    public async Task Someone_else_cannot_change_a_shared_playlists_cover()
    {
        using var db = NewContext();
        var id = AddPlaylist(db, shared: true);

        (await Manager(db).SetImageAsync(id, _viewer, Image)).Should().BeNull();
        (await Manager(db).RemoveImageAsync(id, _viewer)).Should().BeFalse();

        db.Playlists.Single().ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Replacing_a_cover_removes_the_old_file()
    {
        using var db = NewContext();
        var id = AddPlaylist(db);
        var manager = Manager(db);
        var first = await manager.SetImageAsync(id, _owner, Image);

        var second = await manager.SetImageAsync(id, _owner, Image);

        db.Playlists.Single().ImageUrl.Should().Be(second);
        _store.Deleted.Should().Equal(first);
    }

    [Fact]
    public async Task Removing_the_cover_goes_back_to_the_mosaic_and_removes_the_file()
    {
        using var db = NewContext();
        var id = AddPlaylist(db);
        var manager = Manager(db);
        var url = await manager.SetImageAsync(id, _owner, Image);

        (await manager.RemoveImageAsync(id, _owner)).Should().BeTrue();

        db.Playlists.Single().ImageUrl.Should().BeNull();
        _store.Deleted.Should().Equal(url);
    }

    // The copy keeps the cover it was saved with, so deleting the original must
    // not pull the file out from under it.
    [Fact]
    public async Task A_copy_keeps_the_cover_after_the_original_is_deleted()
    {
        using var db = NewContext();
        var id = AddPlaylist(db, shared: true);
        var manager = Manager(db);
        var url = await manager.SetImageAsync(id, _owner, Image);
        var copyId = await manager.CopyPlaylistAsync(id, _viewer, PlaylistAccessFilter.Unrestricted);

        await manager.DeletePlaylistAsync(id, _owner);

        db.Playlists.Single(p => p.Id == copyId).ImageUrl.Should().Be(url);
        _store.Deleted.Should().BeEmpty();

        await manager.DeletePlaylistAsync(copyId!.Value, _viewer);
        _store.Deleted.Should().Equal(url);
    }

    // Four songs from one album used to make a "mosaic" of one cover four times.
    [Fact]
    public void The_mosaic_is_four_different_images_in_playlist_order()
    {
        var summary = new PlaylistSummaryVM { PosterUrls = new List<string> { "a", "a", "a", "b", "c", "b", "d", "e" } };

        summary.PosterUrls.Should().Equal("a", "b", "c", "d");
    }
}
