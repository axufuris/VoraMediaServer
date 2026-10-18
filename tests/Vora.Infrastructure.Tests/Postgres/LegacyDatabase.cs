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

    public const string ReleasedMigration = "20261004192607_ChangesSinceInitial";

    public const string SecondReleaseMigration = "20261009020545_ChangesSinceInitial";

    public const string HardwareAccelerationMigration = "20261011233931_ChangesSinceInitial";

    public const string StreamOriginMigration = "20261012232953_ChangesSinceInitial";

    public const string LocalMetadataMigration = "20261013214138_ChangesSinceInitial";

    public const string RememberedChoicesMigration = "20261014181817_ChangesSinceInitial";

    private const string RememberedChoicesProfileColumns = """
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "PreferredAudioLanguage" character varying(16);
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "PreferredSubtitleLanguage" character varying(16);
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "SubtitleMode" integer;
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "PreferHearingImpairedSubtitles" boolean NOT NULL DEFAULT FALSE;
        """;

    private const string ColumnsSinceRememberedChoices = """
        ALTER TABLE "UserProfiles" DROP COLUMN "PreferredAudioLanguage";
        ALTER TABLE "UserProfiles" DROP COLUMN "PreferredSubtitleLanguage";
        ALTER TABLE "UserProfiles" DROP COLUMN "SubtitleMode";
        ALTER TABLE "UserProfiles" DROP COLUMN "PreferHearingImpairedSubtitles";
        """;

    public const string TrackPreferencesMigration = "20261015010727_ChangesSinceInitial";

    public const string AlbumIndexMigration = "20261015235423_ChangesSinceInitial";

    public const string VideoDimensionsMigration = "20261017000904_ChangesSinceInitial";

    private const string LibrarySortProfileColumns = """
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "LibrarySort" integer;
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "LibrarySortDescending" boolean NOT NULL DEFAULT FALSE;
        """;

    public const string LibrarySortMigration = "20261017011545_ChangesSinceInitial";

    private const string KeepHdrSettingColumns = """
        ALTER TABLE "ServerSettings" ADD COLUMN IF NOT EXISTS "PreserveHdrInTranscodes" boolean NOT NULL DEFAULT TRUE;
        """;

    private const string AutoPlayProfileColumns = """
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "AutoPlayNext" boolean NOT NULL DEFAULT TRUE;
        """;

    public const string HdrTranscodeMigration = "20261017194415_ChangesSinceInitial";

    private const string UpNextProfileColumns = """
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "UpNextOrder" integer NOT NULL DEFAULT 0;
        ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "EpisodeLayout" integer NOT NULL DEFAULT 0;
        """;

    public const string AutoPlayMigration = "20261017231930_ChangesSinceInitial";

    private const string ImageSubtitleDeviceColumns = """
        ALTER TABLE "ClientDevices" ADD COLUMN IF NOT EXISTS "RendersImageSubtitles" boolean;
        """;

    public const string UpNextOrderMigration = "20261018072825_ChangesSinceInitial";

    private const string ColumnsSinceUpNextOrder = """
        ALTER TABLE "ClientDevices" DROP COLUMN "RendersImageSubtitles";
        """;

    private const string ColumnsSinceAutoPlay = """
        ALTER TABLE "UserProfiles" DROP COLUMN "UpNextOrder";
        ALTER TABLE "UserProfiles" DROP COLUMN "EpisodeLayout";
        """ + ColumnsSinceUpNextOrder;

    private const string ColumnsSinceHdrTranscode = """
        ALTER TABLE "UserProfiles" DROP COLUMN "AutoPlayNext";
        """ + ColumnsSinceAutoPlay;

    private const string ColumnsSinceLibrarySort = """
        ALTER TABLE "ServerSettings" DROP COLUMN "PreserveHdrInTranscodes";
        """ + ColumnsSinceHdrTranscode;

    private const string ColumnsSinceVideoDimensions = """
        ALTER TABLE "UserProfiles" DROP COLUMN "LibrarySort";
        ALTER TABLE "UserProfiles" DROP COLUMN "LibrarySortDescending";
        """ + ColumnsSinceLibrarySort;

    private const string ColumnsSinceAlbumIndex = """
        ALTER TABLE "MediaVideoTracks" DROP COLUMN "Width";
        ALTER TABLE "MediaVideoTracks" DROP COLUMN "Height";
        ALTER TABLE "MediaVideoTracks" DROP COLUMN "FrameRate";
        ALTER TABLE "StreamSessions" DROP COLUMN "OutputWidth";
        ALTER TABLE "StreamSessions" DROP COLUMN "OutputHeight";
        """ + ColumnsSinceVideoDimensions;

    private const string IndexesSinceTrackPreferences = """
        DROP INDEX "IX_Albums_ArtistId_Title";
        CREATE UNIQUE INDEX "IX_Albums_ArtistId_Title" ON "Albums" ("ArtistId", "Title");
        """ + ColumnsSinceAlbumIndex;

    private const string SinceRememberedChoices = ColumnsSinceRememberedChoices + IndexesSinceTrackPreferences;

    private const string TablesSinceLocalMetadata = """
        DROP TABLE "ProfileTitlePlaybackChoices";
        """ + SinceRememberedChoices;

    private const string ColumnsSinceStreamOrigin = """
        ALTER TABLE "MediaLibraries" DROP COLUMN "LocalMetadataMode";
        ALTER TABLE "MediaLibraries" DROP COLUMN "PreferLocalArtwork";
        """ + TablesSinceLocalMetadata;

    private const string ColumnsSinceHardwareAcceleration = """
        ALTER TABLE "StreamSessions" DROP COLUMN "IpAddress";
        ALTER TABLE "StreamSessions" DROP COLUMN "Location";
        """ + ColumnsSinceStreamOrigin;

    private const string ColumnsSinceSecondRelease = """
        ALTER TABLE "ServerSettings" DROP COLUMN "HardwareAccelerationType";
        """ + ColumnsSinceHardwareAcceleration;

    private const string ColumnsSinceRelease = """
        ALTER TABLE "MediaItems" DROP COLUMN "FullyRefreshedAt";
        """ + ColumnsSinceSecondRelease;

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
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceRelease, cancellationToken);
        if (!withTaskTable)
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"PendingTasks\"", cancellationToken);
        }

        await ReplaceCombinedMigrationAsync(db, migrationId, "10.0.8", cancellationToken);
    }

    public async Task BuildReleasedStateAsync(CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceRelease, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, ReleasedMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildSecondReleaseStateAsync(CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceSecondRelease, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, SecondReleaseMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildHardwareAccelerationStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceHardwareAcceleration, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, HardwareAccelerationMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildStreamOriginStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceStreamOrigin, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, StreamOriginMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildLocalMetadataStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(TablesSinceLocalMetadata, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, LocalMetadataMigration, "10.0.9", cancellationToken);
    }

    public async Task SeedAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(RememberedChoicesProfileColumns + LibrarySortProfileColumns + KeepHdrSettingColumns + AutoPlayProfileColumns + UpNextProfileColumns + ImageSubtitleDeviceColumns, cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceRememberedChoices + ColumnsSinceVideoDimensions, cancellationToken);
    }

    public async Task BuildRememberedChoicesStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(SinceRememberedChoices, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, RememberedChoicesMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildTrackPreferencesStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(IndexesSinceTrackPreferences, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, TrackPreferencesMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildAlbumIndexStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceAlbumIndex, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, AlbumIndexMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildVideoDimensionsStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceVideoDimensions, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, VideoDimensionsMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildLibrarySortStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceLibrarySort, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, LibrarySortMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildAutoPlayStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceAutoPlay, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, AutoPlayMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildUpNextOrderStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceUpNextOrder, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, UpNextOrderMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildHdrTranscodeStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceHdrTranscode, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, HdrTranscodeMigration, "10.0.9", cancellationToken);
    }

    private static async Task ReplaceCombinedMigrationAsync(VoraDbContext db, string migrationId, string productVersion, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {MigrationHistoryTests.ChangesSinceInitial}",
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {productVersion})",
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
