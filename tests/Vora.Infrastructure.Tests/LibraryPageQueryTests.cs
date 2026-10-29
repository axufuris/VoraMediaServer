using Microsoft.EntityFrameworkCore;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class LibraryPageQueryTests
{
    private static VoraDbContext PostgresContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql("Host=localhost;Database=sql-only", npgsql => npgsql.UseVector())
            .Options);

    [Fact]
    public void A_show_s_episodes_left_are_counted_by_the_database_one_row_per_show()
    {
        using var db = PostgresContext();

        var sql = UserMediaStateRepository.EpisodeCountsByShow(db, [Guid.NewGuid()], Guid.NewGuid()).ToQueryString();

        sql.Should().Contain("LEFT JOIN");
        sql.Should().Contain("GROUP BY");
    }

    [Fact]
    public void A_season_s_episodes_left_are_counted_by_the_database_one_row_per_season()
    {
        using var db = PostgresContext();

        var sql = UserMediaStateRepository.EpisodeCountsBySeason(db, [Guid.NewGuid()], Guid.NewGuid()).ToQueryString();

        sql.Should().Contain("LEFT JOIN");
        sql.Should().Contain("GROUP BY");
    }

    [Fact]
    public void A_library_lists_its_titles_by_naming_their_types_so_the_library_and_type_index_applies()
    {
        using var db = PostgresContext();

        var sql = MediaRepository.TitlesInLibrary(db.MediaItems, Guid.NewGuid()).ToQueryString();

        sql.Should().Contain("\"MediaType\" IN ('Movie', 'TvShow', 'Track')");
        sql.Should().NotContain("<>");
        sql.Should().NotContain("NOT IN");
    }
}
