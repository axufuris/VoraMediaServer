using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// The default home rows sort by expressions, and their indexes
// (IX_MediaItems_PresentByReleaseDate / PresentByDateAdded) are written in
// the migration SQL, where nothing ties them to the ORDER BY EF generates.
// These fail if a change to SmartListRepository.ApplySortOrder leaves a row
// sorting every title again instead of reading the first 20 from an index.
public class PostgresHomeRowIndexTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private sealed class Commands : DbCommandInterceptor
    {
        public List<(string Sql, NpgsqlParameter[] Parameters)> Seen { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Seen.Add((command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(SmartListSortBy.ReleaseDateDesc, "Movie", "IX_MediaItems_PresentByReleaseDate")]
    [InlineData(SmartListSortBy.ReleaseDateDesc, "Episode", "IX_MediaItems_PresentByReleaseDate")]
    [InlineData(SmartListSortBy.ReleaseDateDesc, "Movie,Episode", "IX_MediaItems_PresentByReleaseDate")]
    [InlineData(SmartListSortBy.DateAddedDesc, "Movie", "IX_MediaItems_PresentByDateAdded")]
    [InlineData(SmartListSortBy.DateAddedDesc, "TvShow", "IX_MediaItems_PresentByDateAdded")]
    [InlineData(SmartListSortBy.DateAddedDesc, "Movie,TvShow,Season,Episode", "IX_MediaItems_PresentByDateAdded")]
    public async Task A_default_home_row_reads_its_first_titles_from_an_index(SmartListSortBy sortBy, string mediaTypes, string index)
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seed = database.NewContext())
        {
            var library = new MediaLibrary { Name = "Mixed " + Guid.NewGuid().ToString("N")[..8], Type = LibraryType.TvShow, FolderPaths = new List<string> { "/mixed" } };
            var show = new TvShow { Title = "Lioness", LibraryId = library.Id, AddedAt = DateTime.UtcNow };
            var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id };
            seed.AddRange(library, show, season,
                new Episode { Title = "S1E1", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = library.Id, ReleaseDate = new DateOnly(2023, 7, 23) },
                new Movie { Title = "Sicario", LibraryId = library.Id, ReleaseDate = new DateOnly(2015, 9, 18) });
            await seed.SaveChangesAsync(cancellationToken);
        }

        var commands = new Commands();
        await using var db = database.NewContext(commands);
        var rules = new SmartListRulesDto { MediaTypes = mediaTypes.Split(',').ToList() };
        await new SmartListRepository(db).GetSmartListItemsAsync(Guid.NewGuid(), null, rules, sortBy, 20);

        // The last command is the one that reads the tiles. With sequential
        // scans and sorts priced out, Postgres reads it from an index only if
        // the index gives the order the row asks for.
        var (sql, parameters) = commands.Seen[^1];
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off; SET enable_sort = off; SET enable_bitmapscan = off", connection))
        {
            await off.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var explain = new NpgsqlCommand("EXPLAIN (COSTS OFF) " + sql, connection);
        explain.Parameters.AddRange(parameters);
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) plan.Add(reader.GetString(0));
        }

        var limit = plan.FindIndex(l => l.TrimStart(' ', '-', '>').StartsWith("Limit", StringComparison.Ordinal));
        limit.Should().BeGreaterThanOrEqualTo(0, string.Join("\n", plan));
        plan[limit + 1].Should().Contain("Index Scan").And.Contain(index, string.Join("\n", plan));
    }
}
