using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// Search matches with ILIKE '%word%', which no ordinary index can serve, so it
// read every title, person, artist and album. The migration adds pg_trgm GIN
// indexes that ILIKE uses as they are. They are optional: a database that
// can't have them must still migrate, and search must still work there.
public class PostgresSearchIndexTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly (string Table, string Column, string Index)[] Searched =
    {
        ("MediaItems", "Title", "IX_MediaItems_TitleTrigram"),
        ("Actors", "Name", "IX_Actors_NameTrigram"),
        ("Artists", "Name", "IX_Artists_NameTrigram"),
        ("Albums", "Title", "IX_Albums_TitleTrigram"),
    };

    // A trigram GIN index is what lets ILIKE '%word%' skip the scan; on a
    // copy of QA it took people from 21 ms to 0.06 and songs from 22 to 0.5.
    [Fact]
    public async Task Each_searched_column_has_a_trigram_index()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = database.NewContext();

        foreach (var (table, column, index) in Searched)
        {
            var definition = await db.Database
                .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = current_schema() AND indexname = {0}", index)
                .SingleAsync(cancellationToken);

            definition.Should().Contain($"ON public.\"{table}\" USING gin (\"{column}\" gin_trgm_ops)");
        }
    }

    // pg_trgm is a trusted extension, so it needs CREATE on the database rather
    // than a superuser. A user without that is refused it, which is the case of
    // a server where it can't be installed: the migration must carry on.
    [Fact]
    public async Task A_database_user_who_may_not_install_extensions_still_migrates_and_search_still_works()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var role = "vora_limited_" + suffix;
        var name = "vora_test_limited_" + suffix;
        var password = Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = "postgres" }.ConnectionString;
        var adminOnDatabase = new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = name }.ConnectionString;
        var limited = new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = name, Username = role, Password = password }.ConnectionString;

        await ExecuteAsync(admin, $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}'", cancellationToken);
        await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"", cancellationToken);
        try
        {
            // What an administrator sets up for such a user: vector installed for
            // it, and a schema it may create tables in, but no CREATE on the
            // database.
            await ExecuteAsync(adminOnDatabase, $"""
                CREATE EXTENSION IF NOT EXISTS vector;
                GRANT USAGE, CREATE ON SCHEMA public TO "{role}";
                """, cancellationToken);

            await using (var db = Context(limited))
            {
                await db.Database.MigrateAsync(cancellationToken);
                (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
                (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname LIKE '%Trigram'").SingleAsync(cancellationToken))
                    .Should().Be(0);

                var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
                db.Add(library);
                db.Add(new Movie { Title = "The Haunting of Hill House", LibraryId = library.Id });
                await db.SaveChangesAsync(cancellationToken);
            }

            await using (var db = Context(limited))
            {
                var found = await new SearchRepository(db).SearchMediaAsync("Movie", "haunting", 10, true, new List<Guid>(), new List<string>(), new List<string>(), false);
                found.Select(f => f.Title).Should().Equal("The Haunting of Hill House");
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", CancellationToken.None);
            await ExecuteAsync(admin, $"DROP ROLE IF EXISTS \"{role}\"", CancellationToken.None);
        }
    }

    private static VoraDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options);

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
