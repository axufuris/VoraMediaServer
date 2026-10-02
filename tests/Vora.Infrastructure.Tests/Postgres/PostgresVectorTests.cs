using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Domain.Entities.Ai;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresVectorTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private const int Dimensions = 1536;

    private static float[] Vector(Random random)
    {
        var v = new float[Dimensions];
        for (var i = 0; i < v.Length; i++) v[i] = (float)random.NextDouble();
        return v;
    }

    private static async Task<(Guid LibraryId, Guid AlbumId)> MusicLibraryAsync(VoraDbContext db)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Daft Punk", LibraryId = library.Id };
        var album = new Album { Id = Guid.NewGuid(), Title = "Discovery", ArtistId = artist.Id, LibraryId = library.Id, Year = 2001 };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Artist>().Add(artist);
        db.Set<Album>().Add(album);
        await db.SaveChangesAsync();
        return (library.Id, album.Id);
    }

    [Fact]
    public async Task A_filtered_nearest_track_search_returns_more_than_the_index_default_of_forty()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var random = new Random(7);
        await using var db = database.NewContext();
        var (libraryId, albumId) = await MusicLibraryAsync(db);

        var movieLibrary = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        db.Set<MediaLibrary>().Add(movieLibrary);
        for (var i = 0; i < 400; i++)
        {
            var movie = new Movie { Id = Guid.NewGuid(), Title = $"Movie {i}", LibraryId = movieLibrary.Id };
            db.Set<Movie>().Add(movie);
            db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = movie.Id, Embedding = new Pgvector.Vector(Vector(random)) });
        }
        for (var i = 0; i < 300; i++)
        {
            var track = new Track { Id = Guid.NewGuid(), Title = $"Track {i}", LibraryId = libraryId, AlbumId = albumId, Artist = $"Artist {i}" };
            db.Set<Track>().Add(track);
            db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = track.Id, Embedding = new Pgvector.Vector(Vector(random)) });
        }
        await db.SaveChangesAsync();

        var found = await new AiPlaylistRepository(db).FindNearestTracksAsync(Vector(random), MusicAccessFilter.Unrestricted, AiTrackFilter.None, 200);

        found.Should().HaveCount(200);
        found.Select(c => c.Distance).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Embedding_saves_skip_items_that_are_gone_or_already_embedded()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var random = new Random(11);
        await using var db = database.NewContext();
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        var embedded = new Movie { Id = Guid.NewGuid(), Title = "Embedded", LibraryId = library.Id };
        var fresh = new Movie { Id = Guid.NewGuid(), Title = "Fresh", LibraryId = library.Id };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Movie>().AddRange(embedded, fresh);
        db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = embedded.Id, Embedding = new Pgvector.Vector(Vector(random)) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await EmbeddingWrites.InsertNewAsync(db, new[]
        {
            new MediaItemEmbedding { MediaItemId = embedded.Id, Embedding = new Pgvector.Vector(Vector(random)) },
            new MediaItemEmbedding { MediaItemId = fresh.Id, Embedding = new Pgvector.Vector(Vector(random)) },
            new MediaItemEmbedding { MediaItemId = Guid.NewGuid(), Embedding = new Pgvector.Vector(Vector(random)) },
        });

        saved.Should().Be(1);
        (await db.MediaItemEmbeddings.AsNoTracking().CountAsync(e => e.MediaItemId == fresh.Id || e.MediaItemId == embedded.Id)).Should().Be(2);
    }
}
