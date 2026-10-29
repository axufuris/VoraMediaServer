using Microsoft.EntityFrameworkCore;
using Vora.Application.Collections.ViewModels;
using Vora.Application.Libraries.ViewModels;
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
    public void A_library_item_s_watched_state_episodes_left_and_rating_come_from_the_same_query()
    {
        using var db = PostgresContext();

        var sql = db.MediaItems
            .Select(LibraryItemVM.ProjectionFor(Guid.NewGuid(), db.UserMediaStates, db.UserMediaRatings))
            .ToQueryString();

        sql.Should().Contain("FROM \"UserMediaStates\"");
        sql.Should().Contain("FROM \"UserMediaRatings\"");
        sql.Should().NotContain("@__allEpisodeIds");
    }

    [Fact]
    public void A_collection_s_items_carry_their_watched_state_from_the_same_query()
    {
        using var db = PostgresContext();

        var sql = db.Collections
            .Select(CollectionDetailsVM.ProjectionFor(Guid.NewGuid(), db.UserMediaStates))
            .ToQueryString();

        sql.Should().Contain("FROM \"UserMediaStates\"");
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
