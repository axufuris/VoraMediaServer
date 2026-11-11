using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresMoodTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static async Task<(Album Album, Guid LibraryId)> AlbumAsync(VoraDbContext db, string artistName)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = artistName, Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = artistName, LibraryId = library.Id, ArtworkUrl = $"/{artistName}.jpg" };
        var album = new Album { Id = Guid.NewGuid(), Title = artistName + " Hits", ArtistId = artist.Id, LibraryId = library.Id };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Artist>().Add(artist);
        db.Set<Album>().Add(album);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (album, library.Id);
    }

    private static Track Song(Album album, Guid libraryId, string title, long? listeners, params string[] moods) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        AlbumId = album.Id,
        LibraryId = libraryId,
        GlobalListeners = listeners,
        Moods = moods.Length == 0 ? null : moods.ToList()
    };

    [Fact]
    public async Task Moods_are_counted_per_song_within_the_libraries_a_profile_can_see()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (open, openLibrary) = await AlbumAsync(db, "Counted");
        var (hidden, hiddenLibrary) = await AlbumAsync(db, "Hidden");
        db.Set<Track>().AddRange(
            Song(open, openLibrary, "A", null, "chill", "dreamy"),
            Song(open, openLibrary, "B", null, "chill"),
            Song(open, openLibrary, "C", null),
            Song(hidden, hiddenLibrary, "D", null, "chill", "dark"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new MusicRepository(database.NewContext());
        var access = new MusicAccessFilter { HasAllLibraryAccess = false, AllowedLibraryIds = new List<Guid> { openLibrary } };

        var samples = await repository.GetMoodSamplesAsync(access, 10);

        samples.ToDictionary(s => s.Key, s => s.Value.TrackCount).Should().BeEquivalentTo(new Dictionary<string, int> { ["chill"] = 2, ["dreamy"] = 1 });
        (await repository.CountTracksForMoodAsync("chill", access)).Should().Be(2);
        (await repository.CountTracksForMoodAsync("dark", access)).Should().Be(0);
    }

    [Fact]
    public async Task A_moods_covers_are_its_most_listened_songs_albums_with_the_artists_picture_standing_in()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (bare, library) = await AlbumAsync(db, "Covers");
        var framed = new Album { Id = Guid.NewGuid(), Title = "Framed", ArtistId = bare.ArtistId, LibraryId = library, ArtworkUrl = "/framed.jpg" };
        db.Set<Album>().Add(framed);
        db.Set<Track>().AddRange(
            Song(bare, library, "Quiet", 10, "spooky"),
            Song(framed, library, "Loud", 900_000, "spooky"),
            Song(bare, library, "Middling", 5_000, "spooky"),
            Song(framed, library, "Elsewhere", 1_000_000, "romantic"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new MusicRepository(database.NewContext());
        var access = new MusicAccessFilter { HasAllLibraryAccess = false, AllowedLibraryIds = new List<Guid> { library } };

        var samples = await repository.GetMoodSamplesAsync(access, 2);

        samples["spooky"].TrackCount.Should().Be(3);
        samples["spooky"].Covers.Should().Equal("/framed.jpg", "/Covers.jpg");
        samples["romantic"].Covers.Should().Equal("/framed.jpg");
    }

    [Fact]
    public async Task A_moods_songs_come_most_listened_first_a_page_at_a_time()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (album, libraryId) = await AlbumAsync(db, "Paged");
        db.Set<Track>().AddRange(
            Song(album, libraryId, "Unknown", null, "epic"),
            Song(album, libraryId, "Hit", 900_000, "epic"),
            Song(album, libraryId, "Deep cut", 1_000, "epic"),
            Song(album, libraryId, "Other mood", 5_000_000, "sad"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new MusicRepository(database.NewContext());

        var first = await repository.GetTracksForMoodAsync("epic", new MusicAccessFilter(), 0, 2);
        var second = await repository.GetTracksForMoodAsync("epic", new MusicAccessFilter(), 2, 2);

        first.Select(t => t.Title).Should().Equal("Hit", "Deep cut");
        second.Select(t => t.Title).Should().Equal("Unknown");
        (first[0].Album?.Artist?.ArtworkUrl).Should().Be("/Paged.jpg");
    }

    [Fact]
    public async Task A_mood_shuffle_draws_only_that_moods_songs()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (album, libraryId) = await AlbumAsync(db, "Shuffled");
        db.Set<Track>().AddRange(Enumerable.Range(1, 6).Select(i => Song(album, libraryId, $"Groove {i}", null, "groovy")));
        db.Set<Track>().Add(Song(album, libraryId, "Not groovy", null, "calm"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new MusicRepository(database.NewContext());

        var shuffled = await repository.GetRandomTracksForMoodAsync("groovy", new MusicAccessFilter(), 4);

        shuffled.Should().HaveCount(4).And.OnlyContain(t => t.Title.StartsWith("Groove"));
        shuffled.Select(t => t.Id).Should().OnlyHaveUniqueItems();
    }
}
