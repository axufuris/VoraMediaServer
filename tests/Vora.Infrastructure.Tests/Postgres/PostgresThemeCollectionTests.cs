using Vora.Domain.Entities.Ai;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresThemeCollectionTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private const int Dimensions = 1536;
    private const string Model = "text-embedding-3-small";

    private static float[] Toward(int axis, float lean)
    {
        var v = new float[Dimensions];
        v[0] = 1f;
        v[axis] = lean;
        return v;
    }

    [Fact]
    public async Task Titles_tagged_with_a_theme_keyword_are_found_in_the_collection_s_library()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        var hocusPocus = new Movie { Title = "Hocus Pocus", LibraryId = library.Id, Keywords = new() { "witch", "halloween" } };
        var gone = new Movie { Title = "Trick 'r Treat", LibraryId = library.Id, Keywords = new() { "halloween" }, MissingSince = DateTime.UtcNow };
        var untagged = new Movie { Title = "Saw", LibraryId = library.Id, Keywords = new() { "slasher" } };
        var elsewhere = new Movie { Title = "Halloween", LibraryId = Guid.NewGuid(), Keywords = new() { "halloween" } };
        db.AddRange(library, hocusPocus, gone, untagged, elsewhere);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await new CollectionThemeRepository(db).FindByKeywordsAsync(new[] { "halloween", "trick or treating" }, library.Id);

        found.Should().Equal(hocusPocus.Id);
    }

    [Fact]
    public async Task The_closest_titles_come_nearest_first_from_the_current_model_only()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        var closest = new Movie { Title = "Hocus Pocus", LibraryId = library.Id };
        var next = new Movie { Title = "Beetlejuice", LibraryId = library.Id };
        var far = new Movie { Title = "Elf", LibraryId = library.Id };
        var otherModel = new Movie { Title = "Halloweentown", LibraryId = library.Id };
        db.AddRange(library, closest, next, far, otherModel);
        db.MediaItemEmbeddings.AddRange(
            new MediaItemEmbedding { MediaItemId = closest.Id, Embedding = new Pgvector.Vector(Toward(1, 0.1f)), Model = Model },
            new MediaItemEmbedding { MediaItemId = next.Id, Embedding = new Pgvector.Vector(Toward(1, 0.6f)), Model = Model },
            new MediaItemEmbedding { MediaItemId = far.Id, Embedding = new Pgvector.Vector(Toward(2, 3f)), Model = Model },
            new MediaItemEmbedding { MediaItemId = otherModel.Id, Embedding = new Pgvector.Vector(Toward(1, 0f)), Model = "nomic-embed-text" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await new CollectionThemeRepository(db).FindNearestAsync(Toward(1, 0f), Model, library.Id, 2);

        found.Should().Equal(closest.Id, next.Id);
    }
}
