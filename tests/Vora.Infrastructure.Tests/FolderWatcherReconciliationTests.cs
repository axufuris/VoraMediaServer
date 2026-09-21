using Vora.Domain.Enums;
using Vora.Infrastructure.FileSystem;
using Vora.Plugins;

namespace Vora.Infrastructure.Tests;

public class FolderWatcherReconciliationTests
{
    [Fact]
    public void FindUningestedFiles_returns_files_on_disk_that_are_not_ingested()
    {
        var disk = new[] { "/media/shows/a.mkv", "/media/shows/b.mkv" };
        var ingested = new HashSet<string> { "/media/shows/a.mkv" };

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>(), LibraryType.TvShow);

        result.Should().ContainSingle().Which.Should().Be("/media/shows/b.mkv");
    }

    [Fact]
    public void FindUningestedFiles_returns_empty_when_everything_is_ingested()
    {
        var disk = new[] { "/media/shows/a.mkv", "/media/shows/b.mkv" };
        var ingested = new HashSet<string> { "/media/shows/a.mkv", "/media/shows/b.mkv" };

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>(), LibraryType.TvShow);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindUningestedFiles_skips_unsupported_extensions()
    {
        var disk = new[] { "/media/shows/a.mkv", "/media/shows/a.nfo", "/media/shows/a.txt", "/media/shows/a.srt" };
        var ingested = new HashSet<string>();

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>(), LibraryType.TvShow);

        result.Should().BeEquivalentTo(new[] { "/media/shows/a.mkv" });
    }

    [Fact]
    public void FindUningestedFiles_skips_files_matching_an_exclude_filter()
    {
        var disk = new[] { "/media/shows/Real.mkv", "/media/shows/Dune (2021).TDARR.mkv" };
        var ingested = new HashSet<string>();

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string> { ".TDARR" }, LibraryType.Movie);

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

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>(), LibraryType.TvShow);

        result.Should().ContainSingle().Which.Should().Be("/media/shows/President Curtis - S01E04-NORViNE.mkv");
    }

    [Fact]
    public void FindUningestedFiles_returns_music_files_for_a_music_library()
    {
        var disk = new[] { "/media/music/Artist/Album/01.flac", "/media/music/Artist/Album/02.mp3" };
        var ingested = new HashSet<string> { "/media/music/Artist/Album/01.flac" };

        var result = FolderWatcherService.FindUningestedFiles(disk, ingested, new List<string>(), LibraryType.Music);

        result.Should().ContainSingle().Which.Should().Be("/media/music/Artist/Album/02.mp3");
    }

    [Fact]
    public void FindUningestedFiles_skips_music_files_in_a_video_library()
    {
        var disk = new[] { "/media/movies/Dune (2021).mkv", "/media/movies/Soundtrack.mp3" };

        var result = FolderWatcherService.FindUningestedFiles(disk, new HashSet<string>(), new List<string>(), LibraryType.Movie);

        result.Should().BeEquivalentTo(new[] { "/media/movies/Dune (2021).mkv" });
    }

    [Fact]
    public void FindUningestedFiles_skips_video_and_cover_art_in_a_music_library()
    {
        var disk = new[]
        {
            "/media/music/Artist/Album/01.flac",
            "/media/music/Artist/Album/cover.jpg",
            "/media/music/Artist/Album/live.mkv",
        };

        var result = FolderWatcherService.FindUningestedFiles(disk, new HashSet<string>(), new List<string>(), LibraryType.Music);

        result.Should().BeEquivalentTo(new[] { "/media/music/Artist/Album/01.flac" });
    }

    // Reference equality, not equivalence: the watcher must READ the shared lists
    // rather than hold copies of them. A private copy is exactly how the watcher
    // came to be missing every audio format while the scanner supported eight.
    [Fact]
    public void Music_libraries_read_the_shared_audio_extension_list()
    {
        FolderWatcherService.ExtensionsFor(LibraryType.Music).Should().BeSameAs(MediaFileExtensions.Audio);
    }

    [Fact]
    public void Video_libraries_read_the_shared_video_extension_list()
    {
        FolderWatcherService.ExtensionsFor(LibraryType.Movie).Should().BeSameAs(MediaFileExtensions.Video);
        FolderWatcherService.ExtensionsFor(LibraryType.TvShow).Should().BeSameAs(MediaFileExtensions.Video);
    }
}
