using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Backups;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class BackupCoverageTests : IClassFixture<VoraApiTestFactory>
{
    private const string Scanned = "Rebuilt by a library scan.";
    private const string Derived = "Derived data Vora recomputes.";
    private const string ServerLog = "A log the server keeps for itself.";
    private const string Secret = "A secret that should not outlive the server.";

    private static readonly Dictionary<string, string> BackedUpBy = new()
    {
        ["ServerSettings"] = "settings.server",
        ["PluginSettings"] = "settings.plugins",
        ["WebhookConfigs"] = "settings.webhooks",
        ["ClientTemplateSchedules"] = "templates.client-schedules",
        ["EmailTemplates"] = "templates.email",
        ["OverlayTemplates"] = "templates.overlay",
        ["Users"] = "users.profiles",
        ["UserProfiles"] = "users.profiles",
        ["ProfileAccessSchedules"] = "users.profiles",
        ["ClientDevices"] = "users.devices",
        ["ProfileDeviceSettings"] = "users.devices",
        ["Collections"] = "library.collections",
        ["CollectionItems"] = "library.collections",
        ["CollectionArtwork"] = "library.collections",
        ["SmartLists"] = "library.smart-lists",
        ["MediaDedupeSettings"] = "library.dedupe-rules",
        ["MediaDedupeIgnoredGroups"] = "library.dedupe-rules",
        ["IptvPlaylists"] = "iptv.playlists",
        ["IptvChannels"] = "iptv.channel-settings",
        ["IptvEpgSources"] = "iptv.epg-sources",
        ["IptvTunerProfiles"] = "iptv.tuner-profiles",
        ["IptvRecordingSchedules"] = "iptv.recording-schedules",
        ["IptvRecordingSessions"] = "iptv.recordings",
        ["DiscoveryRowConfigs"] = "discovery.rows",
        ["RequestServers"] = "discovery.request-servers",
        ["UserMediaStates"] = "users.watch-history",
        ["StreamSessions"] = "users.watch-history",
        ["TrackPlayHistory"] = "users.watch-history",
        ["PreservedUserMediaData"] = "users.watch-history",
        ["UserMediaRatings"] = "users.ratings",
        ["UserAlbumRatings"] = "users.ratings",
        ["UserArtistRatings"] = "users.ratings",
        ["TrackLikes"] = "users.ratings",
        ["Playlists"] = "users.playlists",
        ["PlaylistItems"] = "users.playlists",
        ["SmartPlaylists"] = "users.playlists",
        ["UserWatchlistItems"] = "users.watchlists",
        ["Stations"] = "users.stations",
        ["GeneratedMixes"] = "users.ai-playlists",
        ["MediaRequests"] = "users.requests",
        ["MediaRequestUsers"] = "users.requests",
        ["UserProviderConnections"] = "users.external-connections",
        ["ProfileChannelFavorites"] = "users.channel-favorites",
        ["PodcastShows"] = "podcasts.shows",
        ["PodcastSubscriptions"] = "podcasts.listening",
        ["PodcastEpisodeProfileStates"] = "podcasts.listening",
    };

    private static readonly Dictionary<string, string> LeftOut = new()
    {
        ["MediaLibraries"] = Scanned,
        ["MediaItems"] = Scanned,
        ["MediaParts"] = Scanned,
        ["MediaAudioTracks"] = Scanned,
        ["MediaVideoTracks"] = Scanned,
        ["MediaSubtitleTracks"] = Scanned,
        ["MediaArtwork"] = Scanned,
        ["MediaCastMembers"] = Scanned,
        ["MediaExtras"] = Scanned,
        ["MediaVideos"] = Scanned,
        ["MediaItemGenres"] = Scanned,
        ["MediaItemCompanies"] = Scanned,
        ["MediaItemCountries"] = Scanned,
        ["TvShowNetworks"] = Scanned,
        ["Actors"] = Scanned,
        ["Albums"] = Scanned,
        ["Artists"] = Scanned,
        ["Genres"] = Scanned,
        ["Companies"] = Scanned,
        ["Countries"] = Scanned,
        ["Networks"] = Scanned,
        ["MediaItemEmbeddings"] = Derived,
        ["MediaItemAnalysis"] = Derived,
        ["MediaItemAudioFingerprints"] = Derived,
        ["MediaItemMarkers"] = Derived,
        ["ArtistSimilarities"] = Derived,
        ["ArtistTags"] = Derived,
        ["EmailDeliveryLogs"] = ServerLog,
        ["AiUsageLogs"] = ServerLog,
        ["SystemMetrics"] = ServerLog,
        ["AdminNotifications"] = ServerLog,
        ["AuthRefreshTokens"] = Secret,
        ["RegistrationTickets"] = Secret,
        ["InvitationTickets"] = Secret,
        ["PasswordResetTickets"] = Secret,
        ["EmailChangeTickets"] = Secret,
        ["PendingTasks"] = "The task queue, which only lives until the tasks run.",
        ["PodcastEpisodes"] = "Fetched again from each feed; Podcast Listening writes placeholders for episodes with progress.",
    };

    private readonly VoraApiTestFactory _factory;

    public BackupCoverageTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private static List<string> ModelTables()
    {
        using var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.UseVector())
            .Options);
        return db.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>().Distinct().ToList();
    }

    [Fact]
    public void Every_table_is_backed_up_or_deliberately_left_out()
    {
        var tables = ModelTables();

        tables.Except(BackedUpBy.Keys).Except(LeftOut.Keys).Should().BeEmpty(
            "a new table has to be added to a backup section, or to the left-out list here with the reason, before it ships");
        BackedUpBy.Keys.Intersect(LeftOut.Keys).Should().BeEmpty();
        BackedUpBy.Keys.Concat(LeftOut.Keys).Except(tables).Should().BeEmpty("a table named here no longer exists");
    }

    [Fact]
    public void Every_section_named_for_a_table_is_registered()
    {
        using var scope = _factory.Services.CreateScope();
        var keys = scope.ServiceProvider.GetServices<IBackupSection>().Select(s => s.Key).ToHashSet();

        BackedUpBy.Values.Distinct().Except(keys).Should().BeEmpty();
    }
}
