using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;

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
}
