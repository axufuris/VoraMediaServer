using Microsoft.EntityFrameworkCore;
using Vora.Application.SmartLists;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Tasks;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresMigrationHistoryTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task A_database_in_qa_state_takes_only_the_collapsed_migration_and_keeps_its_settings()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildQaStateAsync(withSetupGuide: false, cancellationToken);
        await using (var before = legacy.NewContext())
        {
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"AiPlaylistMatchWindow\" = 0.07", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.AiPlaylistMatchWindow.Should().Be(0.07);
        (await after.ProfileChannelFavorites.CountAsync(cancellationToken)).Should().Be(0);
    }

    private static async Task<List<(Guid Id, int Order, string? Key)>> SmartListOrderAsync(VoraDbContext db, CancellationToken cancellationToken) =>
        (await db.Set<SmartList>().AsNoTracking().ToListAsync(cancellationToken))
            .Select(l => (l.Id, l.DisplayOrder, l.DefaultKey)).OrderBy(l => l.Id).ToList();

    [Fact]
    public async Task A_database_on_the_first_combined_migration_only_gains_the_task_table()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildEarlierCombinedStateAsync(LegacyDatabase.FirstCombinedMigration, withTaskTable: false, cancellationToken);

        var user = new User { Email = "parent@example.com", DisplayName = "Parent" };
        var profile = new UserProfile { Id = Guid.NewGuid(), Name = "Kid", UserId = user.Id };
        var playlist = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Cable" };
        List<(Guid Id, int Order, string? Key)> listsBefore;
        await legacy.SeedAsync(db =>
        {
            db.AddRange(user, profile, playlist, new ProfileChannelFavorite { ProfileId = profile.Id, PlaylistId = playlist.Id, ExternalChannelId = "cnn.us" });
            return db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        await using (var before = legacy.NewContext())
        {
            await before.Database.ExecuteSqlRawAsync("UPDATE \"SmartLists\" SET \"DisplayOrder\" = 0 WHERE \"DefaultKey\" = 'favorite-channels'", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            listsBefore = await SmartListOrderAsync(before, cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await SmartListOrderAsync(after, cancellationToken)).Should().Equal(listsBefore);
        (await after.ProfileChannelFavorites.CountAsync(cancellationToken)).Should().Be(1);
        (await after.PendingTasks.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task A_database_on_the_second_combined_migration_keeps_its_row_order_and_saved_tasks()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildEarlierCombinedStateAsync(LegacyDatabase.SecondCombinedMigration, withTaskTable: true, cancellationToken);

        var saved = new PendingTask { Id = Guid.NewGuid(), Sequence = 1, Kind = "QueueScanLibrary", Name = "Scan Library: Movies", QueuedAt = DateTime.UtcNow };
        List<(Guid Id, int Order, string? Key)> listsBefore;
        await using (var before = legacy.NewContext())
        {
            before.PendingTasks.Add(saved);
            await before.SaveChangesAsync(cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            listsBefore = await SmartListOrderAsync(before, cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var listsAfter = await SmartListOrderAsync(after, cancellationToken);
        listsAfter.Should().Equal(listsBefore);
        listsAfter.Single(l => l.Key == "recent-recordings").Order.Should().Be(10);
        (await after.PendingTasks.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken)).Should().Equal(saved.Id);
    }

    [Fact]
    public async Task A_database_on_the_1_0_migration_only_gains_the_columns_added_since()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildReleasedStateAsync(cancellationToken);

        var saved = new PendingTask { Id = Guid.NewGuid(), Sequence = 1, Kind = "QueueLibraryAnalysis", Name = "Analyze Library: Movies", QueuedAt = DateTime.UtcNow };
        List<(Guid Id, int Order, string? Key)> listsBefore;
        await using (var before = legacy.NewContext())
        {
            before.PendingTasks.Add(saved);
            await before.SaveChangesAsync(cancellationToken);
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"AiPlaylistMatchWindow\" = 0.07", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            listsBefore = await SmartListOrderAsync(before, cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await after.Set<MediaItem>().AsNoTracking().Select(m => m.FullyRefreshedAt).ToListAsync(cancellationToken)).Should().BeEmpty();
        (await after.PendingTasks.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken)).Should().Equal(saved.Id);
        (await SmartListOrderAsync(after, cancellationToken)).Should().Equal(listsBefore);
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.AiPlaylistMatchWindow.Should().Be(0.07);
        settings.HardwareAccelerationType.Should().Be(HardwareAccelerationType.Auto);
    }

    [Fact]
    public async Task A_database_from_before_stream_origins_gains_where_each_stream_came_from()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var sessionId = Guid.NewGuid();
        await legacy.BuildHardwareAccelerationStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] };
            var movie = new Movie { Title = "Arrival", LibraryId = library.Id };
            var part = new MediaPart { FilePath = "/movies/arrival.mkv", MediaItemId = movie.Id };
            var account = new User { Email = "andy@example.com", DisplayName = "Andy" };
            var profile = new UserProfile { Name = "Andy", UserId = account.Id };
            var device = new ClientDevice { DeviceId = "web-1", ClientName = "Vora Web", DeviceName = "Web Browser", LastIpAddress = "67.199.253.6", Location = "Salt Lake City, UT, US" };
            var session = new StreamSession
            {
                Id = sessionId,
                MediaItemId = movie.Id,
                MediaPartId = part.Id,
                UserId = account.Id,
                UserProfileId = profile.Id,
                ClientDeviceId = device.Id,
                Strategy = "DirectPlay",
                VideoStrategy = "DirectPlay",
                AudioStrategy = "DirectPlay",
                SubtitleStrategy = "None",
            };
            db.AddRange(library, movie, part, account, profile, device, session);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"HardwareAccelerationType\" = 2", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await StreamSessionColumnsAsync(before, cancellationToken)).Should().NotContain("IpAddress").And.NotContain("Location");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await StreamSessionColumnsAsync(after, cancellationToken)).Should().Contain("IpAddress").And.Contain("Location");
        var kept = await after.StreamSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, cancellationToken);
        kept.Strategy.Should().Be("DirectPlay");
        kept.IpAddress.Should().BeNull();
        kept.Location.Should().BeNull();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.HardwareAccelerationType.Should().Be(HardwareAccelerationType.QuickSync);
    }

    [Fact]
    public async Task A_database_from_before_local_metadata_gains_the_local_metadata_choices_switched_off()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var libraryId = Guid.NewGuid();
        await legacy.BuildStreamOriginStateAsync(async db =>
        {
            db.Add(new MediaLibrary { Id = libraryId, Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await LibraryColumnsAsync(before, cancellationToken)).Should().NotContain("LocalMetadataMode").And.NotContain("PreferLocalArtwork");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var kept = await after.Set<MediaLibrary>().AsNoTracking().SingleAsync(l => l.Id == libraryId, cancellationToken);
        kept.Name.Should().Be("Movies");
        kept.FolderPaths.Should().Equal("/movies");
        kept.LocalMetadataMode.Should().Be(LocalMetadataMode.Off);
        kept.PreferLocalArtwork.Should().BeFalse();
    }

    [Fact]
    public async Task A_database_on_the_migration_before_this_one_only_gains_the_table_of_remembered_playback_choices()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var libraryId = Guid.NewGuid();
        await legacy.BuildLocalMetadataStateAsync(async db =>
        {
            db.Add(new MediaLibrary { Id = libraryId, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = ["/shows"], LocalMetadataMode = LocalMetadataMode.Prefer });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await TablesAsync(before, cancellationToken)).Should().NotContain("ProfileTitlePlaybackChoices");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await TablesAsync(after, cancellationToken)).Should().Contain("ProfileTitlePlaybackChoices");
        (await after.ProfileTitlePlaybackChoices.CountAsync(cancellationToken)).Should().Be(0);
        var kept = await after.Set<MediaLibrary>().AsNoTracking().SingleAsync(l => l.Id == libraryId, cancellationToken);
        kept.LocalMetadataMode.Should().Be(LocalMetadataMode.Prefer);
    }

    [Fact]
    public async Task A_database_on_the_remembered_choices_migration_only_gains_the_profile_track_preferences_set_to_automatic()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        await legacy.BuildRememberedChoicesStateAsync(async db =>
        {
            db.Add(new User { Id = userId, Email = "viewer@example.com", DisplayName = "Viewer" });
            db.Add(new UserProfile { Id = profileId, Name = "Viewer", UserId = userId, AutoSkipIntro = true });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ProfileColumnsAsync(before, cancellationToken)).Should().NotContain("PreferredAudioLanguage").And.NotContain("SubtitleMode");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var kept = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken);
        kept.AutoSkipIntro.Should().BeTrue();
        kept.PreferredAudioLanguage.Should().BeNull();
        kept.PreferredSubtitleLanguage.Should().BeNull();
        kept.SubtitleMode.Should().BeNull();
        kept.PreferHearingImpairedSubtitles.Should().BeFalse();
    }

    [Fact]
    public async Task A_database_on_the_track_preferences_migration_only_lets_an_artist_have_two_albums_of_one_title()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var libraryId = Guid.NewGuid();
        var artistId = Guid.NewGuid();
        var blueId = Guid.NewGuid();
        await legacy.BuildTrackPreferencesStateAsync(async db =>
        {
            db.Add(new MediaLibrary { Id = libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = ["/music"] });
            db.Add(new Artist { Id = artistId, Name = "Weezer", LibraryId = libraryId });
            db.Add(new Album { Id = blueId, Title = "Weezer", ArtistId = artistId, LibraryId = libraryId, Year = 1994 });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await AlbumTitleIndexAsync(before, cancellationToken)).Should().Contain("UNIQUE");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await AlbumTitleIndexAsync(after, cancellationToken)).Should().NotContain("UNIQUE");
        after.Add(new Album { Title = "Weezer", ArtistId = artistId, LibraryId = libraryId, Year = 2001 });
        await after.SaveChangesAsync(cancellationToken);
        (await after.Albums.CountAsync(a => a.ArtistId == artistId && a.Title == "Weezer", cancellationToken)).Should().Be(2);
        (await after.Albums.AsNoTracking().SingleAsync(a => a.Id == blueId, cancellationToken)).Year.Should().Be(1994);
    }

    private static Task<string> AlbumTitleIndexAsync(VoraDbContext db, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'Albums' AND indexname = 'IX_Albums_ArtistId_Title'")
            .SingleAsync(cancellationToken);

    private static Task<List<string>> TablesAsync(VoraDbContext db, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema()")
            .ToListAsync(cancellationToken);

    private static Task<List<string>> ProfileColumnsAsync(VoraDbContext db, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'UserProfiles'")
            .ToListAsync(cancellationToken);

    private static Task<List<string>> LibraryColumnsAsync(VoraDbContext db, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'MediaLibraries'")
            .ToListAsync(cancellationToken);

    private static Task<List<string>> StreamSessionColumnsAsync(VoraDbContext db, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'StreamSessions'")
            .ToListAsync(cancellationToken);

    [Fact]
    public async Task A_database_on_the_1_1_migration_only_gains_the_hardware_acceleration_choice()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildSecondReleaseStateAsync(cancellationToken);

        var saved = new PendingTask { Id = Guid.NewGuid(), Sequence = 1, Kind = "QueueLibraryAnalysis", Name = "Analyze Library: Movies", QueuedAt = DateTime.UtcNow };
        List<(Guid Id, int Order, string? Key)> listsBefore;
        await using (var before = legacy.NewContext())
        {
            before.PendingTasks.Add(saved);
            await before.SaveChangesAsync(cancellationToken);
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"HardwareTranscodingDevice\" = '/dev/dri/renderD128', \"AiPlaylistMatchWindow\" = 0.07", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            listsBefore = await SmartListOrderAsync(before, cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.HardwareAccelerationType.Should().Be(HardwareAccelerationType.Auto);
        settings.HardwareTranscodingDevice.Should().Be("/dev/dri/renderD128");
        settings.AiPlaylistMatchWindow.Should().Be(0.07);
        (await after.PendingTasks.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken)).Should().Equal(saved.Id);
        (await SmartListOrderAsync(after, cancellationToken)).Should().Equal(listsBefore);
    }

    [Fact]
    public async Task A_database_that_already_ran_the_setup_guide_migration_keeps_its_guide_progress()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildQaStateAsync(withSetupGuide: true, cancellationToken);
        await using (var before = legacy.NewContext())
        {
            before.Set<User>().Add(new User { Email = "admin@example.com", DisplayName = "Admin", IsAdmin = true });
            await before.SaveChangesAsync(cancellationToken);
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"SetupGuideStatus\" = 3, \"SetupGuideContent\" = 5", cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.SetupGuideStatus.Should().Be(SetupGuideStatus.Completed);
        settings.SetupGuideContent.Should().Be(SetupGuideContent.MoviesAndShows | SetupGuideContent.LiveTv);
    }

    [Fact]
    public async Task A_new_server_keeps_its_seeded_settings_with_the_ai_match_window_at_its_default()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();

        var settings = await db.Set<ServerSetting>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

        settings.Id.Should().Be("GLOBAL_SETTINGS");
        settings.AiPlaylistMatchWindow.Should().Be(0.04);
    }

    [Fact]
    public async Task A_new_server_lists_the_default_rows_in_the_default_order()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();

        var lists = (await db.Set<SmartList>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
            .OrderBy(l => l.DisplayOrder)
            .Select(l => (l.DefaultKey, l.DisplayOrder));

        lists.Should().Equal(SmartListDefaults.All.Select(d => ((string?)d.Key, d.DisplayOrder)));
    }
}
