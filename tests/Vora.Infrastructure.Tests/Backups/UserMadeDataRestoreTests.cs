using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Backups.Sections;

namespace Vora.Infrastructure.Tests.Backups;

public sealed class UserMadeDataRestoreTests : IDisposable
{
    private static readonly Guid Profile = BackupTestWorld.ProfileId;
    private readonly string _recordings = Path.Combine(Path.GetTempPath(), "vora-dvr-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_recordings)) Directory.Delete(_recordings, recursive: true);
    }

    private static GeneratedMix Mix(GeneratedMixKind kind, int slot, params Guid[] tracks) => new()
    {
        ProfileId = Profile,
        Name = $"{kind} {slot}",
        Kind = kind,
        Slot = slot,
        TrackOrder = tracks.ToList()
    };

    [Fact]
    public async Task Requested_ai_playlists_and_blends_follow_their_songs_and_mixes_Vora_rebuilds_are_left_alone()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var workout = Mix(GeneratedMixKind.Requested, 1, source.ParanoidAndroid, source.OnlyOnSource);
        workout.Prompt = "Energizing workout beats";
        var blend = Mix(GeneratedMixKind.Blend, 1, source.ParanoidAndroid);
        blend.PartnerProfileId = Guid.NewGuid();
        source.Db.GeneratedMixes.AddRange(workout, blend, Mix(GeneratedMixKind.DailyMix, 1, source.ParanoidAndroid));
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var daily = Mix(GeneratedMixKind.DailyMix, 1, target.ParanoidAndroid);
        target.Db.GeneratedMixes.AddRange(daily, Mix(GeneratedMixKind.Requested, 9, target.ParanoidAndroid));
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(
            new AiPlaylistsBackupSection(source.Db, source.References),
            new AiPlaylistsBackupSection(target.Db, target.References));

        var mixes = await target.Db.GeneratedMixes.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        mixes.Select(m => m.Id).Should().BeEquivalentTo(new[] { daily.Id, workout.Id });
        var restored = mixes.Single(m => m.Id == workout.Id);
        restored.TrackOrder.Should().Equal(target.ParanoidAndroid);
        restored.Prompt.Should().Be("Energizing workout beats");
        result.Warnings.Should().Contain(w => w.StartsWith("1 AI playlist song was skipped because its item isn't on this server."));
        result.Warnings.Should().Contain(w => w.StartsWith("1 AI playlist was skipped because its profile isn't on this server."));
    }

    [Fact]
    public async Task Hidden_channels_and_tv_or_radio_choices_land_on_the_same_channels_after_a_refresh()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var playlistId = Guid.NewGuid();
        IptvChannel Channel(string id) => new() { PlaylistId = playlistId, ExternalChannelId = id, Name = id, StreamUrl = "http://tv/" + id };

        source.Db.IptvPlaylists.Add(new IptvPlaylist { Id = playlistId, Name = "Freeview" });
        var hidden = Channel("shopping.uk");
        hidden.IsHiddenByAdmin = true;
        var radio = Channel("radio1.uk");
        radio.Kind = IptvChannelKind.Radio;
        radio.KindOverriddenByAdmin = true;
        var notLoadedYet = Channel("new.uk");
        notLoadedYet.IsHiddenByAdmin = true;
        source.Db.IptvChannels.AddRange(hidden, radio, Channel("bbc1.uk"), notLoadedYet);
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        target.Db.IptvPlaylists.Add(new IptvPlaylist { Id = playlistId, Name = "Freeview" });
        target.Db.IptvChannels.AddRange(Channel("shopping.uk"), Channel("radio1.uk"), Channel("bbc1.uk"));
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(
            new IptvChannelSettingsBackupSection(source.Db),
            new IptvChannelSettingsBackupSection(target.Db));

        var channels = await target.Db.IptvChannels.AsNoTracking().ToDictionaryAsync(c => c.ExternalChannelId, TestContext.Current.CancellationToken);
        channels["shopping.uk"].IsHiddenByAdmin.Should().BeTrue();
        channels["radio1.uk"].Kind.Should().Be(IptvChannelKind.Radio);
        channels["radio1.uk"].KindOverriddenByAdmin.Should().BeTrue();
        channels["bbc1.uk"].IsHiddenByAdmin.Should().BeFalse();
        channels["bbc1.uk"].KindOverriddenByAdmin.Should().BeFalse();
        result.RowsImported.Should().Be(2);
        result.Warnings.Should().Contain(w => w.StartsWith("1 channel setting was skipped because its channel isn't on this server yet."));
    }

    [Fact]
    public async Task Finished_recordings_come_back_when_their_files_are_still_there()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        Directory.CreateDirectory(_recordings);
        var kept = Path.Combine(_recordings, "News_20261001_180000.mp4");
        await File.WriteAllBytesAsync(kept, new byte[16], TestContext.Current.CancellationToken);
        var scheduleId = Guid.NewGuid();
        IptvRecordingSchedule Schedule() => new() { Id = scheduleId, Title = "News", UserId = BackupTestWorld.UserId, ProfileId = Profile, ChannelId = Guid.NewGuid() };
        IptvRecordingSession Session(IptvRecordingSessionStatus status, string? file) => new()
        {
            Title = "News",
            ScheduleId = scheduleId,
            Status = status,
            OutputFilePath = file,
            StartTime = new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 10, 1, 18, 30, 0, DateTimeKind.Utc)
        };

        source.Db.IptvRecordingSchedules.Add(Schedule());
        var finished = Session(IptvRecordingSessionStatus.Completed, kept);
        var orphanedSchedule = Session(IptvRecordingSessionStatus.Completed, kept);
        orphanedSchedule.ScheduleId = Guid.NewGuid();
        source.Db.IptvRecordingSessions.AddRange(
            finished,
            orphanedSchedule,
            Session(IptvRecordingSessionStatus.Completed, Path.Combine(_recordings, "Gone_20250101_180000.mp4")),
            Session(IptvRecordingSessionStatus.Pending, null));
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        target.Db.IptvRecordingSchedules.Add(Schedule());
        var alreadyHere = Session(IptvRecordingSessionStatus.Completed, Path.Combine(_recordings, "Earlier_20250901_180000.mp4"));
        target.Db.IptvRecordingSessions.Add(alreadyHere);
        await target.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(
            new DvrRecordingsBackupSection(source.Db),
            new DvrRecordingsBackupSection(target.Db));

        (await target.Db.IptvRecordingSessions.Select(s => s.Id).ToListAsync(TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(new[] { alreadyHere.Id, finished.Id });
        result.Warnings.Should().Contain(w => w.StartsWith("1 recording was skipped because its file isn't on this server."));
        result.Warnings.Should().Contain(w => w.StartsWith("1 recording was skipped because its recording schedule isn't on this server."));
    }

    [Fact]
    public async Task Images_uploaded_for_a_collection_come_back_with_it_and_provider_images_do_not()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var collection = new Collection { Title = "Favourites", PosterUrl = "/api/artwork/custom/coll_1_poster_a.jpg" };
        source.Db.Collections.Add(collection);
        source.Db.CollectionArtwork.AddRange(
            new CollectionArtwork { CollectionId = collection.Id, Url = "/api/artwork/custom/coll_1_poster_a.jpg", Kind = ArtworkKind.Poster, IsUserUploaded = true, ProviderId = "upload" },
            new CollectionArtwork { CollectionId = collection.Id, Url = "https://image.tmdb.org/t/p/original/x.jpg", Kind = ArtworkKind.Poster, ProviderId = "tmdb" });
        await source.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await BackupTestWorld.CopyAsync(
            new CollectionsBackupSection(source.Db, source.References),
            new CollectionsBackupSection(target.Db, target.References));

        (await target.Db.Collections.SingleAsync(TestContext.Current.CancellationToken)).PosterUrl.Should().Be("/api/artwork/custom/coll_1_poster_a.jpg");
        (await target.Db.CollectionArtwork.Select(a => a.Url).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Equal("/api/artwork/custom/coll_1_poster_a.jpg");
    }
}
