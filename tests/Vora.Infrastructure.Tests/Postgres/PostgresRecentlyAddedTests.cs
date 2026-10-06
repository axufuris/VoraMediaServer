namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresRecentlyAddedTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Recently_added_keeps_one_tile_per_show_on_postgres()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new RecentlyAddedShowsWorld();
        await world.SeedAsync(db);

        var items = await world.RecentlyAddedAsync(db, RecentlyAddedShowsWorld.MoviesAndShows);

        items.Select(i => i.Id).Should().Equal(world.NewShowId, world.MovieId, world.NewEpisodeId);
    }
}
