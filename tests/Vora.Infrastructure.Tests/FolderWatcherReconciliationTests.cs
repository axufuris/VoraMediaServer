using Vora.Infrastructure.FileSystem;

namespace Vora.Infrastructure.Tests;

public class FolderWatcherReconciliationTests
{
    [Fact]
    public void FindUningestedFiles_returns_files_on_disk_that_are_not_ingested()
    {
        var disk = new[] { "/media/shows/a.mkv", "/media/shows/b.mkv" };
        var ingested = new HashSet<string> { "/media/shows/a.mkv" };

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>());

        result.Should().ContainSingle().Which.Should().Be("/media/shows/b.mkv");
    }

    [Fact]
    public void FindUningestedFiles_returns_empty_when_everything_is_ingested()
    {
        var disk = new[] { "/media/shows/a.mkv", "/media/shows/b.mkv" };
        var ingested = new HashSet<string> { "/media/shows/a.mkv", "/media/shows/b.mkv" };

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindUningestedFiles_skips_unsupported_extensions()
    {
        var disk = new[] { "/media/shows/a.mkv", "/media/shows/a.nfo", "/media/shows/a.txt", "/media/shows/a.srt" };
        var ingested = new HashSet<string>();

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>());

        result.Should().BeEquivalentTo(new[] { "/media/shows/a.mkv" });
    }

    [Fact]
    public void FindUningestedFiles_skips_files_matching_an_exclude_filter()
    {
        var disk = new[] { "/media/shows/Real.mkv", "/media/shows/Dune (2021).TDARR.mkv" };
        var ingested = new HashSet<string>();

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string> { ".TDARR" });

        result.Should().BeEquivalentTo(new[] { "/media/shows/Real.mkv" });
    }

    [Fact]
    public void FindUningestedFiles_flags_a_second_same_episode_file_the_watcher_would_orphan()
    {
        // Two files for one episode: the first is already ingested, the second
        // (a different release group) is on disk but was never picked up.
        var disk = new[]
        {
            "/media/shows/President Curtis - S01E04-playWEB.mkv",
            "/media/shows/President Curtis - S01E04-NORViNE.mkv",
        };
        var ingested = new HashSet<string> { "/media/shows/President Curtis - S01E04-playWEB.mkv" };

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>());

        result.Should().ContainSingle().Which.Should().Be("/media/shows/President Curtis - S01E04-NORViNE.mkv");
    }
}
