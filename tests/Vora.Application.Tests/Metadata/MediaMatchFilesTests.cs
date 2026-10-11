using Vora.Application.Metadata;

namespace Vora.Application.Tests.Metadata;

public class MediaMatchFilesTests
{
    [Theory]
    [InlineData("/media/movies/The Matrix (1999)/The.Matrix.1999.1080p.BluRay.x264.mkv", "The.Matrix.1999.1080p.BluRay.x264.mkv")]
    [InlineData(@"D:\Movies\Heat (1995)\Heat.mkv", "Heat.mkv")]
    [InlineData("Heat.mkv", "Heat.mkv")]
    public void A_file_name_is_the_last_part_of_its_path(string path, string expected)
    {
        MediaMatchFiles.FileName(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("/media/tv/Breaking Bad (2008)/Season 01/Breaking.Bad.S01E01.mkv", "/media/tv")]
    [InlineData("/media/tv/Breaking Bad (2008)/Season 01/Breaking.Bad.S01E01.mkv", "/media/tv/")]
    [InlineData("/media/tv/Breaking Bad (2008)/Breaking.Bad.S01E01.mkv", "/media/tv")]
    [InlineData("/media/tv/Breaking Bad (2008)/Season 01/Extras/Breaking.Bad.S01E01.mkv", "/media/tv")]
    public void A_show_folder_is_the_first_folder_under_its_library_root(string episodePath, string root)
    {
        MediaMatchFiles.ShowFolderName(episodePath, [root]).Should().Be("Breaking Bad (2008)");
    }

    [Fact]
    public void The_deepest_matching_library_root_wins()
    {
        MediaMatchFiles.ShowFolderName("/media/tv/anime/Frieren (2023)/Season 1/Frieren.S01E01.mkv", ["/media/tv", "/media/tv/anime"])
            .Should().Be("Frieren (2023)");
    }

    [Fact]
    public void An_episode_sitting_directly_in_the_library_root_has_no_show_folder()
    {
        MediaMatchFiles.ShowFolderName("/media/tv/Breaking.Bad.S01E01.mkv", ["/media/tv"]).Should().BeNull();
    }

    [Theory]
    [InlineData("/elsewhere/Breaking Bad (2008)/Season 01/Breaking.Bad.S01E01.mkv")]
    [InlineData("/elsewhere/Breaking Bad (2008)/S01/Breaking.Bad.S01E01.mkv")]
    [InlineData("/elsewhere/Breaking Bad (2008)/Specials/Breaking.Bad.S00E01.mkv")]
    [InlineData("/elsewhere/Breaking Bad (2008)/Breaking.Bad.S01E01.mkv")]
    public void Outside_every_library_root_the_season_folder_is_skipped(string episodePath)
    {
        MediaMatchFiles.ShowFolderName(episodePath, ["/media/tv"]).Should().Be("Breaking Bad (2008)");
    }
}
