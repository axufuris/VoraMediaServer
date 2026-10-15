using Vora.Plugins;

namespace Vora.Infrastructure.Tests;

// The scanner and the folder watcher both decide what to leave alone with this
// one rule. When they disagreed, a file one skipped and the other counted came
// back as "new" on every start, and enough of them queued a full library scan
// on every restart.
public class LibraryFileFilterTests
{
    [Fact]
    public void Server_wide_ignored_folders_join_the_librarys_own_filters()
    {
        LibraryFileFilter.Combine(new[] { ".TDARR" }, new[] { ".recycle", ".TDARR", " " })
            .Should().Equal(".TDARR", ".recycle");
    }

    [Theory]
    [InlineData("/music/.recycle/Artist/01.mp3", true)]
    [InlineData("/music/Artist/Album.TDARR/01.mp3", false)]
    [InlineData("/music/Artist/01 Song.TDARR.mp3", true)]
    [InlineData("/music/Artist/Album/._01 Song.mp3", true)]
    [InlineData("/music/Artist/Album/01 Song.mp3", false)]
    [InlineData("/music/recycle bin/01.mp3", false)]
    public void A_file_is_excluded_by_its_name_a_whole_folder_on_its_path_or_being_a_resource_fork(string path, bool excluded)
    {
        LibraryFileFilter.IsExcluded(path, new List<string> { ".recycle", ".TDARR" }).Should().Be(excluded);
    }

    [Fact]
    public void Resource_forks_are_skipped_even_with_no_filters()
    {
        LibraryFileFilter.IsExcluded("/music/._Song.flac", null).Should().BeTrue();
        LibraryFileFilter.IsExcluded("/music/Song.flac", null).Should().BeFalse();
    }

    [Theory]
    [InlineData("/tv/Ted Lasso/Season 2/Plex Versions/Original Quality/Ted Lasso/S02E09.mp4", true)]
    [InlineData("/movies/Heat (1995)/Plex Versions/Optimized for TV/Heat (1995).mp4", true)]
    [InlineData("/tv/Plex Versions Fan Club/Season 1/Show - S01E01 - Pilot.mkv", false)]
    [InlineData("/tv/Ted Lasso/Season 2/Ted Lasso.2x9 - Beard After Hours.mp4", false)]
    public void Plexs_own_optimized_copies_are_left_alone_with_no_filter_set(string path, bool excluded)
    {
        LibraryFileFilter.IsExcluded(path, null).Should().Be(excluded);
    }
}
