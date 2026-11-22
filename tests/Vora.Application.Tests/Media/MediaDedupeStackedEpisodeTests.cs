using Vora.Application.Media;
using Vora.Application.Media.ViewModels;

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

    [Fact]
    public void Files_tied_on_score_rank_the_higher_bitrate_then_the_larger_file_first()
    {
        var lower = new DedupeItemVM { FilePath = "/a.flac", QualityScore = 2131, Bitrate = 928_000, FileSizeBytes = 22_170_000 };
        var higher = new DedupeItemVM { FilePath = "/b.flac", QualityScore = 2131, Bitrate = 945_000, FileSizeBytes = 22_580_000 };
        var larger = new DedupeItemVM { FilePath = "/c.flac", QualityScore = 2131, Bitrate = 945_000, FileSizeBytes = 22_900_000 };
        var better = new DedupeItemVM { FilePath = "/d.flac", QualityScore = 2200, Bitrate = 100_000, FileSizeBytes = 1 };

        MediaDedupeManager.Ranked([lower, higher, larger, better]).Select(p => p.FilePath)
            .Should().Equal("/d.flac", "/c.flac", "/b.flac", "/a.flac");
        MediaDedupeManager.Ranked([higher, lower]).Select(p => p.FilePath).Should().Equal("/b.flac", "/a.flac");
    }
}
