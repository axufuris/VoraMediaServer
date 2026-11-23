using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Streaming;

namespace Vora.Application.Tests.Iptv;

public class TimeshiftCoordinatorTests : IDisposable
{
    private readonly string _root;
    private readonly TimeshiftCoordinator _coordinator;

    public TimeshiftCoordinatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vora-timeshift-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _coordinator = new TimeshiftCoordinator(NullLogger<TimeshiftCoordinator>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task An_idle_live_remux_is_stopped_and_its_files_deleted_while_a_timeshift_keeps_its_longer_allowance()
    {
        var remuxViewer = Guid.NewGuid();
        var timeshiftViewer = Guid.NewGuid();
        var remuxDir = CreateProfileSessionDirectory(remuxViewer);
        var timeshiftDir = CreateProfileSessionDirectory(timeshiftViewer);
        _coordinator.TryRegister(remuxViewer, new Process(), remuxDir, LiveSessionKind.Remux).Should().BeTrue();
        _coordinator.TryRegister(timeshiftViewer, new Process(), timeshiftDir).Should().BeTrue();

        var evicted = await _coordinator.EvictStaleSessionsAsync(TimeSpan.FromHours(1), TimeSpan.FromHours(6), remuxMaxIdle: TimeSpan.Zero);

        evicted.Should().Equal(remuxViewer);
        Directory.Exists(remuxDir).Should().BeFalse();
        _coordinator.IsActive(timeshiftViewer).Should().BeTrue();
        _coordinator.KindOf(timeshiftViewer).Should().Be(LiveSessionKind.Timeshift);
        _coordinator.KindOf(remuxViewer).Should().BeNull();
    }

    [Fact]
    public async Task A_live_remux_that_is_fetched_from_stays_alive()
    {
        var viewer = Guid.NewGuid();
        _coordinator.TryRegister(viewer, new Process(), CreateProfileSessionDirectory(viewer), LiveSessionKind.Remux);
        _coordinator.Heartbeat(viewer);

        var evicted = await _coordinator.EvictStaleSessionsAsync(TimeSpan.FromHours(1), TimeSpan.FromHours(6), remuxMaxIdle: TimeSpan.FromMinutes(1));

        evicted.Should().BeEmpty();
        _coordinator.KindOf(viewer).Should().Be(LiveSessionKind.Remux);
    }

    [Fact]
    public async Task ReapOrphanedDirectoriesAsync_deletes_directories_older_than_threshold()
    {
        var orphanProfileId = Guid.NewGuid();
        var orphanDir = CreateProfileSessionDirectory(orphanProfileId);
        SetDirectoryFilesLastWriteUtc(orphanDir, DateTime.UtcNow - TimeSpan.FromHours(2));

        await _coordinator.ReapOrphanedDirectoriesAsync(_root, TimeSpan.FromMinutes(15));

        Assert.False(Directory.Exists(Path.Combine(_root, orphanProfileId.ToString())));
    }

    [Fact]
    public async Task ReapOrphanedDirectoriesAsync_leaves_recently_touched_directories_alone()
    {
        var freshProfileId = Guid.NewGuid();
        var freshDir = CreateProfileSessionDirectory(freshProfileId);
        SetDirectoryFilesLastWriteUtc(freshDir, DateTime.UtcNow - TimeSpan.FromMinutes(2));

        await _coordinator.ReapOrphanedDirectoriesAsync(_root, TimeSpan.FromMinutes(15));

        Assert.True(Directory.Exists(freshDir));
    }

    [Fact]
    public async Task ReapOrphanedDirectoriesAsync_skips_active_profiles_even_if_files_are_old()
    {
        var activeProfileId = Guid.NewGuid();
        var activeDir = CreateProfileSessionDirectory(activeProfileId);
        SetDirectoryFilesLastWriteUtc(activeDir, DateTime.UtcNow - TimeSpan.FromHours(5));

        var process = new Process();
        var registered = _coordinator.TryRegister(activeProfileId, process, activeDir);
        Assert.True(registered);

        await _coordinator.ReapOrphanedDirectoriesAsync(_root, TimeSpan.FromMinutes(15));

        Assert.True(Directory.Exists(activeDir));
    }

    [Fact]
    public async Task ReapOrphanedDirectoriesAsync_ignores_non_guid_directory_names()
    {
        var notAProfileDir = Path.Combine(_root, "random-junk-dir");
        Directory.CreateDirectory(notAProfileDir);
        File.WriteAllText(Path.Combine(notAProfileDir, "junk.txt"), "x");
        SetDirectoryFilesLastWriteUtc(notAProfileDir, DateTime.UtcNow - TimeSpan.FromDays(7));

        await _coordinator.ReapOrphanedDirectoriesAsync(_root, TimeSpan.FromMinutes(15));

        Assert.True(Directory.Exists(notAProfileDir));
    }

    [Fact]
    public async Task ReapOrphanedDirectoriesAsync_is_a_noop_when_root_does_not_exist()
    {
        var missing = Path.Combine(_root, "nope");

        await _coordinator.ReapOrphanedDirectoriesAsync(missing, TimeSpan.FromMinutes(15));
    }

    private string CreateProfileSessionDirectory(Guid profileId)
    {
        var profileDir = Path.Combine(_root, profileId.ToString());
        var sessionDir = Path.Combine(profileDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "seg_001.ts"), new string('x', 1024));
        File.WriteAllText(Path.Combine(sessionDir, "index.m3u8"), "#EXTM3U");
        return profileDir;
    }

    private static void SetDirectoryFilesLastWriteUtc(string directory, DateTime utcTime)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, utcTime);
        }
    }

    [Fact]
    public void Only_sessions_that_encode_and_whose_ffmpeg_is_running_hold_an_encoder()
    {
        using var running = Process.GetCurrentProcess();
        var encodingOnGpu = Guid.NewGuid();
        var encodingOnCpu = Guid.NewGuid();
        var copying = Guid.NewGuid();
        var stopped = Guid.NewGuid();

        _coordinator.TryRegister(encodingOnGpu, running, CreateProfileSessionDirectory(encodingOnGpu), LiveSessionKind.Timeshift, new RunningEncode(OnGpu: true)).Should().BeTrue();
        _coordinator.TryRegister(encodingOnCpu, running, CreateProfileSessionDirectory(encodingOnCpu), LiveSessionKind.Remux, new RunningEncode(OnGpu: false)).Should().BeTrue();
        _coordinator.TryRegister(copying, running, CreateProfileSessionDirectory(copying)).Should().BeTrue();
        _coordinator.TryRegister(stopped, new Process(), CreateProfileSessionDirectory(stopped), LiveSessionKind.Timeshift, new RunningEncode(OnGpu: true)).Should().BeTrue();

        _coordinator.RunningEncodes(Guid.NewGuid()).Should().BeEquivalentTo([new RunningEncode(true), new RunningEncode(false)]);
    }

    [Fact]
    public void A_session_restarting_is_not_counted_against_itself()
    {
        using var running = Process.GetCurrentProcess();
        var viewer = Guid.NewGuid();
        _coordinator.TryRegister(viewer, running, CreateProfileSessionDirectory(viewer), LiveSessionKind.Timeshift, new RunningEncode(OnGpu: true));

        _coordinator.RunningEncodes(viewer).Should().BeEmpty();
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().ContainSingle();
    }

    private static readonly LiveVideoEncode Encode41 = new(41, null, true);

    private Guid RegisterShared(Guid channel, Process? process = null, LiveSessionKind kind = LiveSessionKind.Timeshift, LiveVideoEncode? encode = null)
    {
        var session = Guid.NewGuid();
        _coordinator.TryRegister(session, process ?? new Process(), CreateProfileSessionDirectory(session), kind, encode is null ? null : new RunningEncode(OnGpu: false), new LiveSessionShare(channel, encode, null))
            .Should().BeTrue();
        return session;
    }

    [Fact]
    public async Task A_session_stops_only_when_its_last_viewer_leaves()
    {
        var session = RegisterShared(Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        (await _coordinator.AttachAsync(first, session)).Attached.Should().BeTrue();
        (await _coordinator.AttachAsync(second, session)).Attached.Should().BeTrue();

        (await _coordinator.DetachAsync(first)).Should().BeNull();
        _coordinator.IsActive(session).Should().BeTrue();
        _coordinator.SessionOfViewer(first).Should().BeNull();
        _coordinator.SessionOfViewer(second).Should().Be(session);

        (await _coordinator.DetachAsync(second)).Should().Be(session, "the key of the session that stopped, for the caller to free its tuner");
        _coordinator.IsActive(session).Should().BeFalse();
    }

    [Fact]
    public async Task A_viewer_who_is_not_watching_has_nothing_to_leave()
    {
        (await _coordinator.DetachAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task A_viewer_who_moves_to_another_session_stops_the_one_nobody_is_left_on()
    {
        var channel = Guid.NewGuid();
        var old = RegisterShared(channel);
        var next = RegisterShared(channel);
        var viewer = Guid.NewGuid();
        await _coordinator.AttachAsync(viewer, old);

        var moved = await _coordinator.AttachAsync(viewer, next);

        moved.Should().Be(new AttachResult(true, old));
        _coordinator.IsActive(old).Should().BeFalse();
        _coordinator.SessionOfViewer(viewer).Should().Be(next);
    }

    [Fact]
    public async Task A_viewer_who_moves_leaves_the_session_other_viewers_are_still_on_running()
    {
        var channel = Guid.NewGuid();
        var old = RegisterShared(channel);
        var next = RegisterShared(channel);
        var mover = Guid.NewGuid();
        var staying = Guid.NewGuid();
        await _coordinator.AttachAsync(mover, old);
        await _coordinator.AttachAsync(staying, old);

        var moved = await _coordinator.AttachAsync(mover, next);

        moved.Should().Be(new AttachResult(true, null));
        _coordinator.IsActive(old).Should().BeTrue();
        _coordinator.SessionOfViewer(staying).Should().Be(old);
    }

    [Fact]
    public async Task A_viewer_can_not_attach_to_a_session_that_is_gone()
    {
        (await _coordinator.AttachAsync(Guid.NewGuid(), Guid.NewGuid())).Should().Be(new AttachResult(false, null));
    }

    [Fact]
    public async Task Stopping_a_session_forgets_the_viewers_on_it()
    {
        var session = RegisterShared(Guid.NewGuid(), kind: LiveSessionKind.Remux);
        var viewer = Guid.NewGuid();
        await _coordinator.AttachAsync(viewer, session);
        _coordinator.KindOfViewer(viewer).Should().Be(LiveSessionKind.Remux);

        await _coordinator.StopAsync(session);

        _coordinator.KindOfViewer(viewer).Should().BeNull();
        _coordinator.SessionOfViewer(viewer).Should().BeNull();
    }

    [Fact]
    public async Task Evicting_an_idle_session_forgets_its_viewers_and_names_the_session_not_the_viewer()
    {
        var session = RegisterShared(Guid.NewGuid());
        var viewer = Guid.NewGuid();
        await _coordinator.AttachAsync(viewer, session);

        var evicted = await _coordinator.EvictStaleSessionsAsync(TimeSpan.Zero, TimeSpan.FromHours(6));

        evicted.Should().Equal(session);
        _coordinator.SessionOfViewer(viewer).Should().BeNull();
    }

    [Fact]
    public async Task A_viewers_ping_keeps_the_session_it_watches_alive()
    {
        var session = RegisterShared(Guid.NewGuid());
        var viewer = Guid.NewGuid();
        await _coordinator.AttachAsync(viewer, session);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        _coordinator.HeartbeatViewer(viewer);
        var evicted = await _coordinator.EvictStaleSessionsAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromHours(6));

        evicted.Should().BeEmpty();
        _coordinator.IsActive(session).Should().BeTrue();
    }

    [Fact]
    public void Only_running_sessions_of_the_same_channel_and_kind_can_be_joined()
    {
        using var running = Process.GetCurrentProcess();
        var channel = Guid.NewGuid();
        var copy = RegisterShared(channel, running);
        var encode = RegisterShared(channel, running, encode: Encode41);
        RegisterShared(channel, running, LiveSessionKind.Remux);
        RegisterShared(Guid.NewGuid(), running);
        RegisterShared(channel);

        var shared = _coordinator.SharedSessions(channel, LiveSessionKind.Timeshift);

        shared.Select(s => s.SessionKey).Should().BeEquivalentTo([copy, encode]);
        shared.Single(s => s.SessionKey == encode).Encode.Should().Be(Encode41);
        shared.Single(s => s.SessionKey == copy).Encode.Should().BeNull();
    }

    [Fact]
    public void A_session_that_learned_its_source_says_so_to_the_next_viewer()
    {
        using var running = Process.GetCurrentProcess();
        var channel = Guid.NewGuid();
        var session = RegisterShared(channel, running);
        var format = new LiveVideoFormat("h264", "High", 41, 1080, 25, false);

        _coordinator.SetSource(session, format);

        _coordinator.SharedSessions(channel, LiveSessionKind.Timeshift).Single().Source.Should().Be(format);
    }

    [Fact]
    public async Task Sessions_whose_ffmpeg_has_stopped_are_cleared_for_that_channel_only()
    {
        var channel = Guid.NewGuid();
        var dead = RegisterShared(channel);
        var otherKind = RegisterShared(channel, kind: LiveSessionKind.Remux);
        var otherChannel = RegisterShared(Guid.NewGuid());
        var viewer = Guid.NewGuid();
        await _coordinator.AttachAsync(viewer, dead);

        var cleared = await _coordinator.StopExitedAsync(channel, LiveSessionKind.Timeshift);

        cleared.Should().Equal(dead);
        _coordinator.IsActive(dead).Should().BeFalse();
        _coordinator.SessionOfViewer(viewer).Should().BeNull();
        _coordinator.IsActive(otherKind).Should().BeTrue();
        _coordinator.IsActive(otherChannel).Should().BeTrue();
    }

    [Fact]
    public async Task Starts_for_one_channel_and_kind_wait_for_each_other_and_other_channels_do_not()
    {
        var channel = Guid.NewGuid();
        await using var first = await _coordinator.LockChannelAsync(channel, LiveSessionKind.Timeshift);

        var sameChannel = _coordinator.LockChannelAsync(channel, LiveSessionKind.Timeshift);
        await using var otherKind = await _coordinator.LockChannelAsync(channel, LiveSessionKind.Remux);
        await using var otherChannel = await _coordinator.LockChannelAsync(Guid.NewGuid(), LiveSessionKind.Timeshift);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        sameChannel.IsCompleted.Should().BeFalse();

        await first.DisposeAsync();
        await using var second = await sameChannel.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
