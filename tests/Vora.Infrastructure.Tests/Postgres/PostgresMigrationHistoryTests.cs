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
using Vora.Infrastructure.Persistence.Repositories;

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

    [Fact]
    public async Task A_database_on_the_album_index_migration_only_gains_video_dimensions_and_the_output_size_of_a_stream()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var trackId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await legacy.BuildAlbumIndexStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] };
            var movie = new Movie { Title = "Minions", LibraryId = library.Id };
            var part = new MediaPart { FilePath = "/movies/minions.mkv", MediaItemId = movie.Id, Resolution = "2160p" };
            var track = new MediaVideoTrack { Id = trackId, MediaPartId = part.Id, Codec = "hevc", HdrType = "DoVi/HDR10", BitDepth = 10 };
            var account = new User { Email = "viewer@example.com", DisplayName = "Viewer" };
            var device = new ClientDevice { DeviceId = "web-1", ClientName = "Vora Web", DeviceName = "Web Browser" };
            var session = new StreamSession
            {
                Id = sessionId,
                MediaItemId = movie.Id,
                MediaPartId = part.Id,
                UserId = account.Id,
                ClientDeviceId = device.Id,
                Strategy = "Transcode",
                VideoStrategy = "Transcode",
                AudioStrategy = "DirectStream",
                SubtitleStrategy = "None",
                OutputResolution = "1440p",
                BandwidthKbps = 8000,
            };
            db.AddRange(library, movie, part, track, account, device, session);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "MediaVideoTracks", cancellationToken)).Should().NotContain("Width").And.NotContain("Height").And.NotContain("FrameRate");
            (await StreamSessionColumnsAsync(before, cancellationToken)).Should().NotContain("OutputWidth").And.NotContain("OutputHeight");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var track = await after.MediaVideoTracks.AsNoTracking().SingleAsync(t => t.Id == trackId, cancellationToken);
        track.HdrType.Should().Be("DoVi/HDR10");
        track.BitDepth.Should().Be(10);
        track.Width.Should().BeNull();
        track.Height.Should().BeNull();
        track.FrameRate.Should().BeNull();
        var session = await after.StreamSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, cancellationToken);
        session.OutputResolution.Should().Be("1440p");
        session.BandwidthKbps.Should().Be(8000);
        session.OutputWidth.Should().BeNull();
        session.OutputHeight.Should().BeNull();
    }

    [Fact]
    public async Task A_database_on_the_video_dimensions_migration_only_gains_the_library_sort_of_a_profile()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var profileId = Guid.NewGuid();
        await legacy.BuildVideoDimensionsStateAsync(async db =>
        {
            var account = new User { Email = "viewer@example.com", DisplayName = "Viewer" };
            var profile = new UserProfile { Id = profileId, Name = "Viewer", UserId = account.Id, PreferredAudioLanguage = "en", SubtitleMode = SubtitleMode.Smart };
            db.AddRange(account, profile);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ProfileColumnsAsync(before, cancellationToken)).Should().NotContain("LibrarySort").And.NotContain("LibrarySortDescending");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var profile = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken);
        profile.PreferredAudioLanguage.Should().Be("en");
        profile.SubtitleMode.Should().Be(SubtitleMode.Smart);
        profile.LibrarySort.Should().BeNull();
        profile.LibrarySortDescending.Should().BeFalse();
    }

    [Fact]
    public async Task A_database_on_the_library_sort_migration_only_gains_keeping_hdr_in_transcodes_switched_on()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var profileId = Guid.NewGuid();
        await legacy.BuildLibrarySortStateAsync(async db =>
        {
            var account = new User { Email = "viewer@example.com", DisplayName = "Viewer" };
            db.AddRange(account, new UserProfile { Id = profileId, Name = "Viewer", UserId = account.Id, LibrarySort = LibrarySortField.ReleaseDate, LibrarySortDescending = true });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"EnableHdrToneMapping\" = FALSE", cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "ServerSettings", cancellationToken)).Should().NotContain("PreserveHdrInTranscodes");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.PreserveHdrInTranscodes.Should().BeTrue();
        settings.EnableHdrToneMapping.Should().BeFalse();
        var profile = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken);
        (profile.LibrarySort, profile.LibrarySortDescending).Should().Be((LibrarySortField.ReleaseDate, true));
    }

    [Fact]
    public async Task A_database_on_the_hdr_transcode_migration_only_gains_autoplay_switched_on_for_every_profile()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var profileId = Guid.NewGuid();
        await legacy.BuildHdrTranscodeStateAsync(async db =>
        {
            var account = new User { Email = "viewer@example.com", DisplayName = "Viewer" };
            db.AddRange(account, new UserProfile { Id = profileId, Name = "Viewer", UserId = account.Id, AutoSkipCredits = true, LibrarySort = LibrarySortField.ReleaseDate });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"PreserveHdrInTranscodes\" = FALSE", cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "UserProfiles", cancellationToken)).Should().NotContain("AutoPlayNext");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var profile = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken);
        profile.AutoPlayNext.Should().BeTrue();
        (profile.AutoSkipCredits, profile.LibrarySort).Should().Be((true, LibrarySortField.ReleaseDate));
        (await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken)).PreserveHdrInTranscodes.Should().BeFalse();
    }

    [Fact]
    public async Task A_database_on_the_autoplay_migration_gains_the_episode_settings_and_forgets_resume_points_under_half_a_minute()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var profileId = Guid.NewGuid();
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var glimpsed = new Movie { Title = "Heat", LibraryId = library.Id };
        var underWay = new Movie { Title = "Ronin", LibraryId = library.Id };
        await legacy.BuildAutoPlayStateAsync(async db =>
        {
            var account = new User { Email = "viewer@example.com", DisplayName = "Viewer" };
            db.AddRange(account, library, glimpsed, underWay, new UserProfile { Id = profileId, Name = "Viewer", UserId = account.Id, AutoPlayNext = false, AutoSkipCredits = true });
            db.UserMediaStates.AddRange(
                new UserMediaState { ProfileId = profileId, MediaItemId = glimpsed.Id, ResumePositionSeconds = 12, LastPlayedAt = DateTime.UtcNow },
                new UserMediaState { ProfileId = profileId, MediaItemId = underWay.Id, ResumePositionSeconds = 1250, LastPlayedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "UserProfiles", cancellationToken)).Should().NotContain(["UpNextOrder", "EpisodeLayout"]);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var profile = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken);
        (profile.UpNextOrder, profile.EpisodeLayout).Should().Be((UpNextOrder.InOrder, EpisodeLayout.Row));
        (profile.AutoPlayNext, profile.AutoSkipCredits).Should().Be((false, true));
        var resumePoints = await after.UserMediaStates.AsNoTracking().ToDictionaryAsync(s => s.MediaItemId, s => s.ResumePositionSeconds, cancellationToken);
        resumePoints.Should().Equal(new Dictionary<Guid, double> { [glimpsed.Id] = 0, [underWay.Id] = 1250 });
    }

    [Fact]
    public async Task A_database_on_the_up_next_order_migration_only_gains_whether_a_device_draws_image_subtitles()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var device = new ClientDevice
        {
            DeviceId = "living-room", ClientName = "Vora", DeviceName = "Shield", DeviceType = "AndroidTV",
            SupportedVideoCodecs = ["h264", "hevc"], SupportedHdrFormats = ["HDR10"], MaxVideoBitDepth = 10,
        };
        await legacy.BuildUpNextOrderStateAsync(async db =>
        {
            db.ClientDevices.Add(device);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "ClientDevices", cancellationToken)).Should().NotContain("RendersImageSubtitles");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var kept = await after.ClientDevices.AsNoTracking().SingleAsync(d => d.Id == device.Id, cancellationToken);
        kept.RendersImageSubtitles.Should().BeNull();
        kept.SupportedHdrFormats.Should().Equal("HDR10");
        kept.MaxVideoBitDepth.Should().Be(10);
    }

    [Fact]
    public async Task A_database_on_the_image_subtitles_migration_forgets_video_bitrates_above_their_whole_file()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var reencoded = Guid.NewGuid();
        var remux = Guid.NewGuid();
        var unmeasured = Guid.NewGuid();
        await legacy.BuildImageSubtitlesStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Shows", Type = LibraryType.TvShow, FolderPaths = ["/tv"] };
            db.Add(library);
            db.Add(Film(library.Id, "Shatter the Moon", 3_706_601, new MediaVideoTrack { Id = reencoded, Codec = "hevc", Bitrate = 23_986_068 }));
            db.Add(Film(library.Id, "Heat", 35_000_000, new MediaVideoTrack { Id = remux, Codec = "h264", Bitrate = 29_800_000 }));
            db.Add(Film(library.Id, "Unprobed", null, new MediaVideoTrack { Id = unmeasured, Codec = "h264", Bitrate = 8_000_000 }));
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var bitrates = await after.MediaVideoTracks.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Bitrate, cancellationToken);
        bitrates[reencoded].Should().BeNull();
        bitrates[remux].Should().Be(29_800_000);
        bitrates[unmeasured].Should().Be(8_000_000);
    }

    private static Movie Film(Guid libraryId, string title, long? overallBitrate, MediaVideoTrack video)
    {
        var film = new Movie { Title = title, LibraryId = libraryId };
        var part = new MediaPart { FilePath = $"/tv/{title}.mkv", Container = "mkv", OverallBitrate = overallBitrate, MediaItemId = film.Id };
        part.VideoTracks.Add(video);
        film.MediaParts.Add(part);
        return film;
    }

    [Fact]
    public async Task A_database_on_the_migration_1_2_shipped_with_keeps_its_channels_and_gains_their_video_format_empty()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var channelId = Guid.NewGuid();
        await legacy.BuildThirdReleaseStateAsync(async db =>
        {
            var playlist = new IptvPlaylist { Name = "Provider", M3uUrl = "https://iptv.example.com/list.m3u" };
            playlist.Channels.Add(new IptvChannel { Id = channelId, ExternalChannelId = "action24", Name = "Action 24 Not 24/7", StreamUrl = "https://iptv.example.com/action24.ts", PlaylistId = playlist.Id });
            db.Add(playlist);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "IptvChannels", cancellationToken)).Should().NotContain("VideoCodec");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var channel = await after.IptvChannels.AsNoTracking().SingleAsync(c => c.Id == channelId, cancellationToken);
        channel.Name.Should().Be("Action 24 Not 24/7");
        channel.VideoCodec.Should().BeNull();
        channel.VideoLevel.Should().BeNull();
        channel.VideoFormatSeenAt.Should().BeNull();
    }

    [Fact]
    public async Task A_database_on_the_channel_video_format_migration_keeps_its_collections_and_titles_and_gains_theme_collections()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var collectionId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        await legacy.BuildChannelVideoFormatStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
            db.Add(library);
            db.Add(new Movie { Id = movieId, Title = "Hocus Pocus", LibraryId = library.Id });
            db.Add(new Collection
            {
                Id = collectionId,
                Title = "Halloween",
                VisibleStartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                VisibleEndDate = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "Collections", cancellationToken)).Should().NotContain("VisibleEveryYear");
            (await ColumnsAsync(before, "MediaItems", cancellationToken)).Should().NotContain("Keywords");
            (await ColumnsAsync(before, "CollectionThemes", cancellationToken)).Should().BeEmpty();
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var collection = await after.Collections.AsNoTracking().SingleAsync(c => c.Id == collectionId, cancellationToken);
        collection.Title.Should().Be("Halloween");
        collection.VisibleStartDate.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        collection.VisibleEveryYear.Should().BeFalse();
        (await after.MediaItems.AsNoTracking().SingleAsync(m => m.Id == movieId, cancellationToken)).Keywords.Should().BeNull();
        (await after.CollectionThemes.CountAsync(cancellationToken)).Should().Be(0);
        (await after.CollectionThemeVerdicts.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task A_database_on_the_theme_collections_migration_keeps_its_listening_history_with_no_source()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var profileId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var playId = Guid.NewGuid();
        var playedAt = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        await legacy.BuildThemeCollectionsStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
            db.Add(library);
            db.Add(new Track { Id = trackId, Title = "Song", LibraryId = library.Id });
            db.Add(new TrackPlayHistory { Id = playId, ProfileId = profileId, TrackId = trackId, PlayedAt = playedAt, DurationListenedSeconds = 200, Completed = true });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "TrackPlayHistory", cancellationToken)).Should().NotContain(new[] { "Source", "SourceId" });
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var play = await after.TrackPlayHistory.AsNoTracking().SingleAsync(p => p.Id == playId, cancellationToken);
        play.TrackId.Should().Be(trackId);
        play.PlayedAt.Should().Be(playedAt);
        play.Completed.Should().BeTrue();
        play.Source.Should().Be(MusicPlaySource.None);
        play.SourceId.Should().BeNull();
    }

    private static RecommendationSnapshot StoredRows(Guid profileId, Guid scope, string providerId, string rows) =>
        new() { ProfileId = profileId, LibraryScope = scope, ProviderId = providerId, Rows = rows, GeneratedAt = new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc) };

    private static async Task<string> StoredRowsJsonAsync(VoraDbContext db, Guid scope, string providerId, CancellationToken cancellationToken) =>
        (await db.RecommendationSnapshots.AsNoTracking().SingleAsync(s => s.LibraryScope == scope && s.ProviderId == providerId, cancellationToken)).Rows;

    [Fact]
    public async Task A_database_on_the_iptv_providers_migration_has_its_stored_ai_rows_lose_the_ai_prefix_and_gain_the_flag()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var library = Guid.NewGuid();
        var heist = Guid.NewGuid();
        var cozy = Guid.NewGuid();
        await legacy.BuildIptvProvidersStateAsync(async db =>
        {
            db.Add(new User { Id = userId, Email = "viewer@example.com", DisplayName = "Viewer" });
            db.Add(new UserProfile { Id = profileId, Name = "Viewer", UserId = userId });
            db.AddRange(
                StoredRows(profileId, RecommendationSnapshot.AllLibraries, "openai_recommendations",
                    $$"""[{"title":"AI: Gritty Heists","description":"Crews and scores.","weight":1,"localItemIds":["{{heist}}"],"externalTmdbIds":[]},{"title":"Cozy Sci-Fi","description":null,"weight":2,"localItemIds":["{{cozy}}"],"externalTmdbIds":[]}]"""),
                StoredRows(profileId, library, "openai_recommendations",
                    """[{"title":"AI: Rogue Machines","description":null,"weight":1,"isAi":true,"localItemIds":[],"externalTmdbIds":[]}]"""),
                StoredRows(profileId, RecommendationSnapshot.AllLibraries, "local_recommendations",
                    $$"""[{"title":"Because you watched Heat","description":"Picked from Heat.","weight":1,"localItemIds":["{{heist}}"],"externalTmdbIds":[]}]"""));
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        string localBefore, flaggedBefore;
        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            localBefore = await StoredRowsJsonAsync(before, RecommendationSnapshot.AllLibraries, "local_recommendations", cancellationToken);
            flaggedBefore = await StoredRowsJsonAsync(before, library, "openai_recommendations", cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        string homeAfter;
        await using (var after = legacy.NewContext())
        {
            (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
            var stored = await new RecommendationSnapshotRepository(after).GetAsync(profileId, null, new[] { "openai_recommendations", "local_recommendations" }, withLastActivity: false);

            var ai = stored.Providers.Single(p => p.ProviderId == "openai_recommendations").Rows;
            ai.Select(r => r.Title).Should().Equal("Gritty Heists", "Cozy Sci-Fi");
            ai.Should().OnlyContain(r => r.IsAi);
            ai.Select(r => r.Weight).Should().Equal(1, 2);
            ai[0].Description.Should().Be("Crews and scores.");
            ai[0].LocalItemIds.Should().Equal(heist);
            ai[1].LocalItemIds.Should().Equal(cozy);

            var local = stored.Providers.Single(p => p.ProviderId == "local_recommendations").Rows.Should().ContainSingle().Subject;
            local.Title.Should().Be("Because you watched Heat");
            local.IsAi.Should().BeFalse();

            (await StoredRowsJsonAsync(after, RecommendationSnapshot.AllLibraries, "local_recommendations", cancellationToken)).Should().Be(localBefore);
            (await StoredRowsJsonAsync(after, library, "openai_recommendations", cancellationToken)).Should().Be(flaggedBefore);
            homeAfter = await StoredRowsJsonAsync(after, RecommendationSnapshot.AllLibraries, "openai_recommendations", cancellationToken);

            // The next fold-in re-issues the migration, so every database runs it again.
            await after.Database.ExecuteSqlAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {MigrationHistoryTests.ChangesSinceInitial}", cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var again = legacy.NewContext();
        (await StoredRowsJsonAsync(again, RecommendationSnapshot.AllLibraries, "openai_recommendations", cancellationToken)).Should().Be(homeAfter);
        (await StoredRowsJsonAsync(again, library, "openai_recommendations", cancellationToken)).Should().Be(flaggedBefore);
        (await StoredRowsJsonAsync(again, RecommendationSnapshot.AllLibraries, "local_recommendations", cancellationToken)).Should().Be(localBefore);
    }

    [Fact]
    public async Task A_database_on_the_for_you_rows_migration_keeps_its_playlists_as_m3u_without_credentials_and_gains_the_provider_columns()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var playlistId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        await legacy.BuildForYouRowsStateAsync(async db =>
        {
            var playlist = new IptvPlaylist
            {
                Id = playlistId,
                Name = "Provider",
                M3uUrl = "https://iptv.example.com/list.m3u",
                TunerProfile = new IptvTunerProfile { PlaylistId = playlistId, MaxConcurrentStreams = 2 }
            };
            playlist.Channels.Add(new IptvChannel { Id = channelId, ExternalChannelId = "action24", Name = "Action 24", StreamUrl = "https://iptv.example.com/action24.ts", PlaylistId = playlistId });
            db.Add(playlist);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await ColumnsAsync(before, "IptvPlaylists", cancellationToken)).Should().NotContain(["SourceType", "ServerUrl", "Username", "PasswordCiphertext", "UserAgent", "Referer", "UseProviderGuide", "AccountMaxConnections"]);
            (await ColumnsAsync(before, "IptvChannels", cancellationToken)).Should().NotContain(["UserAgent", "Referer"]);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using (var after = legacy.NewContext())
        {
            (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
            var playlist = await after.IptvPlaylists.AsNoTracking().Include(p => p.TunerProfile).SingleAsync(p => p.Id == playlistId, cancellationToken);
            playlist.SourceType.Should().Be(IptvSourceType.M3u);
            playlist.M3uUrl.Should().Be("https://iptv.example.com/list.m3u");
            playlist.ServerUrl.Should().BeNull();
            playlist.Username.Should().BeNull();
            playlist.PasswordCiphertext.Should().BeNull();
            playlist.UserAgent.Should().BeNull();
            playlist.Referer.Should().BeNull();
            playlist.UseProviderGuide.Should().BeTrue();
            playlist.AccountMaxConnections.Should().BeNull();
            playlist.TunerProfile!.MaxConcurrentStreams.Should().Be(2);

            var channel = await after.IptvChannels.AsNoTracking().SingleAsync(c => c.Id == channelId, cancellationToken);
            channel.StreamUrl.Should().Be("https://iptv.example.com/action24.ts");
            channel.UserAgent.Should().BeNull();
            channel.Referer.Should().BeNull();

            after.IptvPlaylists.Add(new IptvPlaylist
            {
                Name = "Paid provider",
                SourceType = IptvSourceType.Xtream,
                ServerUrl = "http://provider.example.com:8080",
                Username = "viewer",
                PasswordCiphertext = "ciphertext",
                UserAgent = "VLC/3.0",
                AccountMaxConnections = 1
            });
            await after.SaveChangesAsync(cancellationToken);
        }

        await using var reread = legacy.NewContext();
        var xtream = await reread.IptvPlaylists.AsNoTracking().SingleAsync(p => p.SourceType == IptvSourceType.Xtream, cancellationToken);
        xtream.Username.Should().Be("viewer");
        xtream.AccountMaxConnections.Should().Be(1);
    }

    [Fact]
    public async Task A_database_on_the_search_index_migration_gains_the_for_you_rows_table_empty_and_keeps_its_profiles()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var playedAt = new DateTime(2026, 11, 10, 21, 0, 0, DateTimeKind.Utc);
        await legacy.BuildSearchIndexStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
            db.Add(library);
            db.Add(new Movie { Id = movieId, Title = "Sicario", LibraryId = library.Id });
            db.Add(new User { Id = userId, Email = "viewer@example.com", DisplayName = "Viewer" });
            db.Add(new UserProfile { Id = profileId, Name = "Viewer", UserId = userId });
            db.Add(new UserMediaState { ProfileId = profileId, MediaItemId = movieId, IsPlayed = true, LastPlayedAt = playedAt });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await TablesAsync(before, cancellationToken)).Should().NotContain("RecommendationSnapshots");
        }

        await legacy.MigrateAsync(cancellationToken);

        await using (var after = legacy.NewContext())
        {
            (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
            (await after.RecommendationSnapshots.CountAsync(cancellationToken)).Should().Be(0);
            (await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken)).Name.Should().Be("Viewer");
            (await after.UserMediaStates.AsNoTracking().SingleAsync(s => s.ProfileId == profileId, cancellationToken)).LastPlayedAt.Should().Be(playedAt);

            after.RecommendationSnapshots.Add(new RecommendationSnapshot { ProfileId = profileId, LibraryScope = RecommendationSnapshot.AllLibraries, ProviderId = "local_recommendations", Rows = "[]", GeneratedAt = DateTime.UtcNow });
            await after.SaveChangesAsync(cancellationToken);
            await after.UserProfiles.Where(p => p.Id == profileId).ExecuteDeleteAsync(cancellationToken);
        }

        // The table's foreign key came with it: a profile's rows go with the profile.
        await using var deleted = legacy.NewContext();
        (await deleted.RecommendationSnapshots.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task A_database_on_the_home_row_index_migration_gains_the_search_indexes_and_keeps_its_titles()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var movieId = Guid.NewGuid();
        await legacy.BuildHomeRowIndexStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
            db.Add(library);
            db.Add(new Movie { Id = movieId, Title = "Sicario", LibraryId = library.Id });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await IndexesAsync(before, "MediaItems", cancellationToken)).Should().NotContain(LegacyDatabase.SearchIndexes[0]);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await IndexesAsync(after, "MediaItems", cancellationToken)).Should().Contain(LegacyDatabase.SearchIndexes[0]);
        (await IndexesAsync(after, "Actors", cancellationToken)).Should().Contain(LegacyDatabase.SearchIndexes[1]);
        (await IndexesAsync(after, "Artists", cancellationToken)).Should().Contain(LegacyDatabase.SearchIndexes[2]);
        (await IndexesAsync(after, "Albums", cancellationToken)).Should().Contain(LegacyDatabase.SearchIndexes[3]);
        (await after.MediaItems.AsNoTracking().SingleAsync(m => m.Id == movieId, cancellationToken)).Title.Should().Be("Sicario");
    }

    [Fact]
    public async Task A_database_on_the_play_source_migration_gains_the_home_row_sort_indexes_and_keeps_its_titles()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        var movieId = Guid.NewGuid();
        await legacy.BuildPlaySourceStateAsync(async db =>
        {
            var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
            db.Add(library);
            db.Add(new Movie { Id = movieId, Title = "Sicario", LibraryId = library.Id, ReleaseDate = new DateOnly(2015, 9, 18) });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await using (var before = legacy.NewContext())
        {
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            (await IndexesAsync(before, "MediaItems", cancellationToken)).Should().NotContain(LegacyDatabase.HomeRowIndexes);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await IndexesAsync(after, "MediaItems", cancellationToken)).Should().Contain(LegacyDatabase.HomeRowIndexes);
        (await after.MediaItems.AsNoTracking().SingleAsync(m => m.Id == movieId, cancellationToken)).ReleaseDate.Should().Be(new DateOnly(2015, 9, 18));
    }

    private static Task<List<string>> IndexesAsync(VoraDbContext db, string table, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = current_schema() AND tablename = {0}", table)
            .ToListAsync(cancellationToken);

    private static Task<List<string>> ColumnsAsync(VoraDbContext db, string table, CancellationToken cancellationToken) =>
        db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = {0}", table)
            .ToListAsync(cancellationToken);

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
