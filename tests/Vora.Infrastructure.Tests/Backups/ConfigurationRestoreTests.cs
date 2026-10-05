using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Podcasts;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Backups.Sections;

namespace Vora.Infrastructure.Tests.Backups;

public class ConfigurationRestoreTests
{
    private static readonly Guid Profile = BackupTestWorld.ProfileId;

    [Fact]
    public async Task Restoring_accounts_updates_kept_profiles_in_place_and_maps_library_access_by_name()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var unknownLibrary = Guid.NewGuid();
        var sourceProfile = await source.Db.UserProfiles.SingleAsync(TestContext.Current.CancellationToken);
        sourceProfile.Name = "Andy (restored)";
        sourceProfile.AllowedLibraryIds = new List<Guid> { source.MoviesLibrary, unknownLibrary };
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.UserProfiles.Add(new UserProfile { Name = "Made after the backup", UserId = BackupTestWorld.UserId });
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        await BackupTestWorld.CopyAsync(
            new UsersAndProfilesBackupSection(source.Db, source.References),
            new UsersAndProfilesBackupSection(target.Db, target.References));

        var profile = await target.Db.UserProfiles.SingleAsync(TestContext.Current.CancellationToken);
        profile.Id.Should().Be(Profile);
        profile.Name.Should().Be("Andy (restored)");
        profile.AllowedLibraryIds.Should().Equal(target.MoviesLibrary, unknownLibrary);
    }

    [Fact]
    public async Task Only_admin_made_collections_are_restored_and_their_titles_follow_the_rescan()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var favourites = new Collection { Title = "Andy's favourites", LibraryId = source.MoviesLibrary, ExcludedMediaIdsJson = $"[\"{source.HomeVideo}\"]" };
        var scanned = new Collection { Title = "The Matrix Collection", TmdbId = 2344, SystemGenerated = true };
        source.Db.Collections.AddRange(favourites, scanned);
        source.Db.CollectionItems.AddRange(
            new CollectionItem { CollectionId = favourites.Id, MediaItemId = source.Matrix, ManuallyAdded = true },
            new CollectionItem { CollectionId = favourites.Id, MediaItemId = source.OnlyOnSource, ManuallyAdded = true },
            new CollectionItem { CollectionId = scanned.Id, MediaItemId = source.Matrix });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var rescanned = new Collection { Title = "The Matrix Collection", TmdbId = 2344, SystemGenerated = true };
        target.Db.Collections.Add(rescanned);
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(
            new CollectionsBackupSection(source.Db, source.References),
            new CollectionsBackupSection(target.Db, target.References));

        var collections = await target.Db.Collections.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        collections.Select(c => c.Id).Should().BeEquivalentTo(new[] { favourites.Id, rescanned.Id });
        var restored = collections.Single(c => c.Id == favourites.Id);
        restored.LibraryId.Should().Be(target.MoviesLibrary);
        restored.ExcludedMediaIdsJson.Should().Be($"[\"{target.HomeVideo}\"]");
        var items = await target.Db.CollectionItems.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        items.Should().ContainSingle().Which.MediaItemId.Should().Be(target.Matrix);
        result.Warnings.Should().ContainSingle(w => w.StartsWith("1 collection entry was skipped because its item"));
    }

    [Fact]
    public async Task Smart_lists_follow_their_library_by_name_and_are_skipped_when_their_collection_is_gone()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var collection = new Collection { Title = "Only here", SystemGenerated = true };
        source.Db.Collections.Add(collection);
        source.Db.SmartLists.AddRange(
            new SmartList { Title = "New movies", LibraryId = source.MoviesLibrary },
            new SmartList { Title = "From a collection", LibraryId = source.MoviesLibrary, CollectionId = collection.Id });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new SmartListsBackupSection(source.Db, source.References),
            new SmartListsBackupSection(target.Db, target.References));

        var lists = await target.Db.SmartLists.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        lists.Should().ContainSingle(l => l.Title == "New movies").Which.LibraryId.Should().Be(target.MoviesLibrary);
        lists.Should().NotContain(l => l.Title == "From a collection");
        result.Warnings.Should().ContainSingle(w => w.StartsWith("1 smart list was skipped because its collection"));
    }

    [Fact]
    public async Task Dedupe_rules_and_ignored_duplicates_are_mapped_to_this_servers_library_and_items()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        source.Db.MediaDedupeSettings.Add(new MediaDedupeSettings { LibraryId = source.MoviesLibrary, RuntimeToleranceSeconds = 90 });
        source.Db.MediaDedupeIgnoredGroups.AddRange(
            new MediaDedupeIgnoredGroup { MediaItemId = source.Matrix, Resolution = "1080p" },
            new MediaDedupeIgnoredGroup { MediaItemId = source.OnlyOnSource, Resolution = "4k" });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new DedupeRulesBackupSection(source.Db, source.References),
            new DedupeRulesBackupSection(target.Db, target.References));

        (await target.Db.MediaDedupeSettings.SingleAsync(TestContext.Current.CancellationToken)).LibraryId.Should().Be(target.MoviesLibrary);
        (await target.Db.MediaDedupeIgnoredGroups.SingleAsync(TestContext.Current.CancellationToken)).MediaItemId.Should().Be(target.Matrix);
        result.RowsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task Recording_schedules_follow_their_channel_by_external_id()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var playlistId = Guid.NewGuid();
        foreach (var world in new[] { source, target })
        {
            world.Db.IptvPlaylists.Add(new IptvPlaylist { Id = playlistId, Name = "Freeview" });
            world.Db.IptvChannels.Add(new IptvChannel { PlaylistId = playlistId, ExternalChannelId = "bbc1.uk", Name = "BBC One", StreamUrl = "http://tv/bbc1" });
            await world.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
            world.Db.ChangeTracker.Clear();
        }
        var sourceChannel = await source.Db.IptvChannels.SingleAsync(TestContext.Current.CancellationToken);
        source.Db.IptvRecordingSchedules.AddRange(
            new IptvRecordingSchedule { Title = "News", UserId = BackupTestWorld.UserId, ProfileId = Profile, ChannelId = sourceChannel.Id },
            new IptvRecordingSchedule { Title = "Gone", UserId = BackupTestWorld.UserId, ProfileId = Profile, ChannelId = Guid.NewGuid() });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new IptvRecordingSchedulesBackupSection(source.Db, source.References),
            new IptvRecordingSchedulesBackupSection(target.Db, target.References));

        var targetChannel = await target.Db.IptvChannels.SingleAsync(TestContext.Current.CancellationToken);
        (await target.Db.IptvRecordingSchedules.SingleAsync(TestContext.Current.CancellationToken)).ChannelId.Should().Be(targetChannel.Id);
        result.Warnings.Should().ContainSingle(w => w.Contains("channel isn't on this server yet"));
    }

    [Fact]
    public async Task Removing_a_request_server_releases_the_requests_assigned_to_it()
    {
        using var world = BackupTestWorld.Source();
        var kept = new RequestServer { Name = "Radarr" };
        world.Db.RequestServers.Add(kept);
        await world.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var section = new RequestServersBackupSection(world.Db);
        var archive = new MemoryBackupArchive();
        await section.WriteAsync(archive, TestContext.Current.CancellationToken);

        var removed = new RequestServer { Name = "Sonarr" };
        world.Db.RequestServers.Add(removed);
        world.Db.MediaRequests.Add(new MediaRequest { Title = "Severance", AssignedServerId = removed.Id });
        await world.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        world.Db.ChangeTracker.Clear();

        await section.ReadAsync(archive, TestContext.Current.CancellationToken);

        (await world.Db.RequestServers.SingleAsync(TestContext.Current.CancellationToken)).Id.Should().Be(kept.Id);
        (await world.Db.MediaRequests.SingleAsync(TestContext.Current.CancellationToken)).AssignedServerId.Should().BeNull();
    }

    [Fact]
    public async Task Requests_keep_their_requesters_and_drop_a_server_this_install_does_not_have()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var server = new RequestServer { Name = "Radarr" };
        var request = new MediaRequest { Title = "Dune", AssignedServerId = server.Id };
        source.Db.RequestServers.Add(server);
        source.Db.MediaRequests.Add(request);
        source.Db.MediaRequestUsers.AddRange(
            new MediaRequestUser { RequestId = request.Id, ProfileId = Profile },
            new MediaRequestUser { RequestId = request.Id, ProfileId = Guid.NewGuid() });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(new MediaRequestsBackupSection(source.Db), new MediaRequestsBackupSection(target.Db));

        (await target.Db.MediaRequests.SingleAsync(TestContext.Current.CancellationToken)).AssignedServerId.Should().BeNull();
        (await target.Db.MediaRequestUsers.SingleAsync(TestContext.Current.CancellationToken)).ProfileId.Should().Be(Profile);
        result.RowsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task The_podcast_catalog_is_matched_by_feed_url_and_keeps_existing_episodes()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        source.Db.PodcastShows.AddRange(
            new PodcastShow { FeedUrl = "https://feeds.example/hardfork", Title = "Hard Fork", IsInCatalog = true },
            new PodcastShow { FeedUrl = "https://feeds.example/new", Title = "New Show" });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var existing = new PodcastShow { FeedUrl = "https://feeds.example/hardfork", Title = "Old title" };
        var dropped = new PodcastShow { FeedUrl = "https://feeds.example/dropped", Title = "Dropped" };
        target.Db.PodcastShows.AddRange(existing, dropped);
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        await BackupTestWorld.CopyAsync(new PodcastShowsBackupSection(source.Db), new PodcastShowsBackupSection(target.Db));

        var shows = await target.Db.PodcastShows.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        shows.Select(s => s.FeedUrl).Should().BeEquivalentTo("https://feeds.example/hardfork", "https://feeds.example/new");
        var hardFork = shows.Single(s => s.FeedUrl.EndsWith("hardfork"));
        hardFork.Id.Should().Be(existing.Id);
        hardFork.Title.Should().Be("Hard Fork");
        hardFork.IsInCatalog.Should().BeTrue();
    }

    [Fact]
    public async Task Podcast_progress_follows_the_feed_and_waits_on_a_placeholder_episode_until_the_feed_is_fetched()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var show = new PodcastShow { FeedUrl = "https://feeds.example/hardfork", Title = "Hard Fork" };
        var fetched = new PodcastEpisode { PodcastShowId = show.Id, ExternalGuid = "ep-1", Title = "Episode 1", AudioUrl = "https://cdn/1.mp3" };
        var notYetFetched = new PodcastEpisode { PodcastShowId = show.Id, ExternalGuid = "ep-2", Title = "Episode 2", AudioUrl = "https://cdn/2.mp3", DurationSeconds = 3600 };
        source.Db.PodcastShows.Add(show);
        source.Db.PodcastEpisodes.AddRange(fetched, notYetFetched);
        source.Db.PodcastSubscriptions.Add(new PodcastSubscription { ProfileId = Profile, PodcastShowId = show.Id });
        source.Db.PodcastEpisodeProfileStates.AddRange(
            new PodcastEpisodeProfileState { ProfileId = Profile, PodcastEpisodeId = fetched.Id, PositionSeconds = 120 },
            new PodcastEpisodeProfileState { ProfileId = Profile, PodcastEpisodeId = notYetFetched.Id, IsPlayed = true });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var targetShow = new PodcastShow { FeedUrl = "https://feeds.example/hardfork", Title = "Hard Fork" };
        var targetEpisode = new PodcastEpisode { PodcastShowId = targetShow.Id, ExternalGuid = "ep-1", Title = "Episode 1", AudioUrl = "https://cdn/1.mp3" };
        target.Db.PodcastShows.Add(targetShow);
        target.Db.PodcastEpisodes.Add(targetEpisode);
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(new PodcastListeningBackupSection(source.Db), new PodcastListeningBackupSection(target.Db));

        (await target.Db.PodcastSubscriptions.SingleAsync(TestContext.Current.CancellationToken)).PodcastShowId.Should().Be(targetShow.Id);
        var progress = await target.Db.PodcastEpisodeProfileStates.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        progress.Single(p => p.PositionSeconds == 120).PodcastEpisodeId.Should().Be(targetEpisode.Id);
        var placeholder = await target.Db.PodcastEpisodes.SingleAsync(e => e.ExternalGuid == "ep-2", TestContext.Current.CancellationToken);
        placeholder.PodcastShowId.Should().Be(targetShow.Id);
        placeholder.DurationSeconds.Should().Be(3600);
        progress.Single(p => p.IsPlayed).PodcastEpisodeId.Should().Be(placeholder.Id);
        result.RowsSkipped.Should().Be(0);
    }

    [Fact]
    public async Task Podcast_subscriptions_to_a_show_this_server_does_not_have_are_skipped()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var show = new PodcastShow { FeedUrl = "https://feeds.example/hardfork", Title = "Hard Fork" };
        source.Db.PodcastShows.Add(show);
        source.Db.PodcastSubscriptions.Add(new PodcastSubscription { ProfileId = Profile, PodcastShowId = show.Id });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(new PodcastListeningBackupSection(source.Db), new PodcastListeningBackupSection(target.Db));

        (await target.Db.PodcastSubscriptions.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        result.Warnings.Should().ContainSingle(w => w.StartsWith("1 podcast subscription was skipped because its podcast isn't on this server."));
    }
}
