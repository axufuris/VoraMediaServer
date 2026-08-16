using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Email;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Podcasts;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Notifications;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Posters;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Templates;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Ai;
using Vora.Domain.Entities.Tracking;
using Vora.Domain.Entities.Users;
using Vora.Domain.Entities.YouTube;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence;

public class VoraDbContext : DbContext
{
    public VoraDbContext(DbContextOptions<VoraDbContext> options)
        : base(options)
    {
    }

    public DbSet<MediaItem> MediaItems { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<TvShow> TvShows { get; set; }
    public DbSet<Episode> Episodes { get; set; }
    public DbSet<Track> Tracks { get; set; }
    public DbSet<Artist> Artists { get; set; }
    public DbSet<Album> Albums { get; set; }
    public DbSet<TrackLike> TrackLikes { get; set; }
    public DbSet<TrackPlayHistory> TrackPlayHistory { get; set; }
    public DbSet<GeneratedMix> GeneratedMixes { get; set; }
    public DbSet<Station> Stations { get; set; }
    public DbSet<ArtistSimilarity> ArtistSimilarities { get; set; }
    public DbSet<ArtistTag> ArtistTags { get; set; }
    public DbSet<MediaPart> MediaParts { get; set; }
    public DbSet<MediaVideoTrack> MediaVideoTracks { get; set; }
    public DbSet<MediaAudioTrack> MediaAudioTracks { get; set; }
    public DbSet<MediaSubtitleTrack> MediaSubtitleTracks { get; set; }
    public DbSet<MediaArtwork> MediaArtwork { get; set; }
    public DbSet<MediaVideo> MediaVideos { get; set; }
    public DbSet<MediaExtra> MediaExtras { get; set; }
    public DbSet<MediaItemAnalysis> MediaItemAnalysis { get; set; }
    public DbSet<MediaItemMarker> MediaItemMarkers { get; set; }
    public DbSet<MediaItemAudioFingerprint> MediaItemAudioFingerprints { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<Country> Countries { get; set; }
    public DbSet<Network> Networks { get; set; }

    public DbSet<Actor> Actors { get; set; }
    public DbSet<MediaCastMember> MediaCastMembers { get; set; }

    public DbSet<MediaLibrary> MediaLibraries { get; set; }

    public DbSet<Collection> Collections { get; set; }
    public DbSet<CollectionItem> CollectionItems { get; set; }
    public DbSet<CollectionArtwork> CollectionArtwork { get; set; }

    public DbSet<User> Users { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<UserMediaState> UserMediaStates { get; set; }
    public DbSet<UserMediaRating> UserMediaRatings { get; set; }
    public DbSet<PreservedUserMediaData> PreservedUserMediaData { get; set; }
    public DbSet<UserAlbumRating> UserAlbumRatings { get; set; }
    public DbSet<UserArtistRating> UserArtistRatings { get; set; }
    public DbSet<UserProviderConnection> UserProviderConnections { get; set; }
    public DbSet<ProfileAccessSchedule> ProfileAccessSchedules { get; set; }
    public DbSet<ProfileDeviceSetting> ProfileDeviceSettings { get; set; }
    public DbSet<RegistrationTicket> RegistrationTickets { get; set; }
    public DbSet<PasswordResetTicket> PasswordResetTickets { get; set; }
    public DbSet<EmailChangeTicket> EmailChangeTickets { get; set; }
    public DbSet<InvitationTicket> InvitationTickets { get; set; }
    public DbSet<ClientDevice> ClientDevices { get; set; }

    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<PlaylistItem> PlaylistItems { get; set; }
    public DbSet<SmartPlaylist> SmartPlaylists { get; set; }

    public DbSet<Vora.Domain.Entities.Notifications.AdminNotification> AdminNotifications { get; set; }

    public DbSet<SmartList> SmartLists { get; set; }

    public DbSet<IptvPlaylist> IptvPlaylists { get; set; }
    public DbSet<IptvEpgSource> IptvEpgSources { get; set; }
    public DbSet<IptvChannel> IptvChannels { get; set; }
    public DbSet<IptvTunerProfile> IptvTunerProfiles { get; set; }
    public DbSet<IptvRecordingSchedule> IptvRecordingSchedules { get; set; }
    public DbSet<IptvRecordingSession> IptvRecordingSessions { get; set; }

    public DbSet<PodcastShow> PodcastShows { get; set; }
    public DbSet<PodcastEpisode> PodcastEpisodes { get; set; }
    public DbSet<PodcastSubscription> PodcastSubscriptions { get; set; }
    public DbSet<PodcastEpisodeProfileState> PodcastEpisodeProfileStates { get; set; }

    public DbSet<StreamSession> StreamSessions { get; set; }

    public DbSet<MediaRequest> MediaRequests { get; set; }
    public DbSet<MediaRequestUser> MediaRequestUsers { get; set; }
    public DbSet<RequestServer> RequestServers { get; set; }

    public DbSet<OverlayTemplate> OverlayTemplates { get; set; }

    public DbSet<ServerSetting> ServerSettings { get; set; }
    public DbSet<PluginSettingValue> PluginSettings { get; set; }

    public DbSet<EmailTemplate> EmailTemplates { get; set; }
    public DbSet<EmailDeliveryLog> EmailDeliveryLogs { get; set; }

    public DbSet<ClientTemplateSchedule> ClientTemplateSchedules { get; set; }

    public DbSet<MediaDedupeSettings> MediaDedupeSettings { get; set; }
    public DbSet<MediaDedupeIgnoredGroup> MediaDedupeIgnoredGroups { get; set; }

    public DbSet<WebhookConfig> WebhookConfigs { get; set; }

    public DbSet<AiUsageLog> AiUsageLogs { get; set; }
    public DbSet<MediaItemEmbedding> MediaItemEmbeddings { get; set; }

    public DbSet<SystemMetric> SystemMetrics { get; set; }

    public DbSet<DiscoveryRowConfig> DiscoveryRowConfigs { get; set; }
    public DbSet<UserWatchlistItem> UserWatchlistItems { get; set; }

    public DbSet<YouTubeAccountSettings> YouTubeAccountSettings { get; set; }
    public DbSet<YouTubeProfileSettings> YouTubeProfileSettings { get; set; }
    public DbSet<YouTubeSubscription> YouTubeSubscriptions { get; set; }
    public DbSet<YouTubeWatchHistory> YouTubeWatchHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var isInMemoryProvider = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";

        if (!isInMemoryProvider)
        {
            modelBuilder.HasPostgresExtension("vector");
        }
        else
        {
            modelBuilder.Ignore<MediaItemEmbedding>();
        }

        var converters = new ListValueConverters();

        ConfigureMediaHierarchy(modelBuilder, converters);
        ConfigureMediaParts(modelBuilder);
        ConfigureMediaArtwork(modelBuilder);
        ConfigureMediaVideo(modelBuilder);
        ConfigureMediaExtra(modelBuilder);
        ConfigureMediaItemAnalysis(modelBuilder);
        ConfigureMediaItemMarkers(modelBuilder);
        ConfigureMediaItemAudioFingerprints(modelBuilder);
        ConfigureMediaItemRelationships(modelBuilder, converters);
        ConfigureReferenceTables(modelBuilder);

        ConfigureActors(modelBuilder);

        ConfigureLibrary(modelBuilder, converters);

        ConfigureCollections(modelBuilder, converters);

        ConfigureUsers(modelBuilder, converters);
        ConfigureProfiles(modelBuilder, converters);
        ConfigureProfileSchedules(modelBuilder);
        ConfigureProfileDeviceSettings(modelBuilder);
        ConfigureRegistrationTickets(modelBuilder);
        ConfigurePasswordResetTickets(modelBuilder);
        ConfigureEmailChangeTickets(modelBuilder);
        ConfigureInvitationTickets(modelBuilder);
        ConfigureClientDevices(modelBuilder, converters);

        ConfigurePlaylists(modelBuilder);

        ConfigureSmartLists(modelBuilder);

        ConfigureIptv(modelBuilder);
        ConfigurePodcasts(modelBuilder);

        ConfigureStreaming(modelBuilder);

        ConfigureRequests(modelBuilder);

        ConfigurePosters(modelBuilder);

        ConfigureSettings(modelBuilder);
        ConfigureEmail(modelBuilder);
        ConfigureMediaDedupe(modelBuilder);
        ConfigureClientTemplates(modelBuilder);

        ConfigureNotifications(modelBuilder);

        ConfigureAiUsage(modelBuilder);
        if (!isInMemoryProvider)
        {
            ConfigureMediaItemEmbeddings(modelBuilder);
        }

        ConfigureDiscovery(modelBuilder);

        ConfigureYouTube(modelBuilder);

        SeedReferenceData(modelBuilder);
        SeedSystemDefaults(modelBuilder);
    }

    private static void ConfigureMediaHierarchy(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<MediaItem>(entity =>
        {
            entity.HasDiscriminator<string>("MediaType")
                .HasValue<Movie>("Movie")
                .HasValue<TvShow>("TvShow")
                .HasValue<Season>("Season")
                .HasValue<Episode>("Episode")
                .HasValue<Track>("Track");

            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SortTitle).HasMaxLength(500);
            entity.Property(e => e.OriginalTitle).HasMaxLength(500);
            entity.Property(e => e.OriginalLanguage).HasMaxLength(8);
            entity.Property(e => e.Edition).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(32);
            entity.Property(e => e.HomePage).HasMaxLength(1024);
            entity.Property(e => e.ContentRating).HasMaxLength(32);
            entity.Property(e => e.TmdbId).HasMaxLength(64);
            entity.Property(e => e.ImdbId).HasMaxLength(64);
            entity.Property(e => e.TvdbId).HasMaxLength(64);
            entity.Property(e => e.ThirdPartyRating1Name).HasMaxLength(64);
            entity.Property(e => e.ThirdPartyRating2Name).HasMaxLength(64);
            entity.Property(e => e.PosterUrl).HasMaxLength(1024);
            entity.Property(e => e.OriginalPosterUrl).HasMaxLength(1024);
            entity.Property(e => e.BackgroundUrl).HasMaxLength(1024);
            entity.HasIndex(e => e.TmdbId).HasFilter("\"TmdbId\" IS NOT NULL");
            entity.HasIndex(e => e.ImdbId).HasFilter("\"ImdbId\" IS NOT NULL");
            entity.HasIndex(e => e.TvdbId).HasFilter("\"TvdbId\" IS NOT NULL");
            entity.HasIndex("LibraryId", "MediaType");
            entity.HasIndex(e => e.MissingSince).HasFilter("\"MissingSince\" IS NOT NULL");
        });

        modelBuilder.Entity<TvShow>(entity =>
        {
            entity.Property(e => e.TvType).HasMaxLength(32);
            entity.Property(e => e.LastEpisodeToAirName).HasMaxLength(500);
            entity.Property(e => e.NextEpisodeToAirName).HasMaxLength(500);
        });

        modelBuilder.Entity<Track>(entity =>
        {
            entity.Property(e => e.AudioCodec).HasMaxLength(32);
            entity.Property(e => e.ExternalLyricsPath).HasMaxLength(1024);
            entity.Property(e => e.Artist).HasMaxLength(500);

            entity.HasOne(e => e.Album)
                  .WithMany(a => a.Tracks)
                  .HasForeignKey(e => e.AlbumId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Artist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SortName).HasMaxLength(500);
            entity.Property(e => e.Biography).HasMaxLength(8000);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);
            entity.Property(e => e.BackgroundUrl).HasMaxLength(2048);
            entity.Property(e => e.BannerUrl).HasMaxLength(2048);
            entity.Property(e => e.ClearLogoUrl).HasMaxLength(2048);

            entity.HasOne(e => e.Library)
                  .WithMany()
                  .HasForeignKey(e => e.LibraryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.LibraryId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<TrackLike>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.TrackId }).IsUnique();
            entity.HasIndex(e => e.ProfileId);

            entity.HasOne(e => e.Track)
                  .WithMany()
                  .HasForeignKey(e => e.TrackId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackPlayHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.PlayedAt });
            entity.HasIndex(e => new { e.ProfileId, e.TrackId });

            entity.HasOne(e => e.Track)
                  .WithMany()
                  .HasForeignKey(e => e.TrackId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Album>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SortTitle).HasMaxLength(500);
            entity.Property(e => e.Genre).HasMaxLength(200);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);
            entity.Property(e => e.BackgroundUrl).HasMaxLength(2048);
            entity.Property(e => e.DiscArtUrl).HasMaxLength(2048);
            entity.Property(e => e.AlbumArtist).HasMaxLength(500);

            entity.HasOne(e => e.Artist)
                  .WithMany(a => a.Albums)
                  .HasForeignKey(e => e.ArtistId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Library)
                  .WithMany()
                  .HasForeignKey(e => e.LibraryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ArtistId, e.Title }).IsUnique();
        });

        modelBuilder.Entity<GeneratedMix>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DescriptionTag).HasMaxLength(100);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.TrackOrder)
                  .HasConversion(converters.GuidList)
                  .Metadata.SetValueComparer(converters.GuidListComparer);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ProfileId, e.Kind, e.Slot }).IsUnique();
            entity.HasIndex(e => e.ProfileId);
        });

        modelBuilder.Entity<Station>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SeedKind).HasConversion<int>();
            entity.Property(e => e.SeedGenre).HasMaxLength(200);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ProfileId);
            entity.HasIndex(e => new { e.ProfileId, e.LastPlayedAt });
        });

        modelBuilder.Entity<ArtistSimilarity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SimilarArtistName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.HasIndex(e => e.ArtistId);
        });

        modelBuilder.Entity<ArtistTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Tag).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.HasIndex(e => e.ArtistId);
            entity.HasIndex(e => e.Tag);
        });
    }

    private static void ConfigureMediaParts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaPart>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Container).HasMaxLength(32);
            entity.Property(e => e.Resolution).HasMaxLength(16);
            entity.Property(e => e.VersionName).HasMaxLength(128);
            entity.Property(e => e.Edition).HasMaxLength(64);

            entity.HasMany(e => e.VideoTracks)
                  .WithOne(e => e.MediaPart)
                  .HasForeignKey(e => e.MediaPartId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.AudioTracks)
                  .WithOne(e => e.MediaPart)
                  .HasForeignKey(e => e.MediaPartId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.SubtitleTracks)
                  .WithOne(e => e.MediaPart)
                  .HasForeignKey(e => e.MediaPartId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaVideoTrack>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Codec).HasMaxLength(32);
            entity.Property(e => e.Profile).HasMaxLength(64);
            entity.Property(e => e.HdrType).HasMaxLength(32);
        });

        modelBuilder.Entity<MediaAudioTrack>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Codec).HasMaxLength(32);
            entity.Property(e => e.Language).HasMaxLength(8);
            entity.Property(e => e.Title).HasMaxLength(256);
        });

        modelBuilder.Entity<MediaSubtitleTrack>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Codec).HasMaxLength(32);
            entity.Property(e => e.Language).HasMaxLength(8);
            entity.Property(e => e.Title).HasMaxLength(256);
        });
    }

    private static void ConfigureMediaArtwork(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaArtwork>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Language).HasMaxLength(8);
            entity.Property(e => e.ProviderId).IsRequired().HasMaxLength(64);

            entity.HasOne(e => e.MediaItem)
                  .WithMany(m => m.Artwork)
                  .HasForeignKey(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionArtwork>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Language).HasMaxLength(8);
            entity.Property(e => e.ProviderId).IsRequired().HasMaxLength(64);

            entity.HasOne(e => e.Collection)
                  .WithMany()
                  .HasForeignKey(e => e.CollectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureMediaVideo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaVideo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VideoKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Site).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(32);

            entity.HasOne(v => v.MediaItem)
                  .WithMany(m => m.Videos)
                  .HasForeignKey(v => v.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureMediaExtra(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaExtra>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ExtraType)
                  .HasConversion<string>()
                  .HasMaxLength(32)
                  .IsRequired();

            entity.HasOne(e => e.MediaItem)
                  .WithMany(m => m.Extras)
                  .HasForeignKey(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Parts)
                  .WithOne(p => p.MediaExtra)
                  .HasForeignKey(p => p.MediaExtraId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.MediaItemId);
        });
    }

    private static void ConfigureMediaItemAnalysis(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItemAnalysis>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.MediaItem)
                  .WithOne(m => m.Analysis)
                  .HasForeignKey<MediaItemAnalysis>(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureMediaItemMarkers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItemMarker>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Type)
                  .HasConversion<string>()
                  .HasMaxLength(32)
                  .IsRequired();

            entity.HasOne(e => e.MediaItem)
                  .WithMany(m => m.Markers)
                  .HasForeignKey(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.MediaItemId, e.Type, e.Order });
        });
    }

    private static void ConfigureMediaItemAudioFingerprints(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItemAudioFingerprint>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.MediaItem)
                  .WithOne()
                  .HasForeignKey<MediaItemAudioFingerprint>(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.MediaItemId).IsUnique();
        });
    }

    private static void ConfigureMediaItemRelationships(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<MediaItem>()
            .HasMany(m => m.MediaParts)
            .WithOne(p => p.MediaItem)
            .HasForeignKey(p => p.MediaItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MediaItem>()
            .HasMany(m => m.Genres)
            .WithMany(g => g.MediaItems)
            .UsingEntity(j => j.ToTable("MediaItemGenres"));

        modelBuilder.Entity<MediaItem>()
            .HasMany(m => m.ProductionCompanies)
            .WithMany(c => c.MediaItems)
            .UsingEntity(j => j.ToTable("MediaItemCompanies"));

        modelBuilder.Entity<MediaItem>()
            .HasMany(m => m.OriginCountries)
            .WithMany(c => c.MediaItems)
            .UsingEntity(j => j.ToTable("MediaItemCountries"));

        modelBuilder.Entity<TvShow>()
            .HasMany(t => t.Networks)
            .WithMany(n => n.TvShows)
            .UsingEntity(j => j.ToTable("TvShowNetworks"));

        modelBuilder.Entity<MediaItem>()
            .Property(e => e.LockedFields)
            .HasConversion(converters.StringList)
            .Metadata.SetValueComparer(converters.StringListComparer);

        modelBuilder.Entity<Artist>()
            .Property(e => e.LockedFields)
            .HasConversion(converters.StringList)
            .Metadata.SetValueComparer(converters.StringListComparer);

        modelBuilder.Entity<Album>()
            .Property(e => e.LockedFields)
            .HasConversion(converters.StringList)
            .Metadata.SetValueComparer(converters.StringListComparer);
    }

    private static void ConfigureReferenceTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Genre>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.LogoPath).HasMaxLength(1024);
            entity.Property(e => e.OriginCountry).HasMaxLength(8);
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.Property(e => e.Iso3166_1).HasMaxLength(8);
            entity.Property(e => e.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<Network>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.LogoPath).HasMaxLength(1024);
            entity.Property(e => e.OriginCountry).HasMaxLength(8);
        });
    }

    private static void ConfigureActors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Actor>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ProfileImageUrl).HasMaxLength(1024);
            entity.Property(e => e.PlaceOfBirth).HasMaxLength(256);
            entity.Property(e => e.HomePage).HasMaxLength(1024);
            entity.Property(e => e.ImdbId).HasMaxLength(64);
        });

        modelBuilder.Entity<MediaCastMember>(entity =>
        {
            entity.HasKey(mc => new { mc.ActorId, mc.MediaItemId });
            entity.Property(e => e.CharacterName).HasMaxLength(500);

            entity.HasOne(mc => mc.Actor)
                  .WithMany(a => a.Roles)
                  .HasForeignKey(mc => mc.ActorId);

            entity.HasOne(mc => mc.MediaItem)
                  .WithMany(m => m.Cast)
                  .HasForeignKey(mc => mc.MediaItemId);
        });
    }

    private static void ConfigureLibrary(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<MediaLibrary>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.ScannerRegex).HasMaxLength(1024);
            entity.Property(e => e.MetadataProviderId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ArtworkProviderId).HasMaxLength(64);
            entity.Property(e => e.ThirdPartyRating1ProviderId).HasMaxLength(64);
            entity.Property(e => e.ThirdPartyRating2ProviderId).HasMaxLength(64);

            entity.Property(e => e.FolderPaths)
                  .HasConversion(converters.StringList)
                  .Metadata.SetValueComparer(converters.StringListComparer);

            entity.Property(e => e.ExcludeFilters)
                  .HasConversion(converters.StringList)
                  .Metadata.SetValueComparer(converters.StringListComparer);

            entity.HasMany(l => l.MediaItems)
                  .WithOne(m => m.Library)
                  .HasForeignKey(m => m.LibraryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCollections(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SortTitle).HasMaxLength(500);
            entity.Property(e => e.PosterUrl).HasMaxLength(1024);
            entity.Property(e => e.BackdropUrl).HasMaxLength(1024);
            entity.Property(e => e.ImdbId).HasMaxLength(64);
            entity.Property(e => e.TvdbId).HasMaxLength(64);
            entity.Property(e => e.SortProviderId).HasMaxLength(64);
            entity.Property(e => e.ExternalListId).HasMaxLength(128);
            entity.Property(e => e.ContentSyncProviderId).HasMaxLength(64);
            entity.Property(e => e.ContentSyncExternalId).HasMaxLength(128);
            entity.Property(e => e.SyncIntervalDays).HasDefaultValue(1);

            entity.Property(e => e.LockedFields)
                  .HasConversion(converters.StringList)
                  .Metadata.SetValueComparer(converters.StringListComparer);
        });

        modelBuilder.Entity<Collection>()
            .HasMany(c => c.Items)
            .WithMany(m => m.Collections)
            .UsingEntity<CollectionItem>(
                j => j
                    .HasOne(ci => ci.MediaItem)
                    .WithMany()
                    .HasForeignKey(ci => ci.MediaItemId),
                j => j
                    .HasOne(ci => ci.Collection)
                    .WithMany()
                    .HasForeignKey(ci => ci.CollectionId),
                j =>
                {
                    j.HasKey(ci => new { ci.CollectionId, ci.MediaItemId });
                    j.ToTable("CollectionItems");
                });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Nickname).HasMaxLength(64);
            entity.Property(e => e.PasswordHash).HasMaxLength(256);
            entity.Property(e => e.SecurityStamp).IsRequired().HasMaxLength(32);
            entity.Property(e => e.EmailNotifyOnRequestAvailable).HasDefaultValue(true);

            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.SecurityStamp);

            entity.Property(e => e.AllowedLibraryIds)
                  .HasConversion(converters.GuidList)
                  .Metadata.SetValueComparer(converters.GuidListComparer);

            entity.Property(e => e.AllowedIptvPlaylistIds)
                  .HasConversion(converters.GuidList)
                  .Metadata.SetValueComparer(converters.GuidListComparer);
        });

        modelBuilder.Entity<UserProviderConnection>(entity =>
        {
            entity.Property(e => e.ProviderName).IsRequired().HasMaxLength(64);
            entity.Property(e => e.AccessToken).IsRequired().HasMaxLength(2048);
            entity.Property(e => e.RefreshToken).HasMaxLength(2048);

            entity.HasOne(upc => upc.User)
                  .WithMany(u => u.ProviderConnections)
                  .HasForeignKey(upc => upc.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserMediaState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.MediaItemId }).IsUnique();
        });

        modelBuilder.Entity<UserMediaRating>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.MediaItemId }).IsUnique();
            entity.HasIndex(e => e.MediaItemId);

            entity.HasOne(e => e.MediaItem)
                  .WithMany()
                  .HasForeignKey(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PreservedUserMediaData>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContentKey).IsRequired().HasMaxLength(256);
            entity.HasIndex(e => new { e.ProfileId, e.ContentKey }).IsUnique();
            entity.HasIndex(e => e.ContentKey);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAlbumRating>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.AlbumId }).IsUnique();
            entity.HasIndex(e => e.AlbumId);

            entity.HasOne(e => e.Album)
                  .WithMany()
                  .HasForeignKey(e => e.AlbumId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserArtistRating>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.ArtistId }).IsUnique();
            entity.HasIndex(e => e.ArtistId);

            entity.HasOne(e => e.Artist)
                  .WithMany()
                  .HasForeignKey(e => e.ArtistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureProfiles(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProfileImageUrl).HasMaxLength(1024);
            entity.Property(e => e.PinHash).HasMaxLength(256);
            entity.Property(e => e.SecurityStamp).IsRequired().HasMaxLength(32);
            entity.Property(e => e.LastFmSessionKey).HasMaxLength(128);
            entity.Property(e => e.LastFmUsername).HasMaxLength(128);

            entity.HasIndex(e => e.SecurityStamp);

            entity.Property(e => e.AllowedLibraryIds)
                  .HasConversion(converters.GuidList)
                  .Metadata.SetValueComparer(converters.GuidListComparer);

            entity.Property(e => e.AllowedIptvPlaylistIds)
                  .HasConversion(converters.GuidList)
                  .Metadata.SetValueComparer(converters.GuidListComparer);

            entity.Property(e => e.AllowedMovieRatings)
                  .HasConversion(converters.StringList)
                  .Metadata.SetValueComparer(converters.StringListComparer);

            entity.Property(e => e.AllowedTvRatings)
                  .HasConversion(converters.StringList)
                  .Metadata.SetValueComparer(converters.StringListComparer);

            entity.Property(e => e.AllowedMusicRatings)
                  .HasConversion(converters.StringList)
                  .Metadata.SetValueComparer(converters.StringListComparer);
        });
    }

    private static void ConfigureProfileSchedules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileAccessSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.UserProfile)
                  .WithMany(p => p.AccessSchedules)
                  .HasForeignKey(e => e.UserProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureProfileDeviceSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileDeviceSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceId).IsRequired().HasMaxLength(128);

            entity.HasIndex(e => new { e.ProfileId, e.DeviceId }).IsUnique();

            entity.HasOne(e => e.Profile)
                  .WithMany(p => p.DeviceSettings)
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRegistrationTickets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegistrationTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SecretCode).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.SecretCode).IsUnique();
        });
    }

    private static void ConfigurePasswordResetTickets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PasswordResetTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureEmailChangeTickets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailChangeTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
            entity.Property(e => e.NewEmail).IsRequired().HasMaxLength(320);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureInvitationTickets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvitationTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.Email);
        });
    }

    private static void ConfigureClientDevices(ModelBuilder modelBuilder, ListValueConverters converters)
    {
        modelBuilder.Entity<ClientDevice>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DeviceId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.ClientName).HasMaxLength(64);
            entity.Property(e => e.DeviceName).HasMaxLength(128);
            entity.Property(e => e.DeviceType).HasMaxLength(32);
            entity.Property(e => e.OperatingSystem).HasMaxLength(64);
            entity.Property(e => e.LastIpAddress).HasMaxLength(45);
            entity.Property(e => e.Location).HasMaxLength(128);

            entity.Property(e => e.SupportedVideoCodecs).HasConversion(converters.StringList).Metadata.SetValueComparer(converters.StringListComparer);
            entity.Property(e => e.SupportedAudioCodecs).HasConversion(converters.StringList).Metadata.SetValueComparer(converters.StringListComparer);
            entity.Property(e => e.SupportedContainers).HasConversion(converters.StringList).Metadata.SetValueComparer(converters.StringListComparer);
            entity.Property(e => e.SupportedHdrFormats).HasConversion((ValueConverter)converters.StringList).Metadata.SetValueComparer(converters.StringListComparer);

            entity.HasIndex(e => e.DeviceId).IsUnique();
        });
    }

    private static void ConfigurePlaylists(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Playlist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.MediaType).HasConversion<int>().HasDefaultValue(PlaylistMediaType.Mixed).HasSentinel(PlaylistMediaType.Mixed);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaylistItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PlaylistId, e.Order });

            entity.HasOne(e => e.Playlist)
                  .WithMany(p => p.Items)
                  .HasForeignKey(e => e.PlaylistId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MediaItem)
                  .WithMany()
                  .HasForeignKey(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SmartPlaylist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(1024);
            entity.Property(e => e.RulesJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.SortBy).IsRequired().HasMaxLength(64);
            entity.Property(e => e.SortDirection).IsRequired().HasMaxLength(8);
            entity.Property(e => e.MediaType).HasConversion<int>().HasDefaultValue(PlaylistMediaType.Music).HasSentinel(PlaylistMediaType.Music);

            entity.HasIndex(e => e.ProfileId);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSmartLists(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SmartList>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.FilterRulesJson).IsRequired().HasDefaultValue("{}");

            entity.HasOne<MediaLibrary>()
                  .WithMany()
                  .HasForeignKey(e => e.LibraryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.Collection)
                  .WithMany()
                  .HasForeignKey(s => s.CollectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureIptv(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IptvPlaylist>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.M3uUrl).HasMaxLength(1024);
            entity.Property(e => e.CountryFilter).HasMaxLength(8);

            entity.HasOne(p => p.TunerProfile)
                  .WithOne(t => t.Playlist)
                  .HasForeignKey<IptvTunerProfile>(t => t.PlaylistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IptvEpgSource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.XmlTvUrl).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.LastError).HasColumnType("text");
        });

        modelBuilder.Entity<IptvChannel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalChannelId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.StreamUrl).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.LogoUrl).HasMaxLength(1024);
            entity.Property(e => e.GroupTitle).HasMaxLength(128);
            entity.Property(e => e.Resolution).HasMaxLength(16);
            entity.Property(e => e.CountryCode).HasMaxLength(8);

            entity.HasIndex(e => new { e.PlaylistId, e.ExternalChannelId }).IsUnique();

            entity.HasOne(e => e.Playlist)
                  .WithMany(p => p.Channels)
                  .HasForeignKey(e => e.PlaylistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IptvTunerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<IptvRecordingSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ProgramId).HasMaxLength(128);

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Profile).WithMany().HasForeignKey(e => e.ProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Channel).WithMany().HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IptvRecordingSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.EpisodeTitle).HasMaxLength(500);
            entity.Property(e => e.ExternalProgramId).HasMaxLength(128);
            entity.Property(e => e.OutputFilePath).HasMaxLength(1024);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.StartTime });
            entity.HasIndex(e => e.EndTime);

            entity.HasOne(e => e.Schedule)
                  .WithMany(s => s.Sessions)
                  .HasForeignKey(e => e.ScheduleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePodcasts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PodcastShow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FeedUrl).IsRequired().HasMaxLength(2048);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Author).HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);
            entity.Property(e => e.HomepageUrl).HasMaxLength(2048);
            entity.Property(e => e.Language).HasMaxLength(16);
            entity.Property(e => e.LastError).HasMaxLength(1024);

            entity.HasIndex(e => e.FeedUrl).IsUnique();
        });

        modelBuilder.Entity<PodcastEpisode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalGuid).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).HasMaxLength(8000);
            entity.Property(e => e.AudioUrl).IsRequired().HasMaxLength(2048);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);

            entity.HasIndex(e => new { e.PodcastShowId, e.ExternalGuid }).IsUnique();

            entity.HasOne(e => e.Show)
                  .WithMany(s => s.Episodes)
                  .HasForeignKey(e => e.PodcastShowId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PodcastSubscription>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.ProfileId, e.PodcastShowId }).IsUnique();

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Show)
                  .WithMany(s => s.Subscriptions)
                  .HasForeignKey(e => e.PodcastShowId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PodcastEpisodeProfileState>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.ProfileId, e.PodcastEpisodeId }).IsUnique();

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Episode)
                  .WithMany()
                  .HasForeignKey(e => e.PodcastEpisodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureStreaming(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StreamSession>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Strategy).IsRequired().HasMaxLength(32);
            entity.Property(e => e.VideoStrategy).IsRequired().HasMaxLength(32);
            entity.Property(e => e.AudioStrategy).IsRequired().HasMaxLength(32);
            entity.Property(e => e.SubtitleStrategy).IsRequired().HasMaxLength(32);
            entity.Property(e => e.VideoCodec).HasMaxLength(32);
            entity.Property(e => e.AudioCodec).HasMaxLength(32);
            entity.Property(e => e.Container).HasMaxLength(32);
            entity.Property(e => e.Resolution).HasMaxLength(16);
            entity.Property(e => e.HdrType).HasMaxLength(32);
            entity.Property(e => e.Quality).HasMaxLength(64);

            entity.HasIndex(e => new { e.UserId, e.StartedAt }).HasFilter("\"EndedAt\" IS NULL");
            entity.HasIndex(e => e.LastPingAt).HasFilter("\"EndedAt\" IS NULL");
        });
    }

    private static void ConfigureRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaRequest>(entity =>
        {
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.PosterUrl).HasMaxLength(1024);
            entity.Property(e => e.ExternalId).HasMaxLength(64);
            entity.Property(e => e.Type).HasMaxLength(32);
            entity.Property(e => e.ProviderId).HasMaxLength(64);
        });

        modelBuilder.Entity<MediaRequestUser>(entity =>
        {
            entity.HasKey(mru => new { mru.RequestId, mru.ProfileId });
        });

        modelBuilder.Entity<RequestServer>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(128);
            entity.Property(e => e.ProviderId).HasMaxLength(64);
            entity.Property(e => e.MediaType).HasMaxLength(32);
            entity.Property(e => e.Hostname).HasMaxLength(256);
            entity.Property(e => e.ApiKey).HasMaxLength(256);
            entity.Property(e => e.UrlBase).HasMaxLength(256);
        });
    }

    private static void ConfigurePosters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OverlayTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.TargetMediaType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.ConfigurationJson).IsRequired();
        });
    }

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerSetting>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(64);
            entity.Property(e => e.ServerName).HasMaxLength(128);
            entity.Property(e => e.TmdbApiKey).HasMaxLength(256);
            entity.Property(e => e.TvdbApiKey).HasMaxLength(256);
            entity.Property(e => e.TvdbToken).HasMaxLength(2048);
            entity.Property(e => e.FolderWatcherProviderId).HasMaxLength(64);
            entity.Property(e => e.LocalMediaScannerProviderId).HasMaxLength(64);
            entity.Property(e => e.TranscoderTempDirectory).HasMaxLength(1024);
            entity.Property(e => e.HardwareTranscodingDevice).HasMaxLength(32);
            entity.Property(e => e.MetadataLanguage).HasMaxLength(16);
            entity.Property(e => e.TonemappingAlgorithm).HasMaxLength(32);
            entity.Property(e => e.SmtpHost).HasMaxLength(256);
            entity.Property(e => e.SmtpUsername).HasMaxLength(256);
            entity.Property(e => e.SmtpPasswordCiphertext).HasColumnType("text");
            entity.Property(e => e.SmtpFromAddress).HasMaxLength(256);
            entity.Property(e => e.SmtpFromDisplayName).HasMaxLength(128);
            entity.Property(e => e.EmailPublicBaseUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<PluginSettingValue>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PluginId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(64);

            entity.HasIndex(e => new { e.PluginId, e.Key }).IsUnique();
        });
    }

    private static void ConfigureEmail(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasConversion<string>().HasMaxLength(64);
            entity.Property(e => e.SubjectOverride).HasMaxLength(256);
            entity.Property(e => e.HtmlBodyOverride).HasColumnType("text");
            entity.Property(e => e.TextBodyOverride).HasColumnType("text");
        });

        modelBuilder.Entity<EmailDeliveryLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TemplateKey).HasConversion<string>().HasMaxLength(64);
            entity.Property(e => e.ToAddress).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Subject).HasMaxLength(256);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.TemplateKey, e.CreatedAt });
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
        });
    }

    private static void ConfigureClientTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.Property(e => e.ClientTemplateId).HasMaxLength(64);
            entity.Property(e => e.ScheduleOverrideTemplateId).HasMaxLength(64);
        });

        modelBuilder.Entity<ServerSetting>(entity =>
        {
            entity.Property(e => e.DefaultClientTemplateId).HasMaxLength(64);
        });

        modelBuilder.Entity<ClientTemplateSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TemplateId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);

            entity.HasIndex(e => new { e.Enabled, e.StartsAtUtc, e.EndsAtUtc });
            entity.HasIndex(e => e.TemplateId);
        });
    }

    private static void ConfigureMediaDedupe(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaDedupeSettings>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Library)
                .WithMany()
                .HasForeignKey(e => e.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.LibraryId).IsUnique();
        });

        modelBuilder.Entity<MediaDedupeIgnoredGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Resolution).IsRequired().HasMaxLength(32);
            entity.Property(e => e.IgnoredByProfileId).HasMaxLength(64);
            entity.Property(e => e.Note).HasMaxLength(512);

            entity.HasOne(e => e.MediaItem)
                .WithMany()
                .HasForeignKey(e => e.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.MediaItemId, e.Resolution }).IsUnique();
        });
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        var eventComparer = new ValueComparer<List<WebhookEventType>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList()
        );

        modelBuilder.Entity<WebhookConfig>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.PayloadUrl).IsRequired().HasMaxLength(1024);

            entity.Property(w => w.SubscribedEvents)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<WebhookEventType>>(v, (JsonSerializerOptions?)null) ?? new List<WebhookEventType>()
                )
                .Metadata.SetValueComparer(eventComparer);
        });
    }

    private static void ConfigureAiUsage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PluginId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ModelUsed).HasMaxLength(64);

            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Vora.Domain.Entities.Notifications.AdminNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IsRead, e.CreatedAt });
        });

        modelBuilder.Entity<SystemMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
        });
    }

    private static void ConfigureMediaItemEmbeddings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItemEmbedding>(entity =>
        {
            entity.HasKey(e => e.MediaItemId);

            entity.Property(e => e.Embedding).HasColumnType("vector(1536)");

            entity.HasIndex(e => e.Embedding)
                  .HasMethod("hnsw")
                  .HasOperators("vector_cosine_ops");

            entity.HasOne(e => e.MediaItem)
                  .WithOne()
                  .HasForeignKey<MediaItemEmbedding>(e => e.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDiscovery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscoveryRowConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RowId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProviderId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);

            entity.HasIndex(e => e.OrderIndex);
        });

        modelBuilder.Entity<UserWatchlistItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProviderId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.PosterUrl).HasMaxLength(1024);

            entity.HasIndex(e => e.ProfileId);
            entity.HasIndex(e => new { e.ProfileId, e.ExternalId, e.ProviderId }).IsUnique();
        });
    }

    private static void ConfigureYouTube(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<YouTubeAccountSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.YouTubeAccess).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(e => e.AccountId).IsUnique();
        });

        modelBuilder.Entity<YouTubeProfileSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserProfileId).IsUnique();
        });

        modelBuilder.Entity<YouTubeSubscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChannelId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ChannelName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ChannelThumbnailUrl).HasMaxLength(1024);

            entity.HasIndex(e => new { e.UserProfileId, e.ChannelId }).IsUnique();
            entity.HasIndex(e => e.UserProfileId);
        });

        modelBuilder.Entity<YouTubeWatchHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VideoId).IsRequired().HasMaxLength(32);
            entity.Property(e => e.VideoTitle).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ThumbnailUrl).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.ChannelId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ChannelName).IsRequired().HasMaxLength(256);

            entity.HasIndex(e => new { e.UserProfileId, e.WatchedAt });
            entity.HasIndex(e => new { e.UserProfileId, e.VideoId });
        });
    }

    private static void SeedReferenceData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Genre>().HasData(
            new Genre { Id = 28, Name = "Action" },
            new Genre { Id = 12, Name = "Adventure" },
            new Genre { Id = 16, Name = "Animation" },
            new Genre { Id = 35, Name = "Comedy" },
            new Genre { Id = 80, Name = "Crime" },
            new Genre { Id = 99, Name = "Documentary" },
            new Genre { Id = 18, Name = "Drama" },
            new Genre { Id = 10751, Name = "Family" },
            new Genre { Id = 14, Name = "Fantasy" },
            new Genre { Id = 36, Name = "History" },
            new Genre { Id = 27, Name = "Horror" },
            new Genre { Id = 10402, Name = "Music" },
            new Genre { Id = 9648, Name = "Mystery" },
            new Genre { Id = 10749, Name = "Romance" },
            new Genre { Id = 878, Name = "Science Fiction" },
            new Genre { Id = 10770, Name = "TV Movie" },
            new Genre { Id = 53, Name = "Thriller" },
            new Genre { Id = 10752, Name = "War" },
            new Genre { Id = 37, Name = "Western" },
            new Genre { Id = 10759, Name = "Action & Adventure" },
            new Genre { Id = 10762, Name = "Kids" },
            new Genre { Id = 10763, Name = "News" },
            new Genre { Id = 10764, Name = "Reality" },
            new Genre { Id = 10765, Name = "Sci-Fi & Fantasy" },
            new Genre { Id = 10766, Name = "Soap" },
            new Genre { Id = 10767, Name = "Talk" },
            new Genre { Id = 10768, Name = "War & Politics" }
        );

        modelBuilder.Entity<SmartList>().HasData(
            new SmartList
            {
                Id = Guid.Parse("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                Title = "Recently Released Movies & Shows",
                IsSystemList = true,
                IsSpotlight = true,
                ShowOnHomepage = true,
                ShowToFriends = true,
                SortBy = SmartListSortBy.ReleaseDateDesc,
                MaxItems = 20,
                DisplayOrder = 0,
                FilterRulesJson = "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\"]}"
            },
            new SmartList
            {
                Id = Guid.Parse("17ddede2-2de0-42b8-9b33-32708b4d29b8"),
                Title = "Recently Added Movies & Shows",
                IsSystemList = true,
                ShowOnHomepage = true,
                ShowToFriends = true,
                SortBy = SmartListSortBy.DateAddedDesc,
                MaxItems = 20,
                DisplayOrder = 1,
                FilterRulesJson = "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\"]}"
            },
            new SmartList
            {
                Id = Guid.Parse("ebbefd92-4232-4cae-9c5d-2134943b8bf8"),
                Title = "Recently Released Movies",
                IsSystemList = true,
                ShowOnHomepage = true,
                ShowToFriends = true,
                SortBy = SmartListSortBy.ReleaseDateDesc,
                MaxItems = 20,
                DisplayOrder = 2,
                FilterRulesJson = "{\"mediaTypes\":[\"Movie\"]}"
            },
            new SmartList
            {
                Id = Guid.Parse("c88d6c8a-57ea-4b24-a7be-3f2638a38aca"),
                Title = "Recently Added Movies",
                IsSystemList = true,
                ShowOnHomepage = true,
                ShowToFriends = true,
                SortBy = SmartListSortBy.DateAddedDesc,
                MaxItems = 20,
                DisplayOrder = 3,
                FilterRulesJson = "{\"mediaTypes\":[\"Movie\"]}"
            },
            new SmartList
            {
                Id = Guid.Parse("58424b85-b6da-4a9c-8204-e364f1319508"),
                Title = "Recently Released Shows",
                IsSystemList = true,
                ShowOnHomepage = true,
                ShowToFriends = true,
                SortBy = SmartListSortBy.ReleaseDateDesc,
                MaxItems = 20,
                DisplayOrder = 4,
                FilterRulesJson = "{\"mediaTypes\":[\"TvShow\"]}"
            },
            new SmartList
            {
                Id = Guid.Parse("dfc420d4-421c-4e14-aec4-a5bedefd2f2e"),
                Title = "Recently Added Shows",
                IsSystemList = true,
                ShowOnHomepage = true,
                ShowToFriends = true,
                SortBy = SmartListSortBy.DateAddedDesc,
                MaxItems = 20,
                DisplayOrder = 5,
                FilterRulesJson = "{\"mediaTypes\":[\"TvShow\"]}"
            }
        );
    }

    private static void SeedSystemDefaults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerSetting>().HasData(
            new ServerSetting { EnableNightlyScan = false, RunDetections = DetectionTrigger.Never }
        );

        
    }

    private sealed class ListValueConverters
    {
        public ValueConverter<List<string>, string> StringList { get; }
        public ValueComparer<List<string>> StringListComparer { get; }
        public ValueConverter<List<Guid>, string> GuidList { get; }
        public ValueComparer<List<Guid>> GuidListComparer { get; }

        public ListValueConverters()
        {
            StringList = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            );

            StringListComparer = new ValueComparer<List<string>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()
            );

            GuidList = new ValueConverter<List<Guid>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<Guid>()
                    : JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            );

            GuidListComparer = new ValueComparer<List<Guid>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()
            );
        }
    }
}
