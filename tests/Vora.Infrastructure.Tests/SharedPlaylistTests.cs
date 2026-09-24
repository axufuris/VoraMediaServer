using Microsoft.EntityFrameworkCore;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// A shared playlist is visible, read-only, to every profile on the server;
// anyone can play it or save a copy they own, and only its owner can change it.
// Its items were chosen by the OWNER, so the VIEWER's parental controls have to
// be applied to them — a child opening an adult's shared playlist sees what
// their own library would show them and nothing more.
public class SharedPlaylistTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private Track _explicit = null!;
    private Track _clean = null!;

    private static PlaylistAccessFilter Anyone => PlaylistAccessFilter.Unrestricted;
    private static PlaylistAccessFilter CleanOnly => new() { AllowedMusicRatings = new List<string> { "Clean" } };

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("shared-" + Guid.NewGuid().ToString("N"))
            .Options);

        db.Set<MediaLibrary>().Add(new MediaLibrary { Id = _libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } });
        db.Set<UserProfile>().AddRange(
            new UserProfile { Id = _owner, Name = "Andy" },
            new UserProfile { Id = _viewer, Name = "Sam" });

        var artist = new Artist { Id = Guid.NewGuid(), Name = "Eminem", LibraryId = _libraryId };
        var album = new Album { Id = Guid.NewGuid(), Title = "The Eminem Show", ArtistId = artist.Id, LibraryId = _libraryId, ArtworkUrl = "/art/show.jpg" };
        _explicit = new Track { Id = Guid.NewGuid(), Title = "Without Me", AlbumId = album.Id, TrackNumber = 1, LibraryId = _libraryId, ContentRating = "Explicit" };
        _clean = new Track { Id = Guid.NewGuid(), Title = "Without Me (Clean)", AlbumId = album.Id, TrackNumber = 2, LibraryId = _libraryId, ContentRating = "Clean" };
        db.AddRange(artist, album, _explicit, _clean);
        return db;
    }

    private Playlist AddPlaylist(VoraDbContext db, string name, bool shared, params Track[] tracks)
    {
        var playlist = new Playlist
        {
            ProfileId = _owner,
            Name = name,
            MediaType = PlaylistMediaType.Music,
            IsShared = shared,
            SharedAt = shared ? DateTime.UtcNow : null,
            Items = tracks.Select((t, i) => new PlaylistItem { MediaItemId = t.Id, Order = i + 1 }).ToList()
        };
        db.Playlists.Add(playlist);
        db.SaveChanges();
        return playlist;
    }

    [Fact]
    public async Task Someone_elses_unshared_playlist_cannot_be_read()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Private", shared: false, _clean);

        var details = await new PlaylistRepository(db).GetPlaylistDetailsAsync(playlist.Id, _viewer, Anyone);

        details.Should().BeNull();
    }

    [Fact]
    public async Task A_shared_playlist_is_readable_and_says_whose_it_is()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Road Trip", shared: true, _clean);

        var details = await new PlaylistRepository(db).GetPlaylistDetailsAsync(playlist.Id, _viewer, Anyone);

        details.Should().NotBeNull();
        details!.IsOwner.Should().BeFalse();
        details.OwnerName.Should().Be("Andy");
        details.Items.Should().ContainSingle();
    }

    // The path a viewer who had it open hits once its owner stops sharing it.
    [Fact]
    public async Task Unsharing_makes_it_unavailable_to_everyone_but_the_owner()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Road Trip", shared: true, _clean);
        var repository = new PlaylistRepository(db);

        (await repository.SetSharedAsync(playlist.Id, _owner, false)).Should().BeTrue();

        (await repository.GetPlaylistDetailsAsync(playlist.Id, _viewer, Anyone)).Should().BeNull();
        (await repository.GetPlaylistDetailsAsync(playlist.Id, _owner, Anyone)).Should().NotBeNull();
    }

    // Items, count and poster mosaic all follow the viewer's controls. A mosaic
    // built from every item would leak a restricted title's artwork.
    [Fact]
    public async Task A_viewers_parental_controls_apply_to_someone_elses_items()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Mixed", shared: true, _explicit, _clean);

        var details = await new PlaylistRepository(db).GetPlaylistDetailsAsync(playlist.Id, _viewer, CleanOnly);

        details!.Items.Select(i => i.Title).Should().Equal("Without Me (Clean)");
        details.ItemCount.Should().Be(1);
    }

    [Fact]
    public async Task The_shared_tab_lists_other_peoples_shared_playlists_only()
    {
        using var db = NewContext();
        AddPlaylist(db, "Shared", shared: true, _clean);
        AddPlaylist(db, "Not shared", shared: false, _clean);
        db.Playlists.Add(new Playlist
        {
            ProfileId = _viewer, Name = "The viewer's own", IsShared = true, SharedAt = DateTime.UtcNow,
            Items = new List<PlaylistItem> { new() { MediaItemId = _clean.Id, Order = 1 } }
        });
        db.SaveChanges();

        var shared = await new PlaylistRepository(db).GetSharedByOthersAsync(_viewer, Anyone);

        shared.Select(p => p.Name).Should().Equal("Shared");
        shared.Single().OwnerName.Should().Be("Andy");
    }

    // A child has no use for a shared playlist they can see nothing in, and
    // listing it would show them its title.
    [Fact]
    public async Task A_shared_playlist_with_nothing_the_viewer_may_see_is_not_listed()
    {
        using var db = NewContext();
        AddPlaylist(db, "Explicit Only", shared: true, _explicit);

        var shared = await new PlaylistRepository(db).GetSharedByOthersAsync(_viewer, CleanOnly);

        shared.Should().BeEmpty();
    }

    [Fact]
    public async Task A_copy_is_the_viewers_own_and_starts_unshared()
    {
        using var db = NewContext();
        var source = AddPlaylist(db, "Road Trip", shared: true, _explicit, _clean);

        var copyId = await new PlaylistRepository(db).CopyPlaylistAsync(source.Id, _viewer, Anyone);

        var copy = await db.Playlists.Include(p => p.Items).SingleAsync(p => p.Id == copyId, TestContext.Current.CancellationToken);
        copy.ProfileId.Should().Be(_viewer);
        copy.IsShared.Should().BeFalse("otherwise saving a shared playlist adds a duplicate to everyone's Shared tab");
        copy.Name.Should().Be("Road Trip");
        copy.Items.OrderBy(i => i.Order).Select(i => i.MediaItemId).Should().Equal(_explicit.Id, _clean.Id);
    }

    // Copying the hidden items would slip a restricted title into a restricted
    // profile's own playlist, where it would sit as if they had chosen it.
    [Fact]
    public async Task A_copy_holds_only_what_the_viewer_may_see()
    {
        using var db = NewContext();
        var source = AddPlaylist(db, "Mixed", shared: true, _explicit, _clean);

        var copyId = await new PlaylistRepository(db).CopyPlaylistAsync(source.Id, _viewer, CleanOnly);

        var copy = await db.Playlists.Include(p => p.Items).SingleAsync(p => p.Id == copyId, TestContext.Current.CancellationToken);
        copy.Items.Select(i => i.MediaItemId).Should().Equal(_clean.Id);
    }

    [Fact]
    public async Task Someone_elses_unshared_playlist_cannot_be_copied()
    {
        using var db = NewContext();
        var source = AddPlaylist(db, "Private", shared: false, _clean);

        var copyId = await new PlaylistRepository(db).CopyPlaylistAsync(source.Id, _viewer, Anyone);

        copyId.Should().BeNull();
    }

    [Fact]
    public async Task Only_the_owner_can_share_or_unshare()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Road Trip", shared: true, _clean);

        (await new PlaylistRepository(db).SetSharedAsync(playlist.Id, _viewer, false)).Should().BeFalse();

        (await db.Playlists.AsNoTracking().SingleAsync(p => p.Id == playlist.Id, TestContext.Current.CancellationToken)).IsShared.Should().BeTrue();
    }

    // Sharing opens reads, never writes. Adding an item gates on
    // IsPlaylistOwnerAsync and reordering loads through GetPlaylistWithItemsAsync,
    // and both must still refuse a viewer of a shared playlist. Delete and rename
    // match the owner in their WHERE clause too, but run as ExecuteDelete and
    // ExecuteUpdate, which the in-memory provider cannot execute.
    [Fact]
    public async Task A_viewer_of_a_shared_playlist_is_not_its_owner_for_writes()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Road Trip", shared: true, _clean);
        var repository = new PlaylistRepository(db);

        (await repository.IsPlaylistOwnerAsync(playlist.Id, _viewer)).Should().BeFalse();
        (await repository.GetPlaylistWithItemsAsync(playlist.Id, _viewer)).Should().BeNull();
        (await repository.IsPlaylistOwnerAsync(playlist.Id, _owner)).Should().BeTrue();
    }

    [Fact]
    public async Task Sharing_stamps_when_and_unsharing_clears_it()
    {
        using var db = NewContext();
        var playlist = AddPlaylist(db, "Road Trip", shared: false, _clean);
        var repository = new PlaylistRepository(db);

        await repository.SetSharedAsync(playlist.Id, _owner, true);
        (await db.Playlists.AsNoTracking().SingleAsync(p => p.Id == playlist.Id, TestContext.Current.CancellationToken)).SharedAt.Should().NotBeNull();

        await repository.SetSharedAsync(playlist.Id, _owner, false);
        (await db.Playlists.AsNoTracking().SingleAsync(p => p.Id == playlist.Id, TestContext.Current.CancellationToken)).SharedAt.Should().BeNull();
    }
}
