using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Backups.Sections;

namespace Vora.Infrastructure.Tests.Backups;

public class MediaReferenceRestoreTests
{
    private static readonly Guid Profile = BackupTestWorld.ProfileId;

    private static UserMediaState State(Guid mediaItemId, double position, DateTime lastPlayed) => new()
    {
        ProfileId = Profile,
        MediaItemId = mediaItemId,
        ResumePositionSeconds = position,
        LastPlayedAt = lastPlayed
    };

    [Fact]
    public async Task Watch_history_follows_its_items_to_a_rebuilt_server_and_skips_what_is_missing()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var played = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        source.Db.UserMediaStates.AddRange(
            State(source.Matrix, 1200, played),
            State(source.HomeVideo, 30, played),
            State(source.Pilot, 600, played),
            State(source.OnlyOnSource, 5, played));
        source.Db.TrackPlayHistory.Add(new TrackPlayHistory { ProfileId = Profile, TrackId = source.ParanoidAndroid, DurationListenedSeconds = 380, Completed = true });
        source.Db.StreamSessions.Add(new StreamSession { UserId = BackupTestWorld.UserId, UserProfileId = Profile, ClientDeviceId = BackupTestWorld.DeviceId, MediaItemId = source.Matrix });
        source.Db.PreservedUserMediaData.Add(new PreservedUserMediaData { ProfileId = Profile, ContentKey = "movie:tmdb:42", HasState = true, IsPlayed = true });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new WatchHistoryBackupSection(source.Db, source.References),
            new WatchHistoryBackupSection(target.Db, target.References));

        var states = await target.Db.UserMediaStates.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        states.Select(s => (s.MediaItemId, s.ResumePositionSeconds)).Should().BeEquivalentTo(new[]
        {
            (target.Matrix, 1200d),
            (target.HomeVideo, 30d),
            (target.Pilot, 600d)
        });
        (await target.Db.TrackPlayHistory.SingleAsync(TestContext.Current.CancellationToken)).TrackId.Should().Be(target.ParanoidAndroid);
        (await target.Db.PreservedUserMediaData.SingleAsync(TestContext.Current.CancellationToken)).ContentKey.Should().Be("movie:tmdb:42");
        (await target.Db.StreamSessions.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        result.RowsImported.Should().Be(5);
        result.RowsSkipped.Should().Be(2);
        result.Warnings.Should().Contain(w => w.StartsWith("1 watch-history row was skipped because its item isn't on this server."));
        result.Warnings.Should().Contain(w => w.StartsWith("1 playback session was skipped because its device isn't on this server."));
    }

    [Fact]
    public async Task Two_rows_that_land_on_the_same_item_keep_the_newest()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var duplicate = new Vora.Domain.Entities.Media.Movie { Title = "The Matrix", TmdbId = "603", LibraryId = source.MoviesLibrary };
        source.Db.MediaItems.Add(duplicate);
        source.Db.UserMediaStates.AddRange(
            State(source.Matrix, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            State(duplicate.Id, 900, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new WatchHistoryBackupSection(source.Db, source.References),
            new WatchHistoryBackupSection(target.Db, target.References));

        var state = await target.Db.UserMediaStates.SingleAsync(TestContext.Current.CancellationToken);
        state.MediaItemId.Should().Be(target.Matrix);
        state.ResumePositionSeconds.Should().Be(900);
        result.Warnings.Should().ContainSingle(w => w.StartsWith("1 watch-history row was dropped"));
    }

    [Fact]
    public async Task A_backup_made_before_identities_keeps_rows_whose_item_still_exists()
    {
        using var world = BackupTestWorld.Source();
        world.Db.UserMediaStates.AddRange(State(world.Matrix, 10, DateTime.UtcNow), State(Guid.NewGuid(), 20, DateTime.UtcNow));
        await world.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var section = new WatchHistoryBackupSection(world.Db, world.References);

        var (_, result) = await BackupTestWorld.CopyAsync(section, section, archive =>
        {
            archive.Remove(BackupIdentityFile.PathFor(section.Key));
            archive.Remove($"{section.Key}/preserved-user-data.json");
        });

        (await world.Db.UserMediaStates.SingleAsync(TestContext.Current.CancellationToken)).MediaItemId.Should().Be(world.Matrix);
        result.RowsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task Rows_for_a_profile_that_is_not_on_this_server_are_skipped()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget(withAccount: false);
        source.Db.UserMediaRatings.Add(new UserMediaRating { ProfileId = Profile, MediaItemId = source.Matrix, Rating = 9 });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new RatingsBackupSection(source.Db, source.References),
            new RatingsBackupSection(target.Db, target.References));

        (await target.Db.UserMediaRatings.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        result.Warnings.Should().ContainSingle(w => w.StartsWith("1 rating was skipped because its profile isn't on this server."));
    }

    [Fact]
    public async Task Music_ratings_and_likes_are_matched_by_musicbrainz_id_and_name()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        source.Db.UserAlbumRatings.Add(new UserAlbumRating { ProfileId = Profile, AlbumId = source.OkComputer, Rating = 10 });
        source.Db.UserArtistRatings.Add(new UserArtistRating { ProfileId = Profile, ArtistId = source.Radiohead, Rating = 9 });
        source.Db.TrackLikes.Add(new TrackLike { ProfileId = Profile, TrackId = source.ParanoidAndroid });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var renamed = await target.Db.Albums.SingleAsync(TestContext.Current.CancellationToken);
        renamed.Title = "OK Computer (Remastered)";
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new RatingsBackupSection(source.Db, source.References),
            new RatingsBackupSection(target.Db, target.References));

        (await target.Db.UserAlbumRatings.SingleAsync(TestContext.Current.CancellationToken)).AlbumId.Should().Be(target.OkComputer);
        (await target.Db.UserArtistRatings.SingleAsync(TestContext.Current.CancellationToken)).ArtistId.Should().Be(target.Radiohead);
        (await target.Db.TrackLikes.SingleAsync(TestContext.Current.CancellationToken)).TrackId.Should().Be(target.ParanoidAndroid);
        result.RowsSkipped.Should().Be(0);
    }

    [Fact]
    public async Task Playlist_entries_are_matched_renumbered_and_skipped_when_their_item_is_gone()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var playlist = new Playlist { Name = "Movie night", ProfileId = Profile };
        var stranger = new Playlist { Name = "Someone else's", ProfileId = Guid.NewGuid() };
        source.Db.Playlists.AddRange(playlist, stranger);
        source.Db.PlaylistItems.AddRange(
            new PlaylistItem { PlaylistId = playlist.Id, MediaItemId = source.OnlyOnSource, Order = 1 },
            new PlaylistItem { PlaylistId = playlist.Id, MediaItemId = source.Pilot, Order = 3 },
            new PlaylistItem { PlaylistId = playlist.Id, MediaItemId = source.Matrix, Order = 2 },
            new PlaylistItem { PlaylistId = stranger.Id, MediaItemId = source.Matrix, Order = 1 });
        source.Db.SmartPlaylists.Add(new SmartPlaylist { Name = "Recently liked", ProfileId = Profile });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new PlaylistsBackupSection(source.Db, source.References),
            new PlaylistsBackupSection(target.Db, target.References));

        (await target.Db.Playlists.SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be("Movie night");
        var entries = await target.Db.PlaylistItems.OrderBy(i => i.Order).ToListAsync(TestContext.Current.CancellationToken);
        entries.Select(e => (e.MediaItemId, e.Order)).Should().Equal((target.Matrix, 1), (target.Pilot, 2));
        (await target.Db.SmartPlaylists.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        result.Warnings.Should().Contain(w => w.StartsWith("1 playlist was skipped because its profile"));
        result.Warnings.Should().Contain(w => w.StartsWith("1 playlist entry was skipped because its profile"));
        result.Warnings.Should().Contain(w => w.StartsWith("1 playlist entry was skipped because its item"));
    }

    [Fact]
    public async Task Watchlist_entries_keep_their_external_identity_when_the_library_copy_is_gone()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        source.Db.UserWatchlistItems.AddRange(
            new UserWatchlistItem { Id = Guid.NewGuid(), ProfileId = Profile, Title = "The Matrix", ExternalId = "603", ProviderId = "tmdb_discovery", Type = "movie", MediaItemId = source.Matrix },
            new UserWatchlistItem { Id = Guid.NewGuid(), ProfileId = Profile, Title = "Gone Tomorrow", ExternalId = "999", ProviderId = "tmdb_discovery", Type = "movie", MediaItemId = source.OnlyOnSource },
            new UserWatchlistItem { Id = Guid.NewGuid(), ProfileId = Profile, Title = "Unmatched", Type = "movie", MediaItemId = Guid.NewGuid() });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new WatchlistsBackupSection(source.Db, source.References),
            new WatchlistsBackupSection(target.Db, target.References));

        var entries = await target.Db.UserWatchlistItems.ToListAsync(TestContext.Current.CancellationToken);
        entries.Single(e => e.ExternalId == "603").MediaItemId.Should().Be(target.Matrix);
        entries.Single(e => e.ExternalId == "999").MediaItemId.Should().BeNull();
        entries.Should().HaveCount(2);
        result.RowsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task Stations_follow_their_seed_and_are_skipped_when_it_is_gone()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        source.Db.Stations.AddRange(
            new Station { Name = "Radiohead Radio", ProfileId = Profile, SeedKind = StationSeedKind.Artist, SeedArtistId = source.Radiohead },
            new Station { Name = "Paranoid Android Radio", ProfileId = Profile, SeedKind = StationSeedKind.Track, SeedTrackId = source.ParanoidAndroid },
            new Station { Name = "Lost", ProfileId = Profile, SeedKind = StationSeedKind.Track, SeedTrackId = Guid.NewGuid() },
            new Station { Name = "Jazz", ProfileId = Profile, SeedKind = StationSeedKind.Genre, SeedGenre = "Jazz" });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new StationsBackupSection(source.Db, source.References),
            new StationsBackupSection(target.Db, target.References));

        var stations = await target.Db.Stations.ToListAsync(TestContext.Current.CancellationToken);
        stations.Single(s => s.Name == "Radiohead Radio").SeedArtistId.Should().Be(target.Radiohead);
        stations.Single(s => s.Name == "Paranoid Android Radio").SeedTrackId.Should().Be(target.ParanoidAndroid);
        stations.Should().Contain(s => s.Name == "Jazz");
        stations.Should().NotContain(s => s.Name == "Lost");
        result.RowsSkipped.Should().Be(1);
    }
}
