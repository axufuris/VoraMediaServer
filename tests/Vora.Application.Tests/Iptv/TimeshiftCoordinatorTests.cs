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
}
