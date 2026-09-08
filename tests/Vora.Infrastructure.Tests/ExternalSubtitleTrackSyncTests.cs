using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

// Embedded tracks reconcile by stream index; sidecars have no stream index and
// reconcile by path. Running them through one pass would delete every sidecar on
// each ffprobe — and throw outright, since two sidecars share the default index.
public class ExternalSubtitleTrackSyncTests
{
    private static readonly Guid PartId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("external-subs-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaRepository Repo(VoraDbContext db) => new(NullLogger<MediaRepository>.Instance, db);

    private static MediaSubtitleTrack Embedded(int streamIndex, string codec = "subrip") =>
        new() { StreamIndex = streamIndex, Codec = codec, MediaPartId = PartId };

    private static MediaSubtitleTrack External(string path, string? language = null, bool forced = false) =>
        new() { ExternalFilePath = path, Codec = "subrip", Language = language, IsForced = forced, MediaPartId = PartId };

    private static void Seed(VoraDbContext db, params MediaSubtitleTrack[] tracks)
    {
        db.MediaSubtitleTracks.AddRange(tracks);
        db.SaveChanges();
    }

    private static List<MediaSubtitleTrack> Tracks(VoraDbContext db) =>
        db.MediaSubtitleTracks.Where(t => t.MediaPartId == PartId).ToList();

    // The failure this guards is total: an ffprobe pass that reconciled sidecars
    // would wipe every one of them on the next scan.
    [Fact]
    public async Task An_ffprobe_sync_leaves_sidecar_tracks_alone()
    {
        using var db = NewContext();
        Seed(db, Embedded(2), External("/media/Movie.en.srt"), External("/media/Movie.fr.srt"));

        await Repo(db).SyncMediaTracksAsync(PartId, [], [], [Embedded(2)]);

        Tracks(db).Where(t => t.ExternalFilePath != null).Should().HaveCount(2);
    }

    // Sidecars all carry the default stream index, so a keyed reconciliation over
    // the mixed set would throw on the duplicate before it deleted anything.
    [Fact]
    public async Task An_ffprobe_sync_survives_several_sidecars_sharing_a_stream_index()
    {
        using var db = NewContext();
        Seed(db, External("/media/Movie.en.srt"), External("/media/Movie.fr.srt"), External("/media/Movie.de.srt"));

        var act = async () => await Repo(db).SyncMediaTracksAsync(PartId, [], [], [Embedded(2)]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task An_ffprobe_sync_still_removes_embedded_tracks_that_are_gone()
    {
        using var db = NewContext();
        Seed(db, Embedded(2), Embedded(3), External("/media/Movie.en.srt"));

        await Repo(db).SyncMediaTracksAsync(PartId, [], [], [Embedded(2)]);

        Tracks(db).Where(t => t.ExternalFilePath == null).Should().ContainSingle().Which.StreamIndex.Should().Be(2);
    }

    [Fact]
    public async Task A_newly_appeared_sidecar_is_added()
    {
        using var db = NewContext();
        Seed(db, External("/media/Movie.en.srt"));

        await Repo(db).SyncExternalSubtitleTracksAsync(PartId, [External("/media/Movie.en.srt"), External("/media/Movie.fr.srt")]);

        Tracks(db).Select(t => t.ExternalFilePath)
            .Should().BeEquivalentTo(["/media/Movie.en.srt", "/media/Movie.fr.srt"]);
    }

    [Fact]
    public async Task A_sidecar_whose_file_is_gone_is_removed()
    {
        using var db = NewContext();
        Seed(db, External("/media/Movie.en.srt"), External("/media/Movie.fr.srt"));

        await Repo(db).SyncExternalSubtitleTracksAsync(PartId, [External("/media/Movie.en.srt")]);

        Tracks(db).Should().ContainSingle().Which.ExternalFilePath.Should().Be("/media/Movie.en.srt");
    }

    // Re-running the same discovery must not churn rows — the track id is what
    // the client and the subtitle cache both address, so a new id every scan
    // would invalidate both for no reason.
    [Fact]
    public async Task Re_running_discovery_keeps_the_same_row()
    {
        using var db = NewContext();
        Seed(db, External("/media/Movie.en.srt", language: "en"));
        var originalId = Tracks(db).Single().Id;

        await Repo(db).SyncExternalSubtitleTracksAsync(PartId, [External("/media/Movie.en.srt", language: "en")]);

        Tracks(db).Should().ContainSingle().Which.Id.Should().Be(originalId);
    }

    [Fact]
    public async Task A_renamed_sidecar_updates_its_metadata_in_place()
    {
        using var db = NewContext();
        Seed(db, External("/media/Movie.en.srt", language: "en"));

        await Repo(db).SyncExternalSubtitleTracksAsync(PartId, [External("/media/Movie.en.srt", language: "en", forced: true)]);

        Tracks(db).Should().ContainSingle().Which.IsForced.Should().BeTrue();
    }

    [Fact]
    public async Task Syncing_sidecars_leaves_embedded_tracks_alone()
    {
        using var db = NewContext();
        Seed(db, Embedded(2), Embedded(3));

        await Repo(db).SyncExternalSubtitleTracksAsync(PartId, [External("/media/Movie.en.srt")]);

        Tracks(db).Where(t => t.ExternalFilePath == null).Should().HaveCount(2);
    }

    [Fact]
    public async Task Discovering_nothing_clears_the_sidecars()
    {
        using var db = NewContext();
        Seed(db, Embedded(2), External("/media/Movie.en.srt"));

        await Repo(db).SyncExternalSubtitleTracksAsync(PartId, []);

        Tracks(db).Should().ContainSingle().Which.ExternalFilePath.Should().BeNull();
    }
}
