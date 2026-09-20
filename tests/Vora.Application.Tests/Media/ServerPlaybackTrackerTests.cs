using Vora.Application.Media;

namespace Vora.Application.Tests.Media;

public class ServerPlaybackTrackerTests
{
    private static ServerPlaybackHeartbeat MakeBeat(Guid profileId, Guid trackId, string profileName = "Andy") => new()
    {
        ProfileId = profileId,
        ProfileName = profileName,
        TrackId = trackId,
        TrackTitle = "song"
    };

    [Fact]
    public void Heartbeat_creates_session_on_first_beat()
    {
        var tracker = new ServerPlaybackTracker();
        var profileId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        tracker.Heartbeat(MakeBeat(profileId, trackId));

        var active = tracker.GetActive(excludeProfileId: null);
        active.Should().ContainSingle();
        active[0].ProfileId.Should().Be(profileId);
        active[0].TrackId.Should().Be(trackId);
    }

    [Fact]
    public void Heartbeat_keeps_started_at_when_same_track_continues()
    {
        var tracker = new ServerPlaybackTracker();
        var profileId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        tracker.Heartbeat(MakeBeat(profileId, trackId));
        var startedAfterFirst = tracker.GetActive(null).Single().StartedAt;

        Thread.Sleep(20);
        tracker.Heartbeat(MakeBeat(profileId, trackId));

        var session = tracker.GetActive(null).Single();
        session.StartedAt.Should().Be(startedAfterFirst);
        session.LastHeartbeatAt.Should().BeAfter(startedAfterFirst);
    }

    [Fact]
    public void Heartbeat_resets_started_at_when_track_changes()
    {
        var tracker = new ServerPlaybackTracker();
        var profileId = Guid.NewGuid();
        var firstTrack = Guid.NewGuid();
        var secondTrack = Guid.NewGuid();

        tracker.Heartbeat(MakeBeat(profileId, firstTrack));
        var firstStart = tracker.GetActive(null).Single().StartedAt;

        Thread.Sleep(20);
        tracker.Heartbeat(MakeBeat(profileId, secondTrack));

        var session = tracker.GetActive(null).Single();
        session.TrackId.Should().Be(secondTrack);
        session.StartedAt.Should().BeAfter(firstStart);
    }

    [Fact]
    public void Heartbeat_updates_mutable_fields()
    {
        var tracker = new ServerPlaybackTracker();
        var profileId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        tracker.Heartbeat(new ServerPlaybackHeartbeat
        {
            ProfileId = profileId,
            ProfileName = "Andy",
            TrackId = trackId,
            TrackTitle = "Track A",
            Artist = "Artist A",
            DurationSeconds = 100,
            CurrentTimeSeconds = 10
        });
        tracker.Heartbeat(new ServerPlaybackHeartbeat
        {
            ProfileId = profileId,
            ProfileName = "Andy",
            TrackId = trackId,
            TrackTitle = "Track A",
            Artist = "Artist A",
            DurationSeconds = 100,
            CurrentTimeSeconds = 30
        });

        var session = tracker.GetActive(null).Single();
        session.CurrentTimeSeconds.Should().Be(30);
    }

    [Fact]
    public void Stop_removes_session_for_profile()
    {
        var tracker = new ServerPlaybackTracker();
        var profileId = Guid.NewGuid();

        tracker.Heartbeat(MakeBeat(profileId, Guid.NewGuid()));
        tracker.Stop(profileId);

        tracker.GetActive(null).Should().BeEmpty();
    }

    [Fact]
    public void Stop_is_a_no_op_for_unknown_profile()
    {
        var tracker = new ServerPlaybackTracker();
        var existing = Guid.NewGuid();
        tracker.Heartbeat(MakeBeat(existing, Guid.NewGuid()));

        tracker.Stop(Guid.NewGuid());

        tracker.GetActive(null).Should().ContainSingle();
    }

    [Fact]
    public void GetActive_excludes_specified_profile()
    {
        var tracker = new ServerPlaybackTracker();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();

        tracker.Heartbeat(MakeBeat(me, Guid.NewGuid(), "Me"));
        tracker.Heartbeat(MakeBeat(other, Guid.NewGuid(), "Other"));

        var visible = tracker.GetActive(excludeProfileId: me);

        visible.Should().ContainSingle();
        visible[0].ProfileId.Should().Be(other);
    }

    [Fact]
    public void GetActive_orders_by_last_heartbeat_descending()
    {
        var tracker = new ServerPlaybackTracker();
        var oldProfile = Guid.NewGuid();
        var newProfile = Guid.NewGuid();

        tracker.Heartbeat(MakeBeat(oldProfile, Guid.NewGuid(), "Old"));
        Thread.Sleep(20);
        tracker.Heartbeat(MakeBeat(newProfile, Guid.NewGuid(), "New"));

        var active = tracker.GetActive(null);

        active[0].ProfileId.Should().Be(newProfile);
        active[1].ProfileId.Should().Be(oldProfile);
    }

    [Fact]
    public void GetActive_returns_empty_when_no_sessions()
    {
        new ServerPlaybackTracker().GetActive(null).Should().BeEmpty();
    }
}
