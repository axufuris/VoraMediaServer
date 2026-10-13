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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await new AiPlaylistRepository(db).FindNearestTracksAsync(Vector(random), MusicAccessFilter.Unrestricted, AiTrackFilter.None, 200);

        found.Should().HaveCount(200);
        found.Select(c => c.Distance).Should().BeInAscendingOrder();
    }

    private const string OpenAiModel = "text-embedding-3-small";
    private const string OtherModel = "nomic-embed-text";

    private static async Task<(MediaLibrary Library, Movie Unstamped, Movie ByOtherModel, Movie Fresh)> MoviesAsync(VoraDbContext db, float[] vector)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        var unstamped = new Movie { Id = Guid.NewGuid(), Title = "Read before models were recorded", LibraryId = library.Id };
        var byOtherModel = new Movie { Id = Guid.NewGuid(), Title = "Read by another model", LibraryId = library.Id };
        var fresh = new Movie { Id = Guid.NewGuid(), Title = "Never read", LibraryId = library.Id };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Movie>().AddRange(unstamped, byOtherModel, fresh);
        db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = unstamped.Id, Embedding = new Pgvector.Vector(vector) });
        db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = byOtherModel.Id, Embedding = new Pgvector.Vector(vector), Model = OtherModel });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return (library, unstamped, byOtherModel, fresh);
    }

    [Fact]
    public async Task A_title_waits_for_an_embedding_until_the_current_model_has_read_it()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (_, unstamped, byOtherModel, fresh) = await MoviesAsync(db, Vector(new Random(3)));
        var repository = new OpenAiRecommendationRepository(db);

        var forOpenAi = (await repository.GetMediaItemsMissingEmbeddingsAsync(int.MaxValue, OpenAiModel)).Select(m => m.Id).ToList();
        var forOther = (await repository.GetMediaItemsMissingEmbeddingsAsync(int.MaxValue, OtherModel)).Select(m => m.Id).ToList();

        forOpenAi.Should().Contain(new[] { byOtherModel.Id, fresh.Id }).And.NotContain(unstamped.Id);
        forOther.Should().Contain(new[] { unstamped.Id, fresh.Id }).And.NotContain(byOtherModel.Id);
    }

    [Fact]
    public async Task Recommendations_only_search_vectors_the_current_model_made()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var vector = Vector(new Random(5));
        var (library, unstamped, byOtherModel, _) = await MoviesAsync(db, vector);
        var repository = new OpenAiRecommendationRepository(db);
        var viewer = Guid.NewGuid();

        var withOpenAi = await repository.VectorSearchUnwatchedMediaAsync(viewer, library.Id, vector, 10, OpenAiModel);
        var withOther = await repository.VectorSearchUnwatchedMediaAsync(viewer, library.Id, vector, 10, OtherModel);

        withOpenAi.Should().Equal(unstamped.Id);
        withOther.Should().Equal(byOtherModel.Id);
    }

    [Fact]
    public async Task Saving_replaces_a_vector_another_model_made_and_skips_a_title_that_is_gone()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var random = new Random(11);
        await using var db = database.NewContext();
        var (_, unstamped, _, fresh) = await MoviesAsync(db, Vector(random));

        await new OpenAiRecommendationRepository(db).SaveEmbeddingsAsync(new List<MediaItemEmbedding>
        {
            new() { MediaItemId = unstamped.Id, Embedding = new Pgvector.Vector(Vector(random)), Model = OtherModel },
            new() { MediaItemId = fresh.Id, Embedding = new Pgvector.Vector(Vector(random)), Model = OtherModel },
            new() { MediaItemId = Guid.NewGuid(), Embedding = new Pgvector.Vector(Vector(random)), Model = OtherModel },
        });

        var saved = await db.MediaItemEmbeddings.AsNoTracking()
            .Where(e => e.MediaItemId == unstamped.Id || e.MediaItemId == fresh.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        saved.Should().HaveCount(2).And.OnlyContain(e => e.Model == OtherModel);
    }

    [Fact]
    public async Task A_song_search_only_reads_vectors_the_named_model_made()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var random = new Random(13);
        await using var db = database.NewContext();
        var (libraryId, albumId) = await MusicLibraryAsync(db);
        var model = "model-" + Guid.NewGuid().ToString("N");
        var current = new Track { Id = Guid.NewGuid(), Title = "Read by the current model", LibraryId = libraryId, AlbumId = albumId, Artist = "Daft Punk" };
        var stale = new Track { Id = Guid.NewGuid(), Title = "Read by the last model", LibraryId = libraryId, AlbumId = albumId, Artist = "Daft Punk" };
        db.Set<Track>().AddRange(current, stale);
        db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = current.Id, Embedding = new Pgvector.Vector(Vector(random)), Model = model });
        db.MediaItemEmbeddings.Add(new MediaItemEmbedding { MediaItemId = stale.Id, Embedding = new Pgvector.Vector(Vector(random)), Model = OpenAiModel });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new AiPlaylistRepository(db);

        var found = await repository.FindNearestTracksAsync(Vector(random), MusicAccessFilter.Unrestricted, new AiTrackFilter(EmbeddingModel: model), 50);

        found.Select(c => c.TrackId).Should().Equal(current.Id);
    }
}
