using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.LibraryMigration;
using Vora.Application.Posters;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// A season's ratings overlay draws its show's ratings for the ones it lacks, so
// a change to the show's ratings sends its already-overlaid seasons back to the
// overlay step.
public class PostgresSeasonOverlayRatingsTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly DateTime Drawn = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    private async Task<(TvShow Show, Season One, Season Two, Season Unrelated, Movie Movie)> SeedAsync(CancellationToken cancellationToken)
    {
        var library = new MediaLibrary { Name = "Shows " + Guid.NewGuid().ToString("N")[..6], Type = LibraryType.TvShow, FolderPaths = ["/shows"] };
        var show = new TvShow
        {
            Title = "Severance", LibraryId = library.Id, ServerAdminRating = 9m,
            ThirdPartyRating1Name = "TMDB", ThirdPartyRating1 = 8.3m, ThirdPartyRating2Name = "Rotten Tomatoes", ThirdPartyRating2 = 96m,
        };
        var other = new TvShow { Title = "Dark", LibraryId = library.Id };
        var two = new Season { Title = "Season 2", SeasonNumber = 2, TvShowId = show.Id, LibraryId = library.Id, LastOverlayGeneratedAt = Drawn };
        var one = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id, LastOverlayGeneratedAt = Drawn };
        var unrelated = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = other.Id, LibraryId = library.Id, LastOverlayGeneratedAt = Drawn };
        var movie = new Movie { Title = "Heat", LibraryId = library.Id };
        await using var seed = database.NewContext();
        seed.AddRange(library, show, other, two, one, unrelated, movie);
        await seed.SaveChangesAsync(cancellationToken);
        return (show, one, two, unrelated, movie);
    }

    private async Task<DateTime?> DrawnAtAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = database.NewContext();
        return await db.MediaItems.Where(m => m.Id == id).Select(m => m.LastOverlayGeneratedAt).SingleAsync(cancellationToken);
    }

    [Fact]
    public async Task A_shows_ratings_seasons_and_stale_marking_read_and_write_the_right_rows()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (show, one, two, unrelated, movie) = await SeedAsync(cancellationToken);

        await using (var db = database.NewContext())
        {
            var repository = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

            (await repository.GetOverlayRatingsAsync(show.Id)).Should().Be(new OverlayRatings(9m, "TMDB", 8.3m, "Rotten Tomatoes", 96m));
            (await repository.GetOverlayRatingsAsync(Guid.NewGuid())).Should().BeNull();
            (await repository.GetSeasonIdsOfShowAsync(show.Id)).Should().Equal(one.Id, two.Id);
            (await repository.GetSeasonIdsOfShowAsync(movie.Id)).Should().BeEmpty();

            await repository.MarkSeasonOverlaysStaleAsync(show.Id);
        }

        (await DrawnAtAsync(one.Id, cancellationToken)).Should().BeNull();
        (await DrawnAtAsync(two.Id, cancellationToken)).Should().BeNull();
        (await DrawnAtAsync(unrelated.Id, cancellationToken)).Should().Be(Drawn);
    }

    [Fact]
    public async Task An_imported_server_rating_on_a_show_sends_its_seasons_back_to_the_overlay_step()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (show, one, two, unrelated, _) = await SeedAsync(cancellationToken);

        await using (var db = database.NewContext())
        {
            await new LibraryMigrationRepository(db).BulkSetAdminRatingsAsync([new RatingUpsert { MediaItemId = show.Id, Rating = 7m }]);
        }

        (await DrawnAtAsync(one.Id, cancellationToken)).Should().BeNull();
        (await DrawnAtAsync(two.Id, cancellationToken)).Should().BeNull();
        (await DrawnAtAsync(unrelated.Id, cancellationToken)).Should().Be(Drawn);
    }
}
