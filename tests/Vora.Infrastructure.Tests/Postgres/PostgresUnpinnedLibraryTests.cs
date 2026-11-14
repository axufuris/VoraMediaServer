namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresUnpinnedLibraryTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Recently_added_movies_and_shows_leaves_out_unpinned_libraries_and_still_fills_on_postgres()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new UnpinnedLibrariesWorld();
        await world.SeedAsync(db);

        var everything = await world.RowAsync(db, UnpinnedLibrariesWorld.MoviesAndShows, 4);
        var pinnedOnly = await world.RowAsync(db, UnpinnedLibrariesWorld.MoviesAndShows, 4, world.Unpinned);

        everything.Select(i => i.Title).Should().Equal("Raising Hope", "Seinfeld", "Test Film B", "Test Film A");
        pinnedOnly.Select(i => i.Title).Should().Equal("Parks and Recreation", "The Office", "Arrival", "Alien");
    }
}
