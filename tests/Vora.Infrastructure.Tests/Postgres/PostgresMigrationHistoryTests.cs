using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Settings;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresMigrationHistoryTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly string[] SquashedAway =
    {
        "20260927192048_StopSeedingServerSettings",
        "20260928151531_NameSeasonZeroSpecials",
        "20261001232631_AddAiPlaylistMatchCutoff",
        "20261002220851_ReplaceAiPlaylistMatchCutoffWithWindow",
        "20261002235840_AddSongProfilesAndEmbeddingSource",
        "20261003010710_RefreshArtistsForBiographies",
    };

    [Fact]
    public async Task A_database_that_ran_every_migration_before_the_squash_has_nothing_left_to_apply()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = database.NewContext())
        {
            foreach (var id in SquashedAway)
            {
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({id}, '10.0.0') ON CONFLICT DO NOTHING",
                    cancellationToken);
            }
        }

        await using var check = database.NewContext();

        (await check.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        await check.Database.MigrateAsync(cancellationToken);
    }

    [Fact]
    public async Task A_new_server_keeps_its_seeded_settings_with_the_ai_match_window_at_its_default()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();

        var settings = await db.Set<ServerSetting>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

        settings.Id.Should().Be("GLOBAL_SETTINGS");
        settings.AiPlaylistMatchWindow.Should().Be(0.04);
    }
}
