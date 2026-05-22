using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Vora.Api.Hubs;
using Vora.Api.Middleware;
using Vora.Application.Actors;
using Vora.Application.Admin;
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
using Vora.Application.Tracking;
using Vora.Application.Users;
using Vora.Application.Watchers;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Email;
using Vora.Infrastructure.FileSystem;
using Vora.Infrastructure.Notifications;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
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
        builder.Services.AddVoraAuthenticationAndAuthorization(builder.Configuration);
        builder.Services.AddVoraCors();
        builder.Services.AddVoraJsonOptions();
        return builder;
    }

    private static IServiceCollection AddVoraBackups(this IServiceCollection services, IConfiguration configuration)
    {
        var backupsDir = configuration["StoragePaths:Backups"];
        if (string.IsNullOrWhiteSpace(backupsDir))
        {
            backupsDir = Path.Combine(AppContext.BaseDirectory, "backups");
        }
        Directory.CreateDirectory(backupsDir);

        var dpDir = configuration["StoragePaths:DataProtection"];
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

        services.AddHostedService<Vora.Application.Backups.BackupScheduleWorker>();
        return services;
    }

    private static WebApplicationBuilder AddVoraLogging(this WebApplicationBuilder builder)
    {
        var configuredLogDir = builder.Configuration["StoragePaths:Logs"];
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
        services.AddSwaggerGen();
        return services;
    }

    private static IServiceCollection AddVoraDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<VoraDbContext>(options =>
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
        return services;
    }

    private static IServiceCollection AddVoraManagers(this IServiceCollection services)
    {
        services.AddScoped<IActorManager, ActorManager>();
        services.AddScoped<IAdminNotificationManager, AdminNotificationManager>();
        services.AddScoped<IAuthManager, AuthManager>();
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
        services.AddSingleton<IThemeBundleLoader>(_ =>
            new ThemeBundleLoader(Path.Combine(AppContext.BaseDirectory, "Themes")));
        services.AddSingleton<IThemeAssetService, ThemeAssetService>();
        services.AddSingleton<IThemeRegistry, ThemeRegistry>();
        services.AddScoped<IThemeManager, ThemeManager>();
        services.AddSingleton<IClientTemplateBundleLoader>(_ =>
            new ClientTemplateBundleLoader(Path.Combine(AppContext.BaseDirectory, "Templates")));
        services.AddSingleton<IClientTemplateAssetService, ClientTemplateAssetService>();
        services.AddSingleton<IClientTemplateRegistry, ClientTemplateRegistry>();
        services.AddScoped<IClientTemplateScheduleManager, ClientTemplateScheduleManager>();
        services.AddScoped<IClientTemplateManager, ClientTemplateManager>();
        services.AddScoped<IUserManager, UserManager>();
        services.AddScoped<IUserMediaStateManager, UserMediaStateManager>();
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
        services.AddScoped<CollectionOrderingService>();
        services.AddScoped<CollectionSyncService>();

        services.AddSingleton<IDvrRecordingService, DvrRecordingService>();
        services.AddSingleton<IFileSystemBrowserService, FileSystemBrowserService>();
        services.AddSingleton<IFolderWatcherService, FolderWatcherService>();
        services.AddSingleton<ITimeshiftCoordinator, TimeshiftCoordinator>();
        services.AddSingleton<ITranscodeService, FFmpegTranscodeService>();
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
        return services;
    }

    private static IServiceCollection AddVoraInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient<IWebhookDispatcherService, WebhookDispatcherService>();
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
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-MusicMetadata/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddHttpClient(FanartTvMusicArtworkProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-MusicMetadata/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddHttpClient(TheAudioDbMusicArtworkProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-MusicMetadata/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddHttpClient(ItunesPodcastDiscoveryProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Podcast Discovery)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddHttpClient(LrcLibLyricsProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-Lyrics/1.0 (https://github.com/zenith/vora)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddHttpClient(LastFmListeningDataProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Last.fm Scrobbler)");
        });
        services.AddHttpClient(GeniusLyricsProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora-Lyrics/1.0 (https://github.com/zenith/vora)");
        });
        services.AddHttpClient(PlexLibrarySyncProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Vora/1.0 (Library Migration)");
        });
        services.AddMemoryCache();
        return services;
    }

    private static IServiceCollection AddVoraEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var configuredKeyPath = configuration["StoragePaths:DataProtection"];
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
        var configured = configuration["StoragePaths:Plugins"];
        var pluginsPath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Plugins");
        services.AddVoraPlugins(pluginsPath);
        services.AddScoped<IPluginSettingsProvider, PluginSettingsAdapter>();
        services.AddScoped<Vora.Plugins.Interfaces.IRequestServerLookup, Vora.Application.Requests.RequestServerLookupAdapter>();
        return services;
    }

    private static IServiceCollection AddVoraAuthenticationAndAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:SecretKey"] ?? string.Empty;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminPolicyName, policy => policy.RequireClaim("IsAdmin", "True"));
        });

        return services;
    }

    private static IServiceCollection AddVoraCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy.WithOrigins("https://localhost:5173", "http://localhost:5173")
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
        return services;
    }

    public static string CorsPolicy => CorsPolicyName;
}
