using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// A collection an admin hid is kept out of collection search and its Home row,
// for viewers and admins alike, the way an out-of-season one is.
public class PostgresHiddenCollectionTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Search_and_the_home_rows_leave_out_a_hidden_collection()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.NewContext();

        var library = new MediaLibrary { Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies/" + tag } };
        var film = new Movie { Title = "John Wick " + tag, LibraryId = library.Id };
        var hidden = new Collection { Title = $"Wick {tag} Collection", Hidden = true, Items = new List<MediaItem> { film } };
        var shown = new Collection { Title = $"Wick {tag} Universe", Items = new List<MediaItem> { film } };
        db.AddRange(library, film, hidden, shown,
            new SmartList { Title = "Hidden row " + tag, CollectionId = hidden.Id },
            new SmartList { Title = "Shown row " + tag, CollectionId = shown.Id });
        await db.SaveChangesAsync(cancellationToken);

        var found = await new SearchRepository(db).SearchCollectionsAsync("Wick " + tag, 10, true, new List<Guid>(), new DateOnly(2026, 11, 14));
        found.Select(c => c.Title).Should().Equal($"Wick {tag} Universe");

        var rows = new SmartListRepository(db);
        foreach (var isAdmin in new[] { false, true })
        {
            var titles = (await rows.GetActiveClientListsAsync(isAdmin, new DateOnly(2026, 11, 14))).Select(r => r.Title).ToList();
            titles.Should().Contain("Shown row " + tag);
            titles.Should().NotContain("Hidden row " + tag);
        }
    }
}
