using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

public sealed class LegacyDatabase : IAsyncDisposable
{
    public static readonly string[] QaHistory =
    {
        "20260927192048_StopSeedingServerSettings",
        "20260928151531_NameSeasonZeroSpecials",
        "20261001232631_AddAiPlaylistMatchCutoff",
        "20261002220851_ReplaceAiPlaylistMatchCutoffWithWindow",
        "20261002235840_AddSongProfilesAndEmbeddingSource",
        "20261003010710_RefreshArtistsForBiographies",
        "20261003013801_StoreSongProfileListsAsArrays",
    };

    public const string SetupGuideMigration = "20261004030256_AddSetupGuide";

    private const string OldChainColumns = """
        ALTER TABLE "MediaItems" ADD COLUMN "Energy" integer;
        ALTER TABLE "MediaItems" ADD COLUMN "GoodFor" text[];
        ALTER TABLE "MediaItems" ADD COLUMN "IsInstrumental" boolean;
        ALTER TABLE "MediaItems" ADD COLUMN "Moods" text[];
        ALTER TABLE "MediaItems" ADD COLUMN "ProfiledAt" timestamp with time zone;
        ALTER TABLE "MediaItems" ADD COLUMN "Themes" text[];
        ALTER TABLE "MediaItemEmbeddings" ADD COLUMN "Model" character varying(64);
        ALTER TABLE "MediaItemEmbeddings" ADD COLUMN "SourceHash" character varying(64);
        ALTER TABLE "ServerSettings" ADD COLUMN "AiPlaylistMatchWindow" double precision NOT NULL DEFAULT 0.04;
        """;

    private const string SetupGuideColumns = """
        ALTER TABLE "ServerSettings" ADD COLUMN "SetupGuideContent" integer NOT NULL DEFAULT 3;
        ALTER TABLE "ServerSettings" ADD COLUMN "SetupGuideStatus" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ServerSettings" ADD COLUMN "SetupGuideStep" character varying(64);
        """;

    private readonly string _name = "vora_test_" + Guid.NewGuid().ToString("N");
    private readonly string _connectionString;

    public LegacyDatabase()
    {
        _connectionString = PostgresDatabase.IsConfigured
            ? new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = _name }.ConnectionString
            : string.Empty;
    }

    public VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql(_connectionString, npgsql => npgsql.UseVector())
            .Options);

    public async Task BuildQaStateAsync(bool withSetupGuide, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(MigrationHistoryTests.Initial, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(OldChainColumns, cancellationToken);

        var history = withSetupGuide ? QaHistory.Append(SetupGuideMigration) : QaHistory;
        if (withSetupGuide)
        {
            await db.Database.ExecuteSqlRawAsync(SetupGuideColumns, cancellationToken);
        }

        foreach (var id in history)
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({id}, '10.0.0')",
                cancellationToken);
        }
    }

    public const string FirstCombinedMigration = "20261004151213_ChangesSinceInitial";

    public const string SecondCombinedMigration = "20261004183407_ChangesSinceInitial";

    private const string OldDefaultRowOrder = """
        UPDATE "SmartLists" SET "DisplayOrder" = 7 WHERE "DefaultKey" = 'recently-added-music';
        UPDATE "SmartLists" SET "DisplayOrder" = 8 WHERE "DefaultKey" = 'new-podcast-episodes';
        UPDATE "SmartLists" SET "DisplayOrder" = 9 WHERE "DefaultKey" = 'favorite-stations';
        UPDATE "SmartLists" SET "DisplayOrder" = 10 WHERE "DefaultKey" = 'recent-recordings';
        """;

    public async Task BuildEarlierCombinedStateAsync(string migrationId, bool withTaskTable, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(OldDefaultRowOrder, cancellationToken);
        if (!withTaskTable)
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"PendingTasks\"", cancellationToken);
        }

        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {MigrationHistoryTests.ChangesSinceInitial}",
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, '10.0.8')",
            cancellationToken);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (!PostgresDatabase.IsConfigured) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
