using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Podcasts;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Backups;
using Vora.Infrastructure.Backups.Sections;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Tests.Postgres;

namespace Vora.Infrastructure.Tests.Backups;

public sealed class FullRestoreTests
{
    private static readonly Guid Profile = BackupTestWorld.ProfileId;

    private static List<IBackupSection> SectionsInRestoreOrder(VoraDbContext db, BackupReferenceMapper references) =>
    [
        new UsersAndProfilesBackupSection(db, references),
        new DevicesBackupSection(db),
        new CollectionsBackupSection(db, references),
        new SmartListsBackupSection(db, references),
        new DedupeRulesBackupSection(db, references),
        new IptvPlaylistsBackupSection(db),
        new IptvTunerProfilesBackupSection(db),
        new IptvRecordingSchedulesBackupSection(db, references),
        new RequestServersBackupSection(db),
        new WatchHistoryBackupSection(db, references),
        new RatingsBackupSection(db, references),
        new PlaylistsBackupSection(db, references),
        new WatchlistsBackupSection(db, references),
        new StationsBackupSection(db, references),
        new MediaRequestsBackupSection(db),
        new ExternalConnectionsBackupSection(db),
        new ChannelFavoritesBackupSection(db),
        new PodcastShowsBackupSection(db),
        new PodcastListeningBackupSection(db)
    ];

    [Fact]
    public async Task A_full_restore_onto_a_rebuilt_postgres_server_commits_without_breaking_a_foreign_key()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);

        var sourceDatabase = new PostgresDatabase();
        var targetDatabase = new PostgresDatabase();
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();
        try
        {
            await RunFullRestoreAsync(sourceDatabase.NewContext, targetDatabase.NewContext, transactional: true);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_full_restore_maps_every_section_onto_a_rebuilt_server()
    {
        var sourceName = "full-restore-source-" + Guid.NewGuid().ToString("N");
        var targetName = "full-restore-target-" + Guid.NewGuid().ToString("N");

        await RunFullRestoreAsync(() => InMemory(sourceName), () => InMemory(targetName), transactional: false);
    }

    private static VoraDbContext InMemory(string name) =>
        new(new DbContextOptionsBuilder<VoraDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task RunFullRestoreAsync(Func<VoraDbContext> newSourceContext, Func<VoraDbContext> newTargetContext, bool transactional)
    {
        var ct = TestContext.Current.CancellationToken;

        var archive = new MemoryBackupArchive();
        await using (var sourceDb = newSourceContext())
        {
            var source = BackupTestWorld.Source(sourceDb);
            await SeedEverythingAsync(source, ct);
            foreach (var section in SectionsInRestoreOrder(sourceDb, source.References))
            {
                await section.WriteAsync(archive, ct);
            }
        }

        await using var targetDb = newTargetContext();
        var target = BackupTestWorld.RebuiltTarget(withDevice: true, db: targetDb);
        var extraProfile = new UserProfile { Name = "Made after the backup", UserId = BackupTestWorld.UserId };
        var orphanServer = new RequestServer { Name = "Sonarr" };
        targetDb.UserProfiles.Add(extraProfile);
        targetDb.RequestServers.Add(orphanServer);
        targetDb.StreamSessions.Add(new StreamSession { UserId = BackupTestWorld.UserId, UserProfileId = extraProfile.Id, ClientDeviceId = BackupTestWorld.DeviceId, MediaItemId = target.Matrix });
        targetDb.MediaRequests.Add(new MediaRequest { Title = "Assigned here", AssignedServerId = orphanServer.Id });
        await targetDb.SaveChangesAsync(ct);
        targetDb.ChangeTracker.Clear();

        var results = new List<BackupSectionImportResult>();
        IBackupTransaction? transaction = transactional ? await new EfBackupTransactionFactory(targetDb).BeginAsync(ct) : null;
        foreach (var section in SectionsInRestoreOrder(targetDb, target.References))
        {
            results.Add(await section.ReadAsync(archive, ct));
        }
        if (transaction != null)
        {
            await transaction.CommitAsync(ct);
            await transaction.DisposeAsync();
        }

        await using var check = newTargetContext();
        (await check.UserProfiles.Select(p => p.Id).ToListAsync(ct)).Should().Equal(Profile);
        (await check.UserMediaStates.Select(s => s.MediaItemId).ToListAsync(ct)).Should().BeEquivalentTo(new[] { target.Matrix, target.Pilot });
        (await check.StreamSessions.Select(s => s.MediaItemId).ToListAsync(ct)).Should().Equal(target.Matrix);
        (await check.TrackPlayHistory.Select(p => p.TrackId).ToListAsync(ct)).Should().Equal(target.ParanoidAndroid);
        (await check.TrackLikes.Select(l => l.TrackId).ToListAsync(ct)).Should().Equal(target.ParanoidAndroid);
        (await check.UserAlbumRatings.Select(r => r.AlbumId).ToListAsync(ct)).Should().Equal(target.OkComputer);
        (await check.UserArtistRatings.Select(r => r.ArtistId).ToListAsync(ct)).Should().Equal(target.Radiohead);
        (await check.PlaylistItems.Select(i => i.MediaItemId).ToListAsync(ct)).Should().Equal(target.Pilot);
        (await check.CollectionItems.Select(i => i.MediaItemId).ToListAsync(ct)).Should().Equal(target.Matrix);
        (await check.SmartLists.Where(l => l.Title == "New movies").Select(l => l.LibraryId).SingleAsync(ct)).Should().Be(target.MoviesLibrary);
        (await check.MediaDedupeIgnoredGroups.Select(g => g.MediaItemId).ToListAsync(ct)).Should().Equal(target.Matrix);
        (await check.MediaRequests.Select(r => r.Title).ToListAsync(ct)).Should().Equal("Dune");
        (await check.MediaRequests.Select(r => r.AssignedServerId).SingleAsync(ct)).Should().NotBeNull();
        (await check.MediaRequestUsers.CountAsync(ct)).Should().Be(1);
        (await check.Stations.Select(s => s.SeedArtistId).ToListAsync(ct)).Should().Equal(target.Radiohead);
        (await check.UserWatchlistItems.Select(w => w.MediaItemId).ToListAsync(ct)).Should().Equal(target.Matrix);
        (await check.UserProviderConnections.CountAsync(ct)).Should().Be(1);
        (await check.PodcastSubscriptions.CountAsync(ct)).Should().Be(1);
        (await check.PodcastEpisodeProfileStates.CountAsync(ct)).Should().Be(1);
        (await check.ProfileChannelFavorites.CountAsync(ct)).Should().Be(1);
        (await check.IptvTunerProfiles.CountAsync(ct)).Should().Be(1);
        (await check.IptvRecordingSchedules.CountAsync(ct)).Should().Be(0);

        var warnings = results.SelectMany(r => r.Warnings).ToList();
        warnings.Should().Contain(w => w.Contains("channel isn't on this server yet"));
        warnings.Should().Contain(w => w.StartsWith("1 watch-history row was skipped because its item isn't on this server."));
        results.Sum(r => r.RowsSkipped).Should().Be(2);
    }

    private static async Task SeedEverythingAsync(BackupTestWorld source, CancellationToken ct)
    {
        var db = source.Db;

        db.UserMediaStates.AddRange(
            new UserMediaState { ProfileId = Profile, MediaItemId = source.Matrix, ResumePositionSeconds = 1200 },
            new UserMediaState { ProfileId = Profile, MediaItemId = source.Pilot, IsPlayed = true },
            new UserMediaState { ProfileId = Profile, MediaItemId = source.OnlyOnSource, ResumePositionSeconds = 5 });
        db.StreamSessions.Add(new StreamSession { UserId = BackupTestWorld.UserId, UserProfileId = Profile, ClientDeviceId = BackupTestWorld.DeviceId, MediaItemId = source.Matrix });
        db.TrackPlayHistory.Add(new TrackPlayHistory { ProfileId = Profile, TrackId = source.ParanoidAndroid, Completed = true });
        db.PreservedUserMediaData.Add(new PreservedUserMediaData { ProfileId = Profile, ContentKey = "movie:tmdb:42", HasState = true });
        db.UserMediaRatings.Add(new UserMediaRating { ProfileId = Profile, MediaItemId = source.Matrix, Rating = 9 });
        db.UserAlbumRatings.Add(new UserAlbumRating { ProfileId = Profile, AlbumId = source.OkComputer, Rating = 10 });
        db.UserArtistRatings.Add(new UserArtistRating { ProfileId = Profile, ArtistId = source.Radiohead, Rating = 9 });
        db.TrackLikes.Add(new TrackLike { ProfileId = Profile, TrackId = source.ParanoidAndroid });
        db.UserProviderConnections.Add(new UserProviderConnection { UserId = BackupTestWorld.UserId, ProviderName = "trakt", AccessToken = "token" });

        var playlist = new Playlist { Name = "Binge", ProfileId = Profile };
        db.Playlists.Add(playlist);
        db.PlaylistItems.Add(new PlaylistItem { PlaylistId = playlist.Id, MediaItemId = source.Pilot, Order = 1 });
        db.SmartPlaylists.Add(new SmartPlaylist { Name = "Liked songs", ProfileId = Profile });
        db.UserWatchlistItems.Add(new UserWatchlistItem { Id = Guid.NewGuid(), ProfileId = Profile, Title = "The Matrix", ExternalId = "603", ProviderId = "tmdb_discovery", Type = "movie", MediaItemId = source.Matrix });
        db.Stations.Add(new Station { Name = "Radiohead Radio", ProfileId = Profile, SeedKind = StationSeedKind.Artist, SeedArtistId = source.Radiohead });

        var collection = new Collection { Title = "Favourites", LibraryId = source.MoviesLibrary };
        db.Collections.Add(collection);
        db.CollectionItems.Add(new CollectionItem { CollectionId = collection.Id, MediaItemId = source.Matrix, ManuallyAdded = true });
        db.SmartLists.Add(new SmartList { Title = "New movies", LibraryId = source.MoviesLibrary });
        db.MediaDedupeSettings.Add(new MediaDedupeSettings { LibraryId = source.MoviesLibrary });
        db.MediaDedupeIgnoredGroups.Add(new MediaDedupeIgnoredGroup { MediaItemId = source.Matrix, Resolution = "1080p" });

        var iptv = new IptvPlaylist { Name = "Freeview" };
        var channel = new IptvChannel { PlaylistId = iptv.Id, ExternalChannelId = "bbc1.uk", Name = "BBC One", StreamUrl = "http://tv/bbc1" };
        db.IptvPlaylists.Add(iptv);
        db.IptvChannels.Add(channel);
        db.IptvTunerProfiles.Add(new IptvTunerProfile { PlaylistId = iptv.Id, MaxConcurrentStreams = 2 });
        db.IptvRecordingSchedules.Add(new IptvRecordingSchedule { Title = "News", UserId = BackupTestWorld.UserId, ProfileId = Profile, ChannelId = channel.Id });
        db.ProfileChannelFavorites.Add(new ProfileChannelFavorite { ProfileId = Profile, PlaylistId = iptv.Id, ExternalChannelId = "bbc1.uk" });

        var server = new RequestServer { Name = "Radarr" };
        var request = new MediaRequest { Title = "Dune", AssignedServerId = server.Id };
        db.RequestServers.Add(server);
        db.MediaRequests.Add(request);
        db.MediaRequestUsers.Add(new MediaRequestUser { RequestId = request.Id, ProfileId = Profile });

        var show = new PodcastShow { FeedUrl = "https://feeds.example/hardfork", Title = "Hard Fork", IsInCatalog = true };
        var episode = new PodcastEpisode { PodcastShowId = show.Id, ExternalGuid = "ep-1", Title = "Episode 1", AudioUrl = "https://cdn/1.mp3" };
        db.PodcastShows.Add(show);
        db.PodcastEpisodes.Add(episode);
        db.PodcastSubscriptions.Add(new PodcastSubscription { ProfileId = Profile, PodcastShowId = show.Id });
        db.PodcastEpisodeProfileStates.Add(new PodcastEpisodeProfileState { ProfileId = Profile, PodcastEpisodeId = episode.Id, PositionSeconds = 300 });

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
