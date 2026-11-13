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

    private const string LiveVideoChannelColumns = """
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoCodec" character varying(32);
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoProfile" character varying(32);
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoLevel" integer;
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoHeight" integer;
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoFrameRate" double precision;
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoInterlaced" boolean;
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "VideoFormatSeenAt" timestamp with time zone;
        """;

    public const string UpNextOrderMigration = "20261018072825_ChangesSinceInitial";

    public const string ImageSubtitlesMigration = "20261018192130_ChangesSinceInitial";

    public const string ThirdReleaseMigration = "20261020021843_ChangesSinceInitial";

    public const string ChannelVideoFormatMigration = "20261022014009_ChangesSinceInitial";

    private const string ThemeCollectionColumns = """
        ALTER TABLE "MediaItems" ADD COLUMN IF NOT EXISTS "Keywords" text[];
        ALTER TABLE "Collections" ADD COLUMN IF NOT EXISTS "VisibleEveryYear" boolean NOT NULL DEFAULT FALSE;
        """;

    public const string ThemeCollectionsMigration = "20261029013035_ChangesSinceInitial";

    private const string PlaySourceColumns = """
        ALTER TABLE "TrackPlayHistory" ADD COLUMN IF NOT EXISTS "Source" integer NOT NULL DEFAULT 0;
        ALTER TABLE "TrackPlayHistory" ADD COLUMN IF NOT EXISTS "SourceId" uuid;
        """;

    private const string ColumnsSinceThemeCollections = """
        ALTER TABLE "TrackPlayHistory" DROP COLUMN IF EXISTS "Source";
        ALTER TABLE "TrackPlayHistory" DROP COLUMN IF EXISTS "SourceId";
        """ + IndexesSincePlaySource;

    public const string PlaySourceMigration = "20261110034736_ChangesSinceInitial";

    public static readonly string[] HomeRowIndexes = { "IX_MediaItems_PresentByReleaseDate", "IX_MediaItems_PresentByDateAdded" };

    private const string IndexesSincePlaySource = """
        DROP INDEX IF EXISTS "IX_MediaItems_PresentByReleaseDate";
        DROP INDEX IF EXISTS "IX_MediaItems_PresentByDateAdded";
        """ + IndexesSinceHomeRowIndexes;

    public const string HomeRowIndexMigration = "20261111053514_ChangesSinceInitial";

    public static readonly string[] SearchIndexes = { "IX_MediaItems_TitleTrigram", "IX_Actors_NameTrigram", "IX_Artists_NameTrigram", "IX_Albums_TitleTrigram" };

    private const string IndexesSinceHomeRowIndexes = """
        DROP INDEX IF EXISTS "IX_MediaItems_TitleTrigram";
        DROP INDEX IF EXISTS "IX_Actors_NameTrigram";
        DROP INDEX IF EXISTS "IX_Artists_NameTrigram";
        DROP INDEX IF EXISTS "IX_Albums_TitleTrigram";
        DROP EXTENSION IF EXISTS pg_trgm;
        """ + TablesSinceSearchIndexes;

    public const string SearchIndexMigration = "20261111183206_ChangesSinceInitial";

    private const string TablesSinceSearchIndexes = """
        DROP TABLE IF EXISTS "RecommendationSnapshots";
        """ + ColumnsSinceForYouRows;

    public const string ForYouRowsMigration = "20261112020121_ChangesSinceInitial";

    private const string IptvProviderColumns = """
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "SourceType" integer NOT NULL DEFAULT 0;
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "ServerUrl" character varying(1024);
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "Username" character varying(256);
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "PasswordCiphertext" text;
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "UserAgent" character varying(512);
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "Referer" character varying(1024);
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "UseProviderGuide" boolean NOT NULL DEFAULT TRUE;
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "AccountStatus" character varying(32);
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "AccountExpiresAt" timestamp with time zone;
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "AccountMaxConnections" integer;
        ALTER TABLE "IptvPlaylists" ADD COLUMN IF NOT EXISTS "AccountCheckedAt" timestamp with time zone;
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "UserAgent" character varying(512);
        ALTER TABLE "IptvChannels" ADD COLUMN IF NOT EXISTS "Referer" character varying(1024);
        """;

    private const string ColumnsSinceForYouRows = """
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "SourceType";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "ServerUrl";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "Username";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "PasswordCiphertext";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "UserAgent";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "Referer";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "UseProviderGuide";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "AccountStatus";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "AccountExpiresAt";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "AccountMaxConnections";
        ALTER TABLE "IptvPlaylists" DROP COLUMN IF EXISTS "AccountCheckedAt";
        ALTER TABLE "IptvChannels" DROP COLUMN IF EXISTS "UserAgent";
        ALTER TABLE "IptvChannels" DROP COLUMN IF EXISTS "Referer";
        """;

    public const string IptvProvidersMigration = "20261112032459_ChangesSinceInitial";

    private const string ColumnsSinceChannelVideoFormat = """
        DROP TABLE IF EXISTS "CollectionThemeVerdicts";
        DROP TABLE IF EXISTS "CollectionThemes";
        ALTER TABLE "MediaItems" DROP COLUMN IF EXISTS "Keywords";
        ALTER TABLE "Collections" DROP COLUMN IF EXISTS "VisibleEveryYear";
        """ + ColumnsSinceThemeCollections;

    private const string ColumnsSinceThirdRelease = """
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoCodec";
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoProfile";
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoLevel";
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoHeight";
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoFrameRate";
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoInterlaced";
        ALTER TABLE "IptvChannels" DROP COLUMN "VideoFormatSeenAt";
        """ + ColumnsSinceChannelVideoFormat;

    private const string ColumnsSinceUpNextOrder = """
        ALTER TABLE "ClientDevices" DROP COLUMN "RendersImageSubtitles";
        """ + ColumnsSinceThirdRelease;

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
        await db.Database.ExecuteSqlRawAsync(RememberedChoicesProfileColumns + LibrarySortProfileColumns + KeepHdrSettingColumns + AutoPlayProfileColumns + UpNextProfileColumns + ImageSubtitleDeviceColumns + LiveVideoChannelColumns + ThemeCollectionColumns + PlaySourceColumns + IptvProviderColumns, cancellationToken);
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

    public async Task BuildImageSubtitlesStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceThirdRelease, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, ImageSubtitlesMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildThirdReleaseStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceThirdRelease, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, ThirdReleaseMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildChannelVideoFormatStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceChannelVideoFormat, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, ChannelVideoFormatMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildThemeCollectionsStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceThemeCollections, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, ThemeCollectionsMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildPlaySourceStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(IndexesSincePlaySource, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, PlaySourceMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildHomeRowIndexStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(IndexesSinceHomeRowIndexes, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, HomeRowIndexMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildSearchIndexStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(TablesSinceSearchIndexes, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, SearchIndexMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildForYouRowsStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await db.Database.ExecuteSqlRawAsync(ColumnsSinceForYouRows, cancellationToken);
        await ReplaceCombinedMigrationAsync(db, ForYouRowsMigration, "10.0.9", cancellationToken);
    }

    public async Task BuildIptvProvidersStateAsync(Func<VoraDbContext, Task> seed, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);
        await seed(db);
        await ReplaceCombinedMigrationAsync(db, IptvProvidersMigration, "10.0.9", cancellationToken);
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
