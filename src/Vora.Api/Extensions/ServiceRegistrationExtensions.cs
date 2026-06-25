using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Vora.Api.Hubs;
using Vora.Api.Middleware;
using Vora.Application.Actors;
using Vora.Application.Admin;
using Vora.Application.Ai;
using Vora.Application.Analysis;
using Vora.Application.Artwork;
using Vora.Application.Auth;
using Vora.Application.Calendar;
using Vora.Application.Collections;
using Vora.Application.Devices;
using Vora.Application.Discovery;
using Vora.Application.Email;
using Vora.Application.FileSystem;
using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.LibraryMigration;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Metadata;
using Vora.Application.Net;
using Vora.Application.Notifications;
using Vora.Application.Playlists;
using Vora.Application.Plugins;
using Vora.Application.Podcasts;
using Vora.Application.Posters;
using Vora.Application.Providers;
using Vora.Application.Recommendations;
using Vora.Application.Requests;
using Vora.Application.Search;
using Vora.Application.Settings;
using Vora.Application.SmartLists;
using Vora.Application.Streaming;
using Vora.Application.Sync;
using Vora.Application.Tasks;
using Vora.Application.Templates;
using Vora.Application.Themes;
using Vora.Application.Users;
using Vora.Application.Watchers;
using Vora.Application.YouTube;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Email;
using Vora.Infrastructure.FileSystem;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Infrastructure.Settings;
using Vora.Infrastructure.Transcoding;
using Vora.Infrastructure.Workers;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.FanartTv;
using Vora.Plugins.Providers.Genius;
using Vora.Plugins.Providers.Itunes;
using Vora.Plugins.Providers.LastFm;
using Vora.Plugins.Providers.LrcLib;
using Vora.Plugins.Providers.MusicBrainz;
using Vora.Plugins.Providers.Plex;
using Vora.Plugins.Providers.TheAudioDb;

namespace Vora.Api.Extensions;

public static class ServiceRegistrationExtensions
{
    private const string CorsPolicyName = "AllowReactApp";
    private const string AdminPolicyName = "AdminOnly";

    public static WebApplicationBuilder AddVoraServices(this WebApplicationBuilder builder)
    {
        builder.AddVoraLogging();
        builder.Services.AddVoraSwagger();
        builder.Services.AddVoraOptions(builder.Configuration);
        builder.Services.AddVoraDatabase(builder.Configuration);
        builder.Services.AddVoraRepositories();
        builder.Services.AddVoraManagers();
        builder.Services.AddVoraApplicationServices();
        builder.Services.AddVoraWorkers();
        builder.Services.AddVoraInfrastructure();
        builder.Services.AddVoraEmail(builder.Configuration);
        builder.Services.AddVoraRealtime();
        builder.Services.AddVoraPluginSystem(builder.Configuration);
        builder.Services.AddVoraBackups(builder.Configuration);
        builder.Services.AddVoraAuthenticationAndAuthorization(builder.Configuration, builder.Environment);
        builder.Services.AddVoraCors(builder.Configuration);
        builder.Services.AddVoraJsonOptions();
        builder.Services.AddVoraRateLimiting();
        builder.Services.AddVoraHealthChecks();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<VoraGlobalExceptionHandler>();
        builder.Services.AddVoraResponseCompression();
        return builder;
    }

    private static IHttpClientBuilder AddVoraResilience(this IHttpClientBuilder builder, int totalTimeoutSeconds)
    {
        _ = totalTimeoutSeconds;
        builder.AddStandardResilienceHandler();
        return builder;
    }

    private static IServiceCollection AddVoraResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
            {
                "application/json",
                "application/javascript",
                "text/css",
                "text/html",
                "text/json",
                "text/plain",
                "text/vtt",
                "image/svg+xml",
                "application/xml",
                "application/rss+xml"
            });
        });

        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

        return services;
    }

    private static IServiceCollection AddVoraHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<VoraDbContext>(name: "database", failureStatus: HealthStatus.Unhealthy);
        return services;
    }

    private static IServiceCollection AddVoraRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(VoraRateLimitPolicies.AuthStrict, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(VoraRateLimitPolicies.AuthBurst, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string GetClientPartitionKey(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrEmpty(ip) ? "unknown" : ip;
    }

    private static IServiceCollection AddVoraOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Vora.Application.Auth.JwtOptions>(configuration.GetSection(Vora.Application.Auth.JwtOptions.SectionName));
        services.Configure<Vora.Application.Settings.StoragePathsOptions>(configuration.GetSection(Vora.Application.Settings.StoragePathsOptions.SectionName));
        services.Configure<Vora.Application.Iptv.IptvPassthroughOptions>(configuration.GetSection(Vora.Application.Iptv.IptvPassthroughOptions.SectionName));
        services.Configure<Vora.Application.Settings.ForwardedHeadersConfigOptions>(configuration.GetSection(Vora.Application.Settings.ForwardedHeadersConfigOptions.SectionName));
        services.Configure<Vora.Application.Settings.CorsConfigOptions>(configuration.GetSection(Vora.Application.Settings.CorsConfigOptions.SectionName));
        return services;
    }

    private static IServiceCollection AddVoraBackups(this IServiceCollection services, IConfiguration configuration)
    {
        var storagePaths = ReadStoragePaths(configuration);

        var backupsDir = storagePaths.Backups;
        if (string.IsNullOrWhiteSpace(backupsDir))
        {
            backupsDir = Path.Combine(AppContext.BaseDirectory, "backups");
        }
        Directory.CreateDirectory(backupsDir);

        var dpDir = storagePaths.DataProtection;
        if (string.IsNullOrWhiteSpace(dpDir))
        {
            dpDir = Path.Combine(AppContext.BaseDirectory, "DataProtectionKeys");
        }

        services.AddSingleton(new Vora.Application.Backups.BackupManagerOptions
        {
            DefaultDirectory = backupsDir,
            SupportedSchemaVersion = 1
        });
        services.AddSingleton(new Vora.Infrastructure.Backups.Sections.DataProtectionKeysBackupOptions
        {
            Directory = dpDir
        });

        services.AddSingleton<Vora.Application.Backups.IBackupSettingsStore, Vora.Application.Backups.BackupSettingsStore>();
        services.AddSingleton<Vora.Application.Backups.IBackupManager, Vora.Application.Backups.BackupManager>();
        services.AddScoped<Vora.Application.Backups.IBackupTransactionFactory, Vora.Infrastructure.Backups.EfBackupTransactionFactory>();

        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.ServerSettingsBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.PluginSettingsBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection>(sp =>
            new Vora.Infrastructure.Backups.Sections.DataProtectionKeysBackupSection(
                sp.GetRequiredService<Vora.Infrastructure.Backups.Sections.DataProtectionKeysBackupOptions>()));

        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.ClientTemplateSchedulesBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.EmailTemplatesBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.OverlayTemplatesBackupSection>();

        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.SmartListsBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.DedupeRulesBackupSection>();

        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.IptvPlaylistsBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.IptvEpgSourcesBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.IptvTunerProfilesBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.IptvRecordingSchedulesBackupSection>();

        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.DiscoveryRowsBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.RequestServersBackupSection>();

        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.UsersAndProfilesBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.DevicesBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.WatchHistoryBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.RatingsBackupSection>();
        services.AddScoped<Vora.Application.Backups.IBackupSection, Vora.Infrastructure.Backups.Sections.ExternalConnectionsBackupSection>();

        services.AddHostedService<BackupScheduleWorker>();
        return services;
    }

    private static WebApplicationBuilder AddVoraLogging(this WebApplicationBuilder builder)
    {
        var storagePaths = ReadStoragePaths(builder.Configuration);
        var configuredLogDir = storagePaths.Logs;
        var logDir = !string.IsNullOrWhiteSpace(configuredLogDir)
            ? configuredLogDir
            : Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        var bufferCapacity = builder.Configuration.GetValue<int?>("Logging:Vora:BufferCapacity") ?? 10_000;
        var retentionDays = builder.Configuration.GetValue<int?>("Logging:Vora:RetentionDays") ?? 14;

        var buffer = new Vora.Application.Logging.InMemoryLogBuffer(bufferCapacity);
        var fileSink = new Vora.Application.Logging.LogFileSink(new Vora.Application.Logging.LogFileSinkOptions
        {
            Directory = logDir,
            RetentionDays = retentionDays
        });
        var levels = new Vora.Application.Logging.LogLevelOverrideProvider();

        var defaultLevelString = builder.Configuration["Logging:LogLevel:Default"];
        if (!string.IsNullOrWhiteSpace(defaultLevelString)
            && Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(defaultLevelString, true, out var mel))
        {
            levels.DefaultLevel = mel switch
            {
                Microsoft.Extensions.Logging.LogLevel.Trace => Vora.Application.Logging.VoraLogLevel.Trace,
                Microsoft.Extensions.Logging.LogLevel.Debug => Vora.Application.Logging.VoraLogLevel.Debug,
                Microsoft.Extensions.Logging.LogLevel.Information => Vora.Application.Logging.VoraLogLevel.Information,
                Microsoft.Extensions.Logging.LogLevel.Warning => Vora.Application.Logging.VoraLogLevel.Warning,
                Microsoft.Extensions.Logging.LogLevel.Error => Vora.Application.Logging.VoraLogLevel.Error,
                Microsoft.Extensions.Logging.LogLevel.Critical => Vora.Application.Logging.VoraLogLevel.Critical,
                _ => Vora.Application.Logging.VoraLogLevel.Information
            };
        }

        builder.Services.AddSingleton<Vora.Application.Logging.ILogBuffer>(buffer);
        builder.Services.AddSingleton(fileSink);
        builder.Services.AddSingleton(levels);
        builder.Services.AddSingleton<Vora.Application.Logging.ILogManager, Vora.Application.Logging.LogManager>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Vora.Application.Logging.LogFileSink>());
        builder.Services.AddHostedService<Vora.Application.Logging.LogBroadcastHostedService>();

        builder.Logging.AddProvider(new Vora.Application.Logging.VoraLoggerProvider(buffer, fileSink, levels));
        builder.Logging.AddFilter<Vora.Application.Logging.VoraLoggerProvider>(null, Microsoft.Extensions.Logging.LogLevel.Trace);
        return builder;
    }

    private static IServiceCollection AddVoraSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.CustomOperationIds(SwaggerOperationIds.Build);
            options.SupportNonNullableReferenceTypes();
            options.UseAllOfToExtendReferenceSchemas();
        });
        return services;
    }

    private static IServiceCollection AddVoraDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<VoraDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.UseVector()));
        return services;
    }

    private static IServiceCollection AddVoraRepositories(this IServiceCollection services)
    {
        services.AddScoped<IActorRepository, ActorRepository>();
        services.AddScoped<IAdminNotificationRepository, AdminNotificationRepository>();
        services.AddScoped<ICalendarRepository, CalendarRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IDiscoveryRepository, DiscoveryRepository>();
        services.AddScoped<IEmailDeliveryLogRepository, EmailDeliveryLogRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IIptvRepository, IptvRepository>();
        services.AddScoped<ILibraryRepository, LibraryRepository>();
        services.AddScoped<IMediaArtworkRepository, MediaArtworkRepository>();
        services.AddScoped<IMediaDedupeRepository, MediaDedupeRepository>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IMusicRepository, MusicRepository>();
        services.AddScoped<IMusicRecommendationRepository, MusicRecommendationRepository>();
        services.AddScoped<IOpenAiRecommendationRepository, OpenAiRecommendationRepository>();
        services.AddScoped<IOverlayTemplateRepository, OverlayTemplateRepository>();
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();
        services.AddScoped<IPodcastRepository, PodcastRepository>();
        services.AddScoped<IProviderConnectionRepository, ProviderConnectionRepository>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();
        services.AddScoped<IReferenceRepository, ReferenceRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<IClientTemplateScheduleRepository, ClientTemplateScheduleRepository>();
        services.AddScoped<ISmartListRepository, SmartListRepository>();
        services.AddScoped<ISmartPlaylistRepository, SmartPlaylistRepository>();
        services.AddScoped<ISmartPlaylistEvaluator, SmartPlaylistEvaluator>();
        services.AddScoped<IStreamRepository, StreamRepository>();
        services.AddScoped<ISystemMetricRepository, SystemMetricRepository>();
        services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
        services.AddScoped<IUserMediaStateRepository, UserMediaStateRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILibraryMigrationRepository, LibraryMigrationRepository>();
        services.AddScoped<IYouTubeAccessRepository, YouTubeAccessRepository>();
        services.AddScoped<IYouTubeRepository, YouTubeRepository>();
        return services;
    }

    private static IServiceCollection AddVoraManagers(this IServiceCollection services)
    {
        services.AddScoped<IActorManager, ActorManager>();
        services.AddScoped<IAdminNotificationManager, AdminNotificationManager>();
        services.AddScoped<IAiStatsManager, AiStatsManager>();
        services.AddScoped<IAuthManager, AuthManager>();
        services.AddScoped<IJwtSecurityStampValidator, JwtSecurityStampValidator>();
        services.AddScoped<IBestPathDecisionManager, BestPathDecisionManager>();
        services.AddScoped<ICalendarManager, CalendarManager>();
        services.AddScoped<ICollectionManager, CollectionManager>();
        services.AddScoped<IDashboardManager, DashboardManager>();
        services.AddScoped<IDeviceManager, DeviceManager>();
        services.AddScoped<IDiscoveryManager, DiscoveryManager>();
        services.AddScoped<IDvrManager, DvrManager>();
        services.AddScoped<IEmailSettingsManager, EmailSettingsManager>();
        services.AddScoped<IEmailTemplateManager, EmailTemplateManager>();
        services.AddScoped<IInvitationManager, InvitationManager>();
        services.AddScoped<IIptvManager, IptvManager>();
        services.AddScoped<ILibraryManager, LibraryManager>();
        services.AddScoped<ILibraryMigrationManager, LibraryMigrationManager>();
        services.AddSingleton<ILibraryMigrationJobRunner, LibraryMigrationJobRunner>();
        services.AddScoped<IMediaAnalyzerManager, MediaAnalyzerManager>();
        services.AddScoped<IMarkerAssembler, MarkerAssembler>();
        services.AddScoped<Vora.Application.Thumbnails.IVideoThumbnailManager, Vora.Application.Thumbnails.VideoThumbnailManager>();
        services.AddScoped<IMediaDedupeManager, MediaDedupeManager>();
        services.AddScoped<IMediaManager, MediaManager>();
        services.AddScoped<IMusicManager, MusicManager>();
        services.AddScoped<IMusicRecommendationManager, MusicRecommendationManager>();
        services.AddSingleton<IServerPlaybackTracker, ServerPlaybackTracker>();
        services.AddScoped<IMetadataManager, MetadataManager>();
        services.AddScoped<IPlaylistManager, PlaylistManager>();
        services.AddScoped<ISmartPlaylistManager, SmartPlaylistManager>();
        services.AddScoped<IPodcastManager, PodcastManager>();
        services.AddScoped<IPluginManager, PluginManager>();
        services.AddScoped<IPluginSettingsEnvSeeder, PluginSettingsEnvSeeder>();
        services.AddScoped<IOverlayTemplateManager, OverlayTemplateManager>();
        services.AddSingleton<IOverlaySweepService, Vora.Infrastructure.Posters.OverlaySweepService>();
        services.AddScoped<IPosterOverlayManager, PosterOverlayManager>();
        services.AddScoped<IProviderConnectionManager, ProviderConnectionManager>();
        services.AddScoped<IRecommendationManager, RecommendationManager>();
        services.AddScoped<IRemoteAccessManager, RemoteAccessManager>();
        services.AddScoped<IRequestManager, RequestManager>();
        services.AddScoped<ISearchManager, SearchManager>();
        services.AddScoped<ISmartListManager, SmartListManager>();
        services.AddScoped<IStreamManager, StreamManager>();
        services.AddScoped<ISyncAndStateManager, SyncAndStateManager>();
        services.AddScoped<ISystemSettingsManager, SystemSettingsManager>();
        services.AddSingleton<ITaskQueueManager, TaskQueueManager>();
        services.AddSingleton<IThemeBundleLoader>(sp =>
            new ThemeBundleLoader(Path.Combine(AppContext.BaseDirectory, "Themes"),
                sp.GetRequiredService<ILogger<ThemeBundleLoader>>()));
        services.AddSingleton<IThemeAssetService, ThemeAssetService>();
        services.AddSingleton<IThemeRegistry, ThemeRegistry>();
        services.AddScoped<IThemeManager, ThemeManager>();
        services.AddSingleton<IClientTemplateBundleLoader>(sp =>
            new ClientTemplateBundleLoader(Path.Combine(AppContext.BaseDirectory, "Templates"),
                sp.GetRequiredService<ILogger<ClientTemplateBundleLoader>>()));
        services.AddSingleton<IClientTemplateAssetService, ClientTemplateAssetService>();
        services.AddSingleton<IClientTemplateRegistry, ClientTemplateRegistry>();
        services.AddScoped<IClientTemplateScheduleManager, ClientTemplateScheduleManager>();
        services.AddScoped<IClientTemplateManager, ClientTemplateManager>();
        services.AddScoped<IUserManager, UserManager>();
        services.AddScoped<IUserMediaStateManager, UserMediaStateManager>();
        services.AddScoped<IYouTubeAccessResolver, YouTubeAccessResolver>();
        services.AddScoped<IYouTubeManager, YouTubeManager>();
        return services;
    }

    private static IServiceCollection AddVoraApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IArtworkService, ArtworkService>();
        services.AddScoped<ICollectionArtworkService, CollectionArtworkService>();
        services.AddScoped<IIptvEpgService, IptvEpgService>();
        services.AddScoped<IIptvPassthroughService, IptvPassthroughService>();
        services.AddScoped<IMediaAnalyzerService, FFmpegAnalyzerService>();
        services.AddScoped<IMediaEmbeddingService, MediaEmbeddingService>();
        services.AddScoped<IMediaIngestionService, MediaIngestionService>();
        services.AddScoped<IMetadataFetchService, MetadataFetchService>();
        services.AddScoped<IMetadataMappingService, MetadataMappingService>();
        services.AddScoped<IRequestNotificationService, RequestNotificationService>();
        services.AddScoped<IUserProfileImageService, UserProfileImageService>();
        services.AddScoped<IYouTubeDataApiClient, YouTubeDataApiClient>();
        services.AddScoped<CollectionOrderingService>();
        services.AddScoped<CollectionSyncService>();

        services.AddSingleton<IDvrRecordingService, DvrRecordingService>();
        services.AddSingleton<IFileSystemBrowserService, FileSystemBrowserService>();
        services.AddSingleton<IFolderWatcherService, FolderWatcherService>();
        services.AddSingleton<ITimeshiftCoordinator, TimeshiftCoordinator>();
        services.AddSingleton<ITranscodeService, FFmpegTranscodeService>();
        services.AddSingleton<IAudioTranscodeService, FFmpegAudioTranscodeService>();
        services.AddSingleton<IHardwareCapabilityService, HardwareCapabilityService>();
        services.AddSingleton<IStreamingTokenSigner, StreamingTokenSigner>();
        services.AddSingleton<Vora.Application.Thumbnails.IVideoThumbnailStorageService, Vora.Application.Thumbnails.VideoThumbnailStorageService>();
        services.AddSingleton<Vora.Application.Thumbnails.IVideoThumbnailGeneratorService, Vora.Infrastructure.Thumbnails.FFmpegVideoThumbnailGeneratorService>();
        return services;
    }

    private static IServiceCollection AddVoraWorkers(this IServiceCollection services)
    {
        services.AddHostedService<AnalyticsPollerWorker>();
        services.AddHostedService<DvrPostProcessingWorker>();
        services.AddHostedService<DvrStorageMonitorWorker>();
        services.AddHostedService<DvrWorker>();
        services.AddHostedService<EmailDispatchWorker>();
        services.AddHostedService<PodcastFeedRefreshWorker>();
        services.AddHostedService<RecommendationRefreshWorker>();
        services.AddHostedService<ScheduledJobWorker>();
        services.AddHostedService<StartupWatcherService>();
        services.AddHostedService<TaskProcessingWorker>();
        services.AddHostedService<TimeshiftJanitorWorker>();
        services.AddHostedService<TranscodeJanitorWorker>();
        return services;
    }

    private static IServiceCollection AddVoraInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient(SafeImageDownloader.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ISafeImageDownloader, SafeImageDownloader>();
        services.AddHttpClient(DeviceTrackingMiddleware.GeoLookupHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient(IptvManager.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        });
        services.AddHttpClient(PodcastManager.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Podcast Reader)");
            client.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml");
        });
        services.AddHttpClient(MusicBrainzArtworkProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-MusicMetadata/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(FanartTvMusicArtworkProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-MusicMetadata/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(TheAudioDbMusicArtworkProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-MusicMetadata/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(ItunesPodcastDiscoveryProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Podcast Discovery)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(LrcLibLyricsProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-Lyrics/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddVoraResilience(totalTimeoutSeconds: 20);
        services.AddHttpClient(LastFmListeningDataProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Last.fm Scrobbler)");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(GeniusLyricsProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-Lyrics/1.0 (https://github.com/zenith/vora)");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(PlexLibrarySyncProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Library Migration)");
        }).AddVoraResilience(totalTimeoutSeconds: 60);
        services.AddHttpClient(YouTubeDataApiClient.DataApiHttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (YouTube Client)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddHttpClient(YouTubeDataApiClient.RssHttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (YouTube RSS Reader)");
            client.DefaultRequestHeaders.Add("Accept", "application/atom+xml, application/xml, text/xml");
        }).AddVoraResilience(totalTimeoutSeconds: 30);
        services.AddMemoryCache();
        return services;
    }

    private static IServiceCollection AddVoraEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var storagePaths = ReadStoragePaths(configuration);
        var configuredKeyPath = storagePaths.DataProtection;
        var keyPath = !string.IsNullOrWhiteSpace(configuredKeyPath)
            ? configuredKeyPath
            : Path.Combine(AppContext.BaseDirectory, "DataProtectionKeys");

        Directory.CreateDirectory(keyPath);

        services.AddDataProtection()
            .SetApplicationName("Vora")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        services.AddSingleton<IEmailSecretProtector, DataProtectionEmailSecretProtector>();
        services.AddSingleton<IEmailDispatchQueue, EmailDispatchQueue>();
        services.AddScoped<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();
        services.AddScoped<IEmailService, EmailService>();

        return services;
    }

    private static IServiceCollection AddVoraRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IClientNotifier, SignalRClientNotifier>();
        return services;
    }

    private static IServiceCollection AddVoraPluginSystem(this IServiceCollection services, IConfiguration configuration)
    {
        var storagePaths = ReadStoragePaths(configuration);
        var configured = storagePaths.Plugins;
        var pluginsPath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Plugins");

        using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = startupLoggerFactory.CreateLogger("Vora.PluginLoader");

        services.AddVoraPlugins(pluginsPath, startupLogger);
        services.AddScoped<IPluginSettingsProvider, PluginSettingsAdapter>();
        services.AddScoped<Vora.Plugins.Interfaces.IRequestServerLookup, Vora.Application.Requests.RequestServerLookupAdapter>();
        return services;
    }

    private static StoragePathsOptions ReadStoragePaths(IConfiguration configuration)
        => configuration.GetSection(StoragePathsOptions.SectionName).Get<StoragePathsOptions>() ?? new StoragePathsOptions();

    private const string JwtSecretPlaceholder = "REPLACE_THIS_WITH_A_VERY_LONG_AND_SECURE_RANDOM_STRING_IN_PRODUCTION!";
    private const int JwtMinSecretByteLength = 32;

    private static IServiceCollection AddVoraAuthenticationAndAuthorization(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var allowsDefaults = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var jwtSecret = ResolveJwtSecret(configuration, environment);

        var jwtIssuer = configuration["Jwt:Issuer"];
        if (string.IsNullOrWhiteSpace(jwtIssuer))
        {
            if (allowsDefaults)
            {
                jwtIssuer = "VoraMediaServer";
            }
            else
            {
                throw new InvalidOperationException("Jwt:Issuer is not configured. Set it via appsettings.json, environment variable (Jwt__Issuer), or user-secrets.");
            }
        }

        var jwtAudience = configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(jwtAudience))
        {
            if (allowsDefaults)
            {
                jwtAudience = "VoraMediaServer";
            }
            else
            {
                throw new InvalidOperationException("Jwt:Audience is not configured. Set it via appsettings.json, environment variable (Jwt__Audience), or user-secrets.");
            }
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"].ToString();
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/Vora"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (principal is null)
                        {
                            context.Fail("Principal missing.");
                            return;
                        }

                        var stampValidator = context.HttpContext.RequestServices.GetRequiredService<IJwtSecurityStampValidator>();

                        var accountIdValue = principal.GetAccountId();
                        var profileIdValue = principal.GetProfileId();
                        var accountStamp = principal.FindFirst("stamp")?.Value;
                        var profileStamp = principal.FindFirst("profileStamp")?.Value;

                        if (accountIdValue is null || string.IsNullOrEmpty(accountStamp))
                        {
                            context.Fail("Stamp missing.");
                            return;
                        }

                        var hasProfileToken = !string.IsNullOrEmpty(profileStamp);
                        var profileIdForValidation = hasProfileToken ? profileIdValue : null;

                        var isValid = await stampValidator.IsStampValidAsync(
                            accountIdValue.Value,
                            accountStamp,
                            profileIdForValidation,
                            hasProfileToken ? profileStamp : null);

                        if (!isValid)
                        {
                            context.Fail("Stamp invalid.");
                        }
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminPolicyName, policy => policy.RequireClaim("isAdmin", "True"));
        });

        return services;
    }

    private const string DevelopmentJwtFallbackSecret = "vora-development-only-do-not-use-in-production-environments-please";

    private static string ResolveJwtSecret(IConfiguration configuration, IHostEnvironment environment)
    {
        var jwtSecret = configuration["Jwt:SecretKey"];

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            if (string.IsNullOrWhiteSpace(jwtSecret)
                || string.Equals(jwtSecret, JwtSecretPlaceholder, StringComparison.Ordinal)
                || Encoding.UTF8.GetByteCount(jwtSecret) < JwtMinSecretByteLength)
            {
                if (environment.IsDevelopment())
                {
                    Console.WriteLine("[Vora] WARNING: Jwt:SecretKey is missing, placeholder, or too short. Using built-in development fallback. DO NOT deploy this environment to production.");
                }
                return DevelopmentJwtFallbackSecret;
            }
            return jwtSecret;
        }

        if (string.IsNullOrWhiteSpace(jwtSecret))
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Set it via environment variable (Jwt__SecretKey) or user-secrets. The value must be at least " + JwtMinSecretByteLength + " UTF-8 bytes long.");
        }

        if (string.Equals(jwtSecret, JwtSecretPlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey is still set to the placeholder value. Replace it via environment variable (Jwt__SecretKey) or user-secrets with a unique secret of at least " + JwtMinSecretByteLength + " UTF-8 bytes.");
        }

        if (Encoding.UTF8.GetByteCount(jwtSecret) < JwtMinSecretByteLength)
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey is too short. Provide a value of at least " + JwtMinSecretByteLength + " UTF-8 bytes via environment variable (Jwt__SecretKey) or user-secrets.");
        }

        return jwtSecret;
    }

    private static IServiceCollection AddVoraCors(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration.GetSection(Vora.Application.Settings.CorsConfigOptions.SectionName).Get<Vora.Application.Settings.CorsConfigOptions>()
            ?? new Vora.Application.Settings.CorsConfigOptions();

        var defaultOrigins = new[] { "https://localhost:5173", "http://localhost:5173" };
        var merged = defaultOrigins
            .Concat(configured.AllowedOrigins ?? new List<string>())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy.WithOrigins(merged)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
        return services;
    }

    private static IServiceCollection AddVoraJsonOptions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        return services;
    }

    public static string CorsPolicy => CorsPolicyName;
}
