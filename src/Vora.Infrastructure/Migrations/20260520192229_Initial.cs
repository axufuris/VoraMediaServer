using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Actors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Biography = table.Column<string>(type: "text", nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HomePage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Birthday = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Deathday = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    ImdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsCustom = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Actors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArtistSimilarities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                    SimilarArtistName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistSimilarities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArtistTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperatingSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Location = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    MaxAudioChannels = table.Column<int>(type: "integer", nullable: false),
                    SupportedVideoCodecs = table.Column<string>(type: "text", nullable: false),
                    SupportedAudioCodecs = table.Column<string>(type: "text", nullable: false),
                    SupportedContainers = table.Column<string>(type: "text", nullable: false),
                    LastUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTemplateSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTemplateSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PosterUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    BackdropUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TmdbId = table.Column<int>(type: "integer", nullable: true),
                    ImdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TvdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SystemGenerated = table.Column<bool>(type: "boolean", nullable: false),
                    AutoSyncChronology = table.Column<bool>(type: "boolean", nullable: false),
                    SortProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalListId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ContentSyncProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContentSyncExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DefaultSort = table.Column<int>(type: "integer", nullable: false),
                    VisibleStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VisibleEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedFields = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LogoPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OriginCountry = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Iso3166_1 = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Iso3166_1);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryRowConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RowId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryRowConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IptvEpgSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    XmlTvUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvEpgSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IptvPlaylists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    M3uUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsWebPlayback = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DefaultChannelKind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvPlaylists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaLibraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FolderPaths = table.Column<string>(type: "text", nullable: false),
                    ScannerRegex = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MetadataProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtworkProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ThirdPartyRating1ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ThirdPartyRating2ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EnableRealTimeWatching = table.Column<bool>(type: "boolean", nullable: false),
                    UseLocalAssets = table.Column<bool>(type: "boolean", nullable: false),
                    FindExtras = table.Column<bool>(type: "boolean", nullable: false),
                    OnlyShowTrailers = table.Column<bool>(type: "boolean", nullable: false),
                    EnableVideoPreviewThumbnails = table.Column<bool>(type: "boolean", nullable: false),
                    CollectionDisplay = table.Column<int>(type: "integer", nullable: false),
                    MinimumCollectionSize = table.Column<int>(type: "integer", nullable: false),
                    EnableCreditsDetection = table.Column<bool>(type: "boolean", nullable: false),
                    EnableVoiceActivityDetection = table.Column<bool>(type: "boolean", nullable: false),
                    EnableIntroDetection = table.Column<bool>(type: "boolean", nullable: false),
                    EpisodeSorting = table.Column<int>(type: "integer", nullable: false),
                    EpisodeOrder = table.Column<int>(type: "integer", nullable: false),
                    SeasonsDisplay = table.Column<int>(type: "integer", nullable: false),
                    UseSeasonTitles = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Networks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LogoPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OriginCountry = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Networks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OverlayTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetMediaType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetLibraryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlayTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PodcastShows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    HomepageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsInCatalog = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastShows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    UseSsl = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UrlBase = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Is4K = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderSettingsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RegistrationMode = table.Column<int>(type: "integer", nullable: false),
                    TmdbApiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TvdbApiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TvdbToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    EnableNightlyScan = table.Column<bool>(type: "boolean", nullable: false),
                    NightlyScanTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RunDetections = table.Column<int>(type: "integer", nullable: false),
                    DetectionScheduleTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    IptvSyncTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    FolderWatcherProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FolderWatcherPollingInterval = table.Column<int>(type: "integer", nullable: false),
                    LocalMediaScannerProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnableRemoteAccess = table.Column<bool>(type: "boolean", nullable: false),
                    ManuallySpecifyPublicPort = table.Column<bool>(type: "boolean", nullable: false),
                    PublicPort = table.Column<int>(type: "integer", nullable: false),
                    InternetUploadSpeedMbps = table.Column<int>(type: "integer", nullable: false),
                    MaxRemoteStreamBitrateMbps = table.Column<int>(type: "integer", nullable: false),
                    StreamingProfile = table.Column<int>(type: "integer", nullable: false),
                    DisableVideoTranscoding = table.Column<bool>(type: "boolean", nullable: false),
                    UseHardwareAcceleration = table.Column<bool>(type: "boolean", nullable: false),
                    UseHardwareEncoding = table.Column<bool>(type: "boolean", nullable: false),
                    HardwareTranscodingDevice = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TranscodeQuality = table.Column<int>(type: "integer", nullable: false),
                    BackgroundX264Preset = table.Column<int>(type: "integer", nullable: false),
                    EnableHevcEncoding = table.Column<int>(type: "integer", nullable: false),
                    EnableHevcOptimization = table.Column<bool>(type: "boolean", nullable: false),
                    EnableHdrToneMapping = table.Column<bool>(type: "boolean", nullable: false),
                    TonemappingAlgorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TranscoderTempDirectory = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TranscoderThrottleBuffer = table.Column<int>(type: "integer", nullable: false),
                    MaxGpuTranscodes = table.Column<int>(type: "integer", nullable: false),
                    MaxCpuTranscodes = table.Column<int>(type: "integer", nullable: false),
                    MaxBackgroundTranscodes = table.Column<int>(type: "integer", nullable: false),
                    EnableDailyMixes = table.Column<bool>(type: "boolean", nullable: false),
                    DailyMixSchedule = table.Column<string>(type: "text", nullable: false),
                    DailyMixCount = table.Column<int>(type: "integer", nullable: false),
                    DailyMixSize = table.Column<int>(type: "integer", nullable: false),
                    DailyMixDriftPercent = table.Column<int>(type: "integer", nullable: false),
                    DailyMixMinPlays = table.Column<int>(type: "integer", nullable: false),
                    DailyMixLastRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnableWeeklyMixes = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyMixLastRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnableDiscover = table.Column<bool>(type: "boolean", nullable: false),
                    EnableForYou = table.Column<bool>(type: "boolean", nullable: false),
                    EnableReleaseCalendar = table.Column<bool>(type: "boolean", nullable: false),
                    EnableLiveTv = table.Column<bool>(type: "boolean", nullable: false),
                    EnableDvr = table.Column<bool>(type: "boolean", nullable: false),
                    EnableInternetRadio = table.Column<bool>(type: "boolean", nullable: false),
                    EnablePodcasts = table.Column<bool>(type: "boolean", nullable: false),
                    DvrStoragePath = table.Column<string>(type: "text", nullable: true),
                    DvrMaxStorageGb = table.Column<long>(type: "bigint", nullable: false),
                    DvrStorageWarningPercent = table.Column<int>(type: "integer", nullable: false),
                    DvrAutoDeleteWatchedDays = table.Column<int>(type: "integer", nullable: false),
                    DvrDefaultSeriesRetention = table.Column<int>(type: "integer", nullable: false),
                    DvrNotifyOnFailure = table.Column<bool>(type: "boolean", nullable: false),
                    DvrNotifyOnStorageThreshold = table.Column<bool>(type: "boolean", nullable: false),
                    DvrPreRollSeconds = table.Column<int>(type: "integer", nullable: false),
                    DvrPostRollSeconds = table.Column<int>(type: "integer", nullable: false),
                    DvrConflictPolicy = table.Column<int>(type: "integer", nullable: false),
                    AdminThemeId = table.Column<string>(type: "text", nullable: false),
                    DefaultClientTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveStreams = table.Column<int>(type: "integer", nullable: false),
                    ActiveTranscodes = table.Column<int>(type: "integer", nullable: false),
                    CpuUsagePercentage = table.Column<double>(type: "double precision", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    HasAllLibraryAccess = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedLibraryIds = table.Column<string>(type: "text", nullable: false),
                    HasAllIptvAccess = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedIptvPlaylistIds = table.Column<string>(type: "text", nullable: false),
                    CanRequestMedia = table.Column<bool>(type: "boolean", nullable: false),
                    AutoApproveRequests = table.Column<bool>(type: "boolean", nullable: false),
                    EnableAiRecommendations = table.Column<bool>(type: "boolean", nullable: false),
                    CanRecordLiveTv = table.Column<bool>(type: "boolean", nullable: false),
                    DvrStorageQuotaBytes = table.Column<long>(type: "bigint", nullable: false),
                    CanTimeshiftIptv = table.Column<bool>(type: "boolean", nullable: false),
                    CanAddCustomPodcastFeeds = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserWatchlistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PosterUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpectedReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWatchlistItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SubscribedEvents = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionArtwork",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    VoteAverage = table.Column<double>(type: "double precision", nullable: true),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsUserUploaded = table.Column<bool>(type: "boolean", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionArtwork", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionArtwork_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IptvChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalChannelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StreamUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    GroupTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    IsHiddenByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    KindOverriddenByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IptvChannels_IptvPlaylists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "IptvPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IptvTunerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxConcurrentStreams = table.Column<int>(type: "integer", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvTunerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IptvTunerProfiles_IptvPlaylists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "IptvPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Biography = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BackgroundUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BannerUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ClearLogoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedFields = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artists_MediaLibraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "MediaLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaDedupeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: true),
                    GroupAcrossResolutions = table.Column<bool>(type: "boolean", nullable: false),
                    RuntimeToleranceSeconds = table.Column<int>(type: "integer", nullable: false),
                    MinimumFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MinimumRuntimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    ScoreResolution4k = table.Column<int>(type: "integer", nullable: false),
                    ScoreResolution1080 = table.Column<int>(type: "integer", nullable: false),
                    ScoreResolution720 = table.Column<int>(type: "integer", nullable: false),
                    ScoreResolutionOther = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecAv1 = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecHevc = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecVp9 = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecH264 = table.Column<int>(type: "integer", nullable: false),
                    ScoreHdrDolbyVision = table.Column<int>(type: "integer", nullable: false),
                    ScoreHdr = table.Column<int>(type: "integer", nullable: false),
                    ScoreAudioLossless = table.Column<int>(type: "integer", nullable: false),
                    ScoreAudioSurround = table.Column<int>(type: "integer", nullable: false),
                    ScoreAudioBase = table.Column<int>(type: "integer", nullable: false),
                    ScoreBitrateDivisor = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecMusicLossless = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecMusicLossyHigh = table.Column<int>(type: "integer", nullable: false),
                    ScoreCodecMusicLossyStandard = table.Column<int>(type: "integer", nullable: false),
                    ScoreSampleRateHi = table.Column<int>(type: "integer", nullable: false),
                    ScoreSampleRateStandard = table.Column<int>(type: "integer", nullable: false),
                    ScoreSampleRateLow = table.Column<int>(type: "integer", nullable: false),
                    ScoreFileSizeDivisor = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaDedupeSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaDedupeSettings_MediaLibraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "MediaLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmartLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FilterRulesJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    SortBy = table.Column<int>(type: "integer", nullable: false),
                    MaxItems = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ShowOnHomepage = table.Column<bool>(type: "boolean", nullable: false),
                    ShowToFriends = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystemList = table.Column<bool>(type: "boolean", nullable: false),
                    IsSpotlight = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveStartMonth = table.Column<int>(type: "integer", nullable: true),
                    ActiveStartDay = table.Column<int>(type: "integer", nullable: true),
                    ActiveEndMonth = table.Column<int>(type: "integer", nullable: true),
                    ActiveEndDay = table.Column<int>(type: "integer", nullable: true),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartLists_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SmartLists_MediaLibraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "MediaLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodcastEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PodcastShowId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalGuid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    AudioUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodes_PodcastShows_PodcastShowId",
                        column: x => x.PodcastShowId,
                        principalTable: "PodcastShows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PosterUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpectedReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedServerId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRequests_RequestServers_AssignedServerId",
                        column: x => x.AssignedServerId,
                        principalTable: "RequestServers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileImageUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PinHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    HasAllLibraryAccess = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedLibraryIds = table.Column<string>(type: "text", nullable: false),
                    HasAllIptvAccess = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedIptvPlaylistIds = table.Column<string>(type: "text", nullable: false),
                    BlockUnratedContent = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedMovieRatings = table.Column<string>(type: "text", nullable: false),
                    AllowedTvRatings = table.Column<string>(type: "text", nullable: false),
                    AllowedMusicRatings = table.Column<string>(type: "text", nullable: false),
                    AutoApproveRequests = table.Column<bool>(type: "boolean", nullable: false),
                    CanRecordLiveTv = table.Column<bool>(type: "boolean", nullable: false),
                    CanAddCustomPodcastFeeds = table.Column<bool>(type: "boolean", nullable: false),
                    LastFmSessionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastFmUsername = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClientTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScheduleOverrideTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScheduleOverrideScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProviderConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProviderConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProviderConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Genre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BackgroundUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DiscArtUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AlbumArtist = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCompilation = table.Column<bool>(type: "boolean", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedFields = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Albums_MediaLibraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "MediaLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelUsed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsageLogs_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedMixes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastDriftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrackOrder = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedMixes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedMixes_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IptvRecordingSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProgramId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsSeriesRecording = table.Column<bool>(type: "boolean", nullable: false),
                    KeepMaxEpisodes = table.Column<int>(type: "integer", nullable: false),
                    DeleteAfterWatching = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvRecordingSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IptvRecordingSchedules_IptvChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "IptvChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IptvRecordingSchedules_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IptvRecordingSchedules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaRequestUsers",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRequestUsers", x => new { x.RequestId, x.ProfileId });
                    table.ForeignKey(
                        name: "FK_MediaRequestUsers_MediaRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "MediaRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaRequestUsers_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playlists_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodcastEpisodeProfileStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PodcastEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsPlayed = table.Column<bool>(type: "boolean", nullable: false),
                    LastListenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastEpisodeProfileStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodeProfileStates_PodcastEpisodes_PodcastEpisodeId",
                        column: x => x.PodcastEpisodeId,
                        principalTable: "PodcastEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodeProfileStates_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodcastSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PodcastShowId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PodcastSubscriptions_PodcastShows_PodcastShowId",
                        column: x => x.PodcastShowId,
                        principalTable: "PodcastShows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PodcastSubscriptions_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileAccessSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileAccessSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileAccessSchedules_UserProfiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileDeviceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NavPrefsJson = table.Column<string>(type: "text", nullable: false),
                    PlaybackPrefs = table.Column<string>(type: "text", nullable: false),
                    IptvPrefsJson = table.Column<string>(type: "text", nullable: false),
                    RadioPrefsJson = table.Column<string>(type: "text", nullable: false),
                    DiscoveryLayoutJson = table.Column<string>(type: "text", nullable: false),
                    HomeLayoutJson = table.Column<string>(type: "text", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileDeviceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileDeviceSettings_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmartPlaylists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ArtworkUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MediaType = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    RulesJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    Limit = table.Column<int>(type: "integer", nullable: true),
                    SortBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortDirection = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartPlaylists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartPlaylists_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeedKind = table.Column<int>(type: "integer", nullable: false),
                    SeedArtistId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeedTrackId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeedGenre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stations_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginalTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginalLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Overview = table.Column<string>(type: "text", nullable: true),
                    Tagline = table.Column<string>(type: "text", nullable: true),
                    Edition = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HomePage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ContentRating = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsAdult = table.Column<bool>(type: "boolean", nullable: false),
                    TmdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TvdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ServerAdminRating = table.Column<decimal>(type: "numeric", nullable: true),
                    ThirdPartyRating1 = table.Column<decimal>(type: "numeric", nullable: true),
                    ThirdPartyRating1Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ThirdPartyRating2 = table.Column<decimal>(type: "numeric", nullable: true),
                    ThirdPartyRating2Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PosterUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OriginalPosterUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    BackgroundUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HasMidCreditsStinger = table.Column<bool>(type: "boolean", nullable: false),
                    HasPostCreditsStinger = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMetadataRefresh = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOverlayGeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovieGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Budget = table.Column<long>(type: "bigint", nullable: true),
                    Revenue = table.Column<long>(type: "bigint", nullable: true),
                    TheatricalReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DigitalReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    EpisodeCount = table.Column<int>(type: "integer", nullable: true),
                    VoteAverage = table.Column<decimal>(type: "numeric", nullable: true),
                    TvShowId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: true),
                    Artist = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TrackNumber = table.Column<int>(type: "integer", nullable: true),
                    DiscNumber = table.Column<int>(type: "integer", nullable: true),
                    AudioCodec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SampleRate = table.Column<int>(type: "integer", nullable: true),
                    Bitrate = table.Column<int>(type: "integer", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    HasEmbeddedLyrics = table.Column<bool>(type: "boolean", nullable: true),
                    ExternalLyricsPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TvType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InProduction = table.Column<bool>(type: "boolean", nullable: true),
                    NumberOfSeasons = table.Column<int>(type: "integer", nullable: true),
                    NumberOfEpisodes = table.Column<int>(type: "integer", nullable: true),
                    LastAirDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAirDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastEpisodeToAirName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NextEpisodeToAirName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpcomingEpisodesJson = table.Column<string>(type: "text", nullable: true),
                    LockedFields = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaItems_MediaItems_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaItems_MediaItems_TvShowId",
                        column: x => x.TvShowId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaItems_MediaLibraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "MediaLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IptvRecordingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EpisodeTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    ExternalProgramId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    OutputFilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CommercialMarkersJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvRecordingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IptvRecordingSessions_IptvRecordingSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "IptvRecordingSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionItems",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<decimal>(type: "numeric", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItems", x => new { x.CollectionId, x.MediaItemId });
                    table.ForeignKey(
                        name: "FK_CollectionItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaArtwork",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    VoteAverage = table.Column<double>(type: "double precision", nullable: true),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsUserUploaded = table.Column<bool>(type: "boolean", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaArtwork", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaArtwork_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaCastMembers",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Roles = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaCastMembers", x => new { x.ActorId, x.MediaItemId });
                    table.ForeignKey(
                        name: "FK_MediaCastMembers_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaCastMembers_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaDedupeIgnoredGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IgnoredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IgnoredByProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaDedupeIgnoredGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaDedupeIgnoredGroups_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItemAnalysis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    IntroStart = table.Column<TimeSpan>(type: "interval", nullable: true),
                    IntroEnd = table.Column<TimeSpan>(type: "interval", nullable: true),
                    CreditsStart = table.Column<TimeSpan>(type: "interval", nullable: true),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItemAnalysis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItemAnalysis_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItemCompanies",
                columns: table => new
                {
                    MediaItemsId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionCompaniesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItemCompanies", x => new { x.MediaItemsId, x.ProductionCompaniesId });
                    table.ForeignKey(
                        name: "FK_MediaItemCompanies_Companies_ProductionCompaniesId",
                        column: x => x.ProductionCompaniesId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaItemCompanies_MediaItems_MediaItemsId",
                        column: x => x.MediaItemsId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItemCountries",
                columns: table => new
                {
                    MediaItemsId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginCountriesIso3166_1 = table.Column<string>(type: "character varying(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItemCountries", x => new { x.MediaItemsId, x.OriginCountriesIso3166_1 });
                    table.ForeignKey(
                        name: "FK_MediaItemCountries_Countries_OriginCountriesIso3166_1",
                        column: x => x.OriginCountriesIso3166_1,
                        principalTable: "Countries",
                        principalColumn: "Iso3166_1",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaItemCountries_MediaItems_MediaItemsId",
                        column: x => x.MediaItemsId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItemEmbeddings",
                columns: table => new
                {
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItemEmbeddings", x => x.MediaItemId);
                    table.ForeignKey(
                        name: "FK_MediaItemEmbeddings_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItemGenres",
                columns: table => new
                {
                    GenresId = table.Column<int>(type: "integer", nullable: false),
                    MediaItemsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItemGenres", x => new { x.GenresId, x.MediaItemsId });
                    table.ForeignKey(
                        name: "FK_MediaItemGenres_Genres_GenresId",
                        column: x => x.GenresId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaItemGenres_MediaItems_MediaItemsId",
                        column: x => x.MediaItemsId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    VersionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PartNumber = table.Column<int>(type: "integer", nullable: false),
                    Container = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    OverallBitrate = table.Column<long>(type: "bigint", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaParts_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Site = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaVideos_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StreamSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VideoStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AudioStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubtitleStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VideoCodec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AudioCodec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Container = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    HdrType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TargetAudioChannels = table.Column<int>(type: "integer", nullable: false),
                    IsSubtitleBurnIn = table.Column<bool>(type: "boolean", nullable: false),
                    BandwidthKbps = table.Column<int>(type: "integer", nullable: false),
                    Quality = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionLog = table.Column<string>(type: "text", nullable: false),
                    StartPosition = table.Column<double>(type: "double precision", nullable: false),
                    CurrentPosition = table.Column<double>(type: "double precision", nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    TotalPausedDuration = table.Column<double>(type: "double precision", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaPartId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudioTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubtitleTrackId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientDeviceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StreamSessions_ClientDevices_ClientDeviceId",
                        column: x => x.ClientDeviceId,
                        principalTable: "ClientDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StreamSessions_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StreamSessions_UserProfiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StreamSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackLikes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    LikedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackLikes_MediaItems_TrackId",
                        column: x => x.TrackId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackPlayHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationListenedSeconds = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackPlayHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackPlayHistory_MediaItems_TrackId",
                        column: x => x.TrackId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TvShowNetworks",
                columns: table => new
                {
                    NetworksId = table.Column<int>(type: "integer", nullable: false),
                    TvShowsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TvShowNetworks", x => new { x.NetworksId, x.TvShowsId });
                    table.ForeignKey(
                        name: "FK_TvShowNetworks_MediaItems_TvShowsId",
                        column: x => x.TvShowsId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TvShowNetworks_Networks_NetworksId",
                        column: x => x.NetworksId,
                        principalTable: "Networks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMediaStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumePositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsPlayed = table.Column<bool>(type: "boolean", nullable: false),
                    IsHiddenFromContinueWatching = table.Column<bool>(type: "boolean", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMediaStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMediaStates_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserMediaStates_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAudioTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamIndex = table.Column<int>(type: "integer", nullable: false),
                    Codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Channels = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    MediaPartId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAudioTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAudioTracks_MediaParts_MediaPartId",
                        column: x => x.MediaPartId,
                        principalTable: "MediaParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaSubtitleTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamIndex = table.Column<int>(type: "integer", nullable: false),
                    Codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsForced = table.Column<bool>(type: "boolean", nullable: false),
                    MediaPartId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaSubtitleTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaSubtitleTracks_MediaParts_MediaPartId",
                        column: x => x.MediaPartId,
                        principalTable: "MediaParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaVideoTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamIndex = table.Column<int>(type: "integer", nullable: false),
                    Codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Profile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HdrType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BitDepth = table.Column<int>(type: "integer", nullable: true),
                    Bitrate = table.Column<long>(type: "bigint", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    MediaPartId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaVideoTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaVideoTracks_MediaParts_MediaPartId",
                        column: x => x.MediaPartId,
                        principalTable: "MediaParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 12, "Adventure" },
                    { 14, "Fantasy" },
                    { 16, "Animation" },
                    { 18, "Drama" },
                    { 27, "Horror" },
                    { 28, "Action" },
                    { 35, "Comedy" },
                    { 36, "History" },
                    { 37, "Western" },
                    { 53, "Thriller" },
                    { 80, "Crime" },
                    { 99, "Documentary" },
                    { 878, "Science Fiction" },
                    { 9648, "Mystery" },
                    { 10402, "Music" },
                    { 10749, "Romance" },
                    { 10751, "Family" },
                    { 10752, "War" },
                    { 10759, "Action & Adventure" },
                    { 10762, "Kids" },
                    { 10763, "News" },
                    { 10764, "Reality" },
                    { 10765, "Sci-Fi & Fantasy" },
                    { 10766, "Soap" },
                    { 10767, "Talk" },
                    { 10768, "War & Politics" },
                    { 10770, "TV Movie" }
                });

            migrationBuilder.InsertData(
                table: "ServerSettings",
                columns: new[] { "Id", "AdminThemeId", "BackgroundX264Preset", "DailyMixCount", "DailyMixDriftPercent", "DailyMixLastRefreshedAt", "DailyMixMinPlays", "DailyMixSchedule", "DailyMixSize", "DefaultClientTemplateId", "DetectionScheduleTime", "DisableVideoTranscoding", "DvrAutoDeleteWatchedDays", "DvrConflictPolicy", "DvrDefaultSeriesRetention", "DvrMaxStorageGb", "DvrNotifyOnFailure", "DvrNotifyOnStorageThreshold", "DvrPostRollSeconds", "DvrPreRollSeconds", "DvrStoragePath", "DvrStorageWarningPercent", "EnableDailyMixes", "EnableDiscover", "EnableDvr", "EnableForYou", "EnableHdrToneMapping", "EnableHevcEncoding", "EnableHevcOptimization", "EnableInternetRadio", "EnableLiveTv", "EnableNightlyScan", "EnablePodcasts", "EnableReleaseCalendar", "EnableRemoteAccess", "EnableWeeklyMixes", "FolderWatcherPollingInterval", "FolderWatcherProviderId", "HardwareTranscodingDevice", "InternetUploadSpeedMbps", "IptvSyncTime", "LocalMediaScannerProviderId", "ManuallySpecifyPublicPort", "MaxBackgroundTranscodes", "MaxCpuTranscodes", "MaxGpuTranscodes", "MaxRemoteStreamBitrateMbps", "NightlyScanTime", "PublicPort", "RegistrationMode", "RunDetections", "ServerName", "StreamingProfile", "TmdbApiKey", "TonemappingAlgorithm", "TranscodeQuality", "TranscoderTempDirectory", "TranscoderThrottleBuffer", "TvdbApiKey", "TvdbToken", "UseHardwareAcceleration", "UseHardwareEncoding", "WeeklyMixLastRefreshedAt" },
                values: new object[] { "GLOBAL_SETTINGS", "vora-default", 2, 6, 20, null, 50, "Daily3am", 50, "vora-cinema", new TimeSpan(0, 3, 0, 0, 0), false, 0, 0, 0, 0L, true, true, 300, 120, null, 90, true, true, true, true, true, 1, true, true, true, false, true, true, true, true, 30, "polling_watcher", "Auto", 1000, new TimeSpan(0, 4, 0, 0, 0), "Vora_scanner", false, 0, 0, 2, 0, new TimeSpan(0, 2, 0, 0, 0), 32080, 2, 0, "Vora Server", 0, "37627bd54505a2f5f83df81303bc1eaa", "hable", 0, "/transcode", 60, "56c51421-6057-4825-bc6f-a550198c8fcc", null, true, true, null });

            migrationBuilder.InsertData(
                table: "SmartLists",
                columns: new[] { "Id", "ActiveEndDay", "ActiveEndMonth", "ActiveStartDay", "ActiveStartMonth", "CollectionId", "DisplayOrder", "FilterRulesJson", "IsSpotlight", "IsSystemList", "LibraryId", "MaxItems", "ShowOnHomepage", "ShowToFriends", "SortBy", "Title" },
                values: new object[,]
                {
                    { new Guid("17ddede2-2de0-42b8-9b33-32708b4d29b8"), null, null, null, null, null, 1, "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\"]}", false, true, null, 20, true, true, 0, "Recently Added Movies & Shows" },
                    { new Guid("58424b85-b6da-4a9c-8204-e364f1319508"), null, null, null, null, null, 4, "{\"mediaTypes\":[\"TvShow\"]}", false, true, null, 20, true, true, 1, "Recently Released Shows" },
                    { new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"), null, null, null, null, null, 0, "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\"]}", false, true, null, 20, true, true, 1, "Recently Released Movies & Shows" },
                    { new Guid("c88d6c8a-57ea-4b24-a7be-3f2638a38aca"), null, null, null, null, null, 3, "{\"mediaTypes\":[\"Movie\"]}", false, true, null, 20, true, true, 0, "Recently Added Movies" },
                    { new Guid("dfc420d4-421c-4e14-aec4-a5bedefd2f2e"), null, null, null, null, null, 5, "{\"mediaTypes\":[\"TvShow\"]}", false, true, null, 20, true, true, 0, "Recently Added Shows" },
                    { new Guid("ebbefd92-4232-4cae-9c5d-2134943b8bf8"), null, null, null, null, null, 2, "{\"mediaTypes\":[\"Movie\"]}", false, true, null, 20, true, true, 1, "Recently Released Movies" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageLogs_ProfileId",
                table: "AiUsageLogs",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ArtistId_Title",
                table: "Albums",
                columns: new[] { "ArtistId", "Title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Albums_LibraryId",
                table: "Albums",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_LibraryId_Name",
                table: "Artists",
                columns: new[] { "LibraryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSimilarities_ArtistId",
                table: "ArtistSimilarities",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistTags_ArtistId",
                table: "ArtistTags",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistTags_Tag",
                table: "ArtistTags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_ClientDevices_DeviceId",
                table: "ClientDevices",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTemplateSchedules_Enabled_StartsAtUtc_EndsAtUtc",
                table: "ClientTemplateSchedules",
                columns: new[] { "Enabled", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTemplateSchedules_TemplateId",
                table: "ClientTemplateSchedules",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionArtwork_CollectionId",
                table: "CollectionArtwork",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_MediaItemId",
                table: "CollectionItems",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryRowConfigs_OrderIndex",
                table: "DiscoveryRowConfigs",
                column: "OrderIndex");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedMixes_ProfileId",
                table: "GeneratedMixes",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedMixes_ProfileId_Kind_Slot",
                table: "GeneratedMixes",
                columns: new[] { "ProfileId", "Kind", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_ExternalChannelId",
                table: "IptvChannels",
                column: "ExternalChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_PlaylistId",
                table: "IptvChannels",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvRecordingSchedules_ChannelId",
                table: "IptvRecordingSchedules",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvRecordingSchedules_ProfileId",
                table: "IptvRecordingSchedules",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvRecordingSchedules_UserId",
                table: "IptvRecordingSchedules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvRecordingSessions_ScheduleId",
                table: "IptvRecordingSessions",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvTunerProfiles_PlaylistId",
                table: "IptvTunerProfiles",
                column: "PlaylistId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaArtwork_MediaItemId",
                table: "MediaArtwork",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAudioTracks_MediaPartId",
                table: "MediaAudioTracks",
                column: "MediaPartId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaCastMembers_MediaItemId",
                table: "MediaCastMembers",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaDedupeIgnoredGroups_MediaItemId_Resolution",
                table: "MediaDedupeIgnoredGroups",
                columns: new[] { "MediaItemId", "Resolution" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaDedupeSettings_LibraryId",
                table: "MediaDedupeSettings",
                column: "LibraryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItemAnalysis_MediaItemId",
                table: "MediaItemAnalysis",
                column: "MediaItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItemCompanies_ProductionCompaniesId",
                table: "MediaItemCompanies",
                column: "ProductionCompaniesId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItemCountries_OriginCountriesIso3166_1",
                table: "MediaItemCountries",
                column: "OriginCountriesIso3166_1");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItemGenres_MediaItemsId",
                table: "MediaItemGenres",
                column: "MediaItemsId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_AlbumId",
                table: "MediaItems",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_LibraryId",
                table: "MediaItems",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_SeasonId",
                table: "MediaItems",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_TvShowId",
                table: "MediaItems",
                column: "TvShowId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaParts_MediaItemId",
                table: "MediaParts",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_AssignedServerId",
                table: "MediaRequests",
                column: "AssignedServerId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequestUsers_ProfileId",
                table: "MediaRequestUsers",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaSubtitleTracks_MediaPartId",
                table: "MediaSubtitleTracks",
                column: "MediaPartId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaVideos_MediaItemId",
                table: "MediaVideos",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaVideoTracks_MediaPartId",
                table: "MediaVideoTracks",
                column: "MediaPartId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_MediaItemId",
                table: "PlaylistItems",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId_Order",
                table: "PlaylistItems",
                columns: new[] { "PlaylistId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_ProfileId",
                table: "Playlists",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PluginSettings_PluginId_Key",
                table: "PluginSettings",
                columns: new[] { "PluginId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodeProfileStates_PodcastEpisodeId",
                table: "PodcastEpisodeProfileStates",
                column: "PodcastEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodeProfileStates_ProfileId_PodcastEpisodeId",
                table: "PodcastEpisodeProfileStates",
                columns: new[] { "ProfileId", "PodcastEpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_PodcastShowId_ExternalGuid",
                table: "PodcastEpisodes",
                columns: new[] { "PodcastShowId", "ExternalGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastShows_FeedUrl",
                table: "PodcastShows",
                column: "FeedUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastSubscriptions_PodcastShowId",
                table: "PodcastSubscriptions",
                column: "PodcastShowId");

            migrationBuilder.CreateIndex(
                name: "IX_PodcastSubscriptions_ProfileId_PodcastShowId",
                table: "PodcastSubscriptions",
                columns: new[] { "ProfileId", "PodcastShowId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileAccessSchedules_UserProfileId",
                table: "ProfileAccessSchedules",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileDeviceSettings_ProfileId_DeviceId",
                table: "ProfileDeviceSettings",
                columns: new[] { "ProfileId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationTickets_SecretCode",
                table: "RegistrationTickets",
                column: "SecretCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartLists_CollectionId",
                table: "SmartLists",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartLists_LibraryId",
                table: "SmartLists",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlaylists_ProfileId",
                table: "SmartPlaylists",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_ProfileId",
                table: "Stations",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_ProfileId_LastPlayedAt",
                table: "Stations",
                columns: new[] { "ProfileId", "LastPlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StreamSessions_ClientDeviceId",
                table: "StreamSessions",
                column: "ClientDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_StreamSessions_MediaItemId",
                table: "StreamSessions",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StreamSessions_UserId",
                table: "StreamSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StreamSessions_UserProfileId",
                table: "StreamSessions",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackLikes_ProfileId",
                table: "TrackLikes",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackLikes_ProfileId_TrackId",
                table: "TrackLikes",
                columns: new[] { "ProfileId", "TrackId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackLikes_TrackId",
                table: "TrackLikes",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackPlayHistory_ProfileId_PlayedAt",
                table: "TrackPlayHistory",
                columns: new[] { "ProfileId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackPlayHistory_ProfileId_TrackId",
                table: "TrackPlayHistory",
                columns: new[] { "ProfileId", "TrackId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackPlayHistory_TrackId",
                table: "TrackPlayHistory",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TvShowNetworks_TvShowsId",
                table: "TvShowNetworks",
                column: "TvShowsId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMediaStates_MediaItemId",
                table: "UserMediaStates",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMediaStates_ProfileId_MediaItemId",
                table: "UserMediaStates",
                columns: new[] { "ProfileId", "MediaItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UserId",
                table: "UserProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProviderConnections_UserId",
                table: "UserProviderConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWatchlistItems_ProfileId",
                table: "UserWatchlistItems",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_UserWatchlistItems_ProfileId_ExternalId_ProviderId",
                table: "UserWatchlistItems",
                columns: new[] { "ProfileId", "ExternalId", "ProviderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminNotifications");

            migrationBuilder.DropTable(
                name: "AiUsageLogs");

            migrationBuilder.DropTable(
                name: "ArtistSimilarities");

            migrationBuilder.DropTable(
                name: "ArtistTags");

            migrationBuilder.DropTable(
                name: "ClientTemplateSchedules");

            migrationBuilder.DropTable(
                name: "CollectionArtwork");

            migrationBuilder.DropTable(
                name: "CollectionItems");

            migrationBuilder.DropTable(
                name: "DiscoveryRowConfigs");

            migrationBuilder.DropTable(
                name: "GeneratedMixes");

            migrationBuilder.DropTable(
                name: "IptvEpgSources");

            migrationBuilder.DropTable(
                name: "IptvRecordingSessions");

            migrationBuilder.DropTable(
                name: "IptvTunerProfiles");

            migrationBuilder.DropTable(
                name: "MediaArtwork");

            migrationBuilder.DropTable(
                name: "MediaAudioTracks");

            migrationBuilder.DropTable(
                name: "MediaCastMembers");

            migrationBuilder.DropTable(
                name: "MediaDedupeIgnoredGroups");

            migrationBuilder.DropTable(
                name: "MediaDedupeSettings");

            migrationBuilder.DropTable(
                name: "MediaItemAnalysis");

            migrationBuilder.DropTable(
                name: "MediaItemCompanies");

            migrationBuilder.DropTable(
                name: "MediaItemCountries");

            migrationBuilder.DropTable(
                name: "MediaItemEmbeddings");

            migrationBuilder.DropTable(
                name: "MediaItemGenres");

            migrationBuilder.DropTable(
                name: "MediaRequestUsers");

            migrationBuilder.DropTable(
                name: "MediaSubtitleTracks");

            migrationBuilder.DropTable(
                name: "MediaVideos");

            migrationBuilder.DropTable(
                name: "MediaVideoTracks");

            migrationBuilder.DropTable(
                name: "OverlayTemplates");

            migrationBuilder.DropTable(
                name: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "PluginSettings");

            migrationBuilder.DropTable(
                name: "PodcastEpisodeProfileStates");

            migrationBuilder.DropTable(
                name: "PodcastSubscriptions");

            migrationBuilder.DropTable(
                name: "ProfileAccessSchedules");

            migrationBuilder.DropTable(
                name: "ProfileDeviceSettings");

            migrationBuilder.DropTable(
                name: "RegistrationTickets");

            migrationBuilder.DropTable(
                name: "ServerSettings");

            migrationBuilder.DropTable(
                name: "SmartLists");

            migrationBuilder.DropTable(
                name: "SmartPlaylists");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropTable(
                name: "StreamSessions");

            migrationBuilder.DropTable(
                name: "SystemMetrics");

            migrationBuilder.DropTable(
                name: "TrackLikes");

            migrationBuilder.DropTable(
                name: "TrackPlayHistory");

            migrationBuilder.DropTable(
                name: "TvShowNetworks");

            migrationBuilder.DropTable(
                name: "UserMediaStates");

            migrationBuilder.DropTable(
                name: "UserProviderConnections");

            migrationBuilder.DropTable(
                name: "UserWatchlistItems");

            migrationBuilder.DropTable(
                name: "WebhookConfigs");

            migrationBuilder.DropTable(
                name: "IptvRecordingSchedules");

            migrationBuilder.DropTable(
                name: "Actors");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "MediaRequests");

            migrationBuilder.DropTable(
                name: "MediaParts");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "PodcastEpisodes");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "ClientDevices");

            migrationBuilder.DropTable(
                name: "Networks");

            migrationBuilder.DropTable(
                name: "IptvChannels");

            migrationBuilder.DropTable(
                name: "RequestServers");

            migrationBuilder.DropTable(
                name: "MediaItems");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "PodcastShows");

            migrationBuilder.DropTable(
                name: "IptvPlaylists");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "MediaLibraries");
        }
    }
}
