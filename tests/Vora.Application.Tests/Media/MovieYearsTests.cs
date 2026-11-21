using Vora.Application.Media;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Media;

public class MovieYearsTests
{
    [Fact]
    public void Files_named_with_different_years_are_named_on_the_group()
    {
        MovieYears.Named(
        [
            "/movies/A Nightmare on Elm Street (2010) Bluray-2160p.mkv",
            "/movies/A Nightmare on Elm Street (1984) Bluray-2160p Proper.mp4",
        ]).Should().Equal(1984, 2010);
    }

    [Fact]
    public void One_film_in_two_resolutions_is_a_real_duplicate()
    {
        MovieYears.Named(
        [
            "/movies/Heat (1995) Bluray-1080p.mkv",
            "/movies/Heat (1995) Bluray-2160p.mkv",
        ]).Should().BeEmpty();
    }

    [Fact]
    public void A_year_apart_is_within_tolerance()
    {
        MovieYears.Named(["/movies/Heat (1995).mkv", "/movies/Heat (1996).mkv"]).Should().BeEmpty();
    }

    [Fact]
    public void Two_years_apart_are_different_films()
    {
        MovieYears.Named(["/movies/Heat (1995).mkv", "/movies/Heat (1997).mkv"]).Should().Equal(1995, 1997);
    }

    [Fact]
    public void Files_without_a_year_are_not_counted()
    {
        MovieYears.Named(["/movies/Heat.mkv", "/movies/Heat (1995).mkv"]).Should().BeEmpty();
        MovieYears.Named(["/movies/Heat.mkv", "/movies/Heat 4K.mkv"]).Should().BeEmpty();
    }

    [Fact]
    public void The_year_comes_from_the_folder_or_a_release_name()
    {
        MovieYears.In("/movies/Heat (1995)/Heat.mkv").Should().Be(1995);
        MovieYears.In("/movies/heat.1995.1080p.bluray.x264-group.mkv").Should().Be(1995);
        MovieYears.In("/movies/Heat.mkv").Should().BeNull();
    }

    [Fact]
    public void The_film_keeps_the_files_of_the_year_it_was_released()
    {
        MovieYears.FilesOfOtherYears(
            [("/a/Elm (2010).mkv", 2010), ("/b/Elm (1984).mp4", 1984)], 1984)
            .Should().Equal("/a/Elm (2010).mkv");
        MovieYears.FilesOfOtherYears(
            [("/a/Elm (2010).mkv", 2010), ("/b/Elm (1984).mp4", 1984)], 2010)
            .Should().Equal("/b/Elm (1984).mp4");
    }

    [Fact]
    public void Without_a_matching_year_the_largest_group_stays_and_ties_go_to_the_earliest_year()
    {
        MovieYears.FilesOfOtherYears(
            [("/a", 2010), ("/b", 2010), ("/c", 1984)], 1999).Should().Equal("/c");
        MovieYears.FilesOfOtherYears(
            [("/a", 2010), ("/c", 1984)], null).Should().Equal("/a");
    }

    [Fact]
    public void Same_year_neighbouring_years_and_undated_files_stay()
    {
        MovieYears.FilesOfOtherYears([("/a", 1995), ("/b", 1995)], 1995).Should().BeEmpty();
        MovieYears.FilesOfOtherYears([("/a", 1995), ("/b", 1996)], 1995).Should().BeEmpty();
        MovieYears.FilesOfOtherYears([("/a", 1995), ("/b", null)], 2010).Should().BeEmpty();
        MovieYears.FilesOfOtherYears([("/a", 2010), ("/b", null), ("/c", 1984)], 2010).Should().Equal("/c");
    }
}
