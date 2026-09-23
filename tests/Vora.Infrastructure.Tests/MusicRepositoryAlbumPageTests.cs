using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MusicRepositoryAlbumPageTests
{
    private static readonly DateTime June = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("album-page-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class Shelf
    {
        public required VoraDbContext Db { get; init; }
        public required MediaLibrary Library { get; init; }
        public required Artist Artist { get; init; }

        public Album Album(string title, int daysAfterJune, string? sortTitle = null, string? rating = null, bool withTrack = true)
        {
            var album = new Album { Id = Guid.NewGuid(), Title = title, SortTitle = sortTitle, ArtistId = Artist.Id, LibraryId = Library.Id, AddedAt = June.AddDays(daysAfterJune) };
            Db.Add(album);
            if (withTrack)
            {
                Db.Add(new Track { Id = Guid.NewGuid(), Title = title + " track", AlbumId = album.Id, LibraryId = Library.Id, ContentRating = rating });
            }
            return album;
        }
    }

    private static Shelf NewShelf(VoraDbContext db, string name = "Music")
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = name, Type = LibraryType.Music, FolderPaths = new List<string> { "/music/" + name } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = name + " Artist", LibraryId = library.Id };
        db.AddRange(library, artist);
        return new Shelf { Db = db, Library = library, Artist = artist };
    }

    // Every call that exists today passes no term. The parameter is optional, so
    // this pins that the two spellings are the same call and cannot drift.
    [Fact]
    public async Task An_absent_term_and_an_explicit_null_are_the_same_query()
    {
        await using var db = NewContext();
        var shelf = NewShelf(db);
        shelf.Album("Old", 1);
        shelf.Album("Newest", 30);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new MusicRepository(db);
        var absent = await repository.GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 0, 10);
        var explicitNull = await repository.GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 0, 10, null);

        explicitNull.Total.Should().Be(absent.Total);
        explicitNull.Albums.Select(a => a.Title).Should().Equal(absent.Albums.Select(a => a.Title));
    }

    [Fact]
    public async Task Recently_added_lists_the_newest_albums_first_with_their_artist()
    {
        await using var db = NewContext();
        var shelf = NewShelf(db);
        shelf.Album("Old", 1);
        shelf.Album("Newest", 30);
        shelf.Album("Middle", 10);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (albums, total) = await new MusicRepository(db).GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 0, 10);

        total.Should().Be(3);
        albums.Select(a => a.Title).Should().Equal("Newest", "Middle", "Old");
        albums.Should().OnlyContain(a => a.Artist.Name == "Music Artist");
    }

    [Fact]
    public async Task Alphabetical_orders_by_sort_title_when_one_is_set()
    {
        await using var db = NewContext();
        var shelf = NewShelf(db);
        shelf.Album("The Wall", 1, sortTitle: "Wall");
        shelf.Album("Abbey Road", 2);
        shelf.Album("Rumours", 3);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (albums, _) = await new MusicRepository(db).GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.Alphabetical, 0, 10);

        albums.Select(a => a.Title).Should().Equal("Abbey Road", "Rumours", "The Wall");
    }

    [Fact]
    public async Task Pages_walk_the_whole_list_without_repeats_and_keep_the_full_total()
    {
        await using var db = NewContext();
        var shelf = NewShelf(db);
        for (var i = 0; i < 5; i++) shelf.Album($"Album {i}", 0);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new MusicRepository(db);

        var (first, firstTotal) = await repo.GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 0, 2);
        var (second, _) = await repo.GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 2, 2);
        var (last, lastTotal) = await repo.GetAlbumsPageAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 4, 2);

        firstTotal.Should().Be(5);
        lastTotal.Should().Be(5);
        last.Should().HaveCount(1);
        first.Concat(second).Concat(last).Select(a => a.Id).Should().OnlyHaveUniqueItems().And.HaveCount(5);
    }

    [Fact]
    public async Task Only_albums_in_libraries_the_profile_can_see_are_listed()
    {
        await using var db = NewContext();
        var allowed = NewShelf(db, "Allowed");
        var hidden = NewShelf(db, "Hidden");
        allowed.Album("Visible", 1);
        hidden.Album("Secret", 2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var access = new MusicAccessFilter { HasAllLibraryAccess = false, AllowedLibraryIds = new List<Guid> { allowed.Library.Id } };

        var (albums, total) = await new MusicRepository(db).GetAlbumsPageAsync(null, access, AlbumSortOrder.RecentlyAdded, 0, 10);

        total.Should().Be(1);
        albums.Select(a => a.Title).Should().Equal("Visible");
    }

    [Fact]
    public async Task A_library_id_narrows_the_list_to_that_library()
    {
        await using var db = NewContext();
        var rock = NewShelf(db, "Rock");
        var jazz = NewShelf(db, "Jazz");
        rock.Album("Rock Album", 1);
        jazz.Album("Jazz Album", 2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (albums, _) = await new MusicRepository(db).GetAlbumsPageAsync(jazz.Library.Id, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, 0, 10);

        albums.Select(a => a.Title).Should().Equal("Jazz Album");
    }

    [Fact]
    public async Task Albums_with_no_track_the_profile_may_play_are_left_out()
    {
        await using var db = NewContext();
        var shelf = NewShelf(db);
        shelf.Album("Clean", 1, rating: "Clean");
        shelf.Album("Explicit", 2, rating: "Explicit");
        shelf.Album("Empty", 3, withTrack: false);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var access = new MusicAccessFilter { HasAllRatings = false, AllowedRatings = new List<string> { "Clean" } };

        var (albums, total) = await new MusicRepository(db).GetAlbumsPageAsync(null, access, AlbumSortOrder.RecentlyAdded, 0, 10);

        total.Should().Be(1);
        albums.Select(a => a.Title).Should().Equal("Clean");
    }
}
