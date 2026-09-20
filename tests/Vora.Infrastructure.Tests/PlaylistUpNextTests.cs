using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class PlaylistUpNextTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("playlist-up-next-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class Fixture
    {
        public required Guid PlaylistId { get; init; }
        public required List<Movie> Movies { get; init; }
    }

    private static async Task<Fixture> SeedAsync(VoraDbContext db, int itemCount = 3)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        var playlist = new Playlist { Id = Guid.NewGuid(), Name = "Saturday night", ProfileId = Guid.NewGuid(), MediaType = PlaylistMediaType.Movies };
        db.AddRange(library, playlist);

        var movies = new List<Movie>();
        for (var i = 0; i < itemCount; i++)
        {
            var movie = new Movie { Id = Guid.NewGuid(), Title = $"Movie {i + 1}", LibraryId = library.Id };
            movies.Add(movie);
            db.Add(movie);
            // The first item's Order is 0 — the value the old code could not tell
            // apart from "this media isn't in the playlist".
            db.Add(new PlaylistItem { Id = Guid.NewGuid(), PlaylistId = playlist.Id, MediaItemId = movie.Id, Order = i });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Fixture { PlaylistId = playlist.Id, Movies = movies };
    }

    [Fact]
    public async Task The_first_item_offers_the_second_one()
    {
        await using var db = NewContext();
        var fixture = await SeedAsync(db);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(fixture.Movies[0].Id, "playlist", fixture.PlaylistId, null);

        result.NextItem.Should().NotBeNull();
        result.NextItem!.Title.Should().Be("Movie 2");
        result.PreviousItem.Should().BeNull();
    }

    [Fact]
    public async Task A_middle_item_offers_both_neighbours()
    {
        await using var db = NewContext();
        var fixture = await SeedAsync(db);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(fixture.Movies[1].Id, "playlist", fixture.PlaylistId, null);

        result.NextItem!.Title.Should().Be("Movie 3");
        result.PreviousItem!.Title.Should().Be("Movie 1");
    }

    [Fact]
    public async Task The_last_item_offers_only_the_one_before_it()
    {
        await using var db = NewContext();
        var fixture = await SeedAsync(db);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(fixture.Movies[2].Id, "playlist", fixture.PlaylistId, null);

        result.NextItem.Should().BeNull();
        result.PreviousItem!.Title.Should().Be("Movie 2");
    }

    [Fact]
    public async Task A_single_item_playlist_offers_neither_neighbour()
    {
        await using var db = NewContext();
        var fixture = await SeedAsync(db, itemCount: 1);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(fixture.Movies[0].Id, "playlist", fixture.PlaylistId, null);

        result.NextItem.Should().BeNull();
        result.PreviousItem.Should().BeNull();
    }

    [Fact]
    public async Task A_trashed_item_is_skipped_rather_than_offered()
    {
        await using var db = NewContext();
        var fixture = await SeedAsync(db);
        fixture.Movies[1].MissingSince = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(fixture.Movies[0].Id, "playlist", fixture.PlaylistId, null);

        result.NextItem!.Title.Should().Be("Movie 3");
    }

    [Fact]
    public async Task Media_outside_the_playlist_offers_nothing_from_it()
    {
        await using var db = NewContext();
        var fixture = await SeedAsync(db);
        var stranger = new Movie { Id = Guid.NewGuid(), Title = "Not in the playlist", LibraryId = fixture.Movies[0].LibraryId };
        db.Add(stranger);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(stranger.Id, "playlist", fixture.PlaylistId, null);

        result.NextItem.Should().BeNull();
        result.PreviousItem.Should().BeNull();
    }
}
