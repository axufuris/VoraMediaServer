using Vora.Application.Media;

namespace Vora.Application.Tests.Media;

public class MediaDedupeStackedEpisodeTests
{
    [Fact]
    public void Files_named_as_different_episodes_are_named_on_the_group()
    {
        MediaDedupeManager.DifferentEpisodesNamed(
        [
            "/tv/Ted Lasso/Season 1/Ted Lasso.1x2 - Biscuits.mkv",
            "/tv/Ted Lasso/Season 1/Ted Lasso.1x1 - Pilot.mkv",
            "/tv/Ted Lasso/Season 2/Ted Lasso.2x9 - Beard After Hours.mp4",
        ]).Should().Equal("S01E01", "S01E02", "S02E09");
    }

    [Fact]
    public void Two_copies_of_one_episode_are_real_duplicates()
    {
        MediaDedupeManager.DifferentEpisodesNamed(
        [
            "/tv/1883 (2021)/Season 01/1883 (2021) - S01E01 - 1883 [Bluray-1080p Remux][TrueHD 5.1][AVC]-MJOLNiR.mkv",
            "/tv/1883 (2021)/Season 01/1883 (2021) - S01E01 - 1883 [WEBDL-1080p][EAC3 5.1][h264]-NTb.mkv",
            "/tv/1883 (2021)/Season 01/Bonus Material.mkv",
        ]).Should().BeEmpty();
    }
}
