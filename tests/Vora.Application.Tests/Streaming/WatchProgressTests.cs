using Vora.Application.Streaming;

namespace Vora.Application.Tests.Streaming;

public class WatchProgressTests
{
    private const double Movie = 8700;

    [Theory]
    [InlineData(0, 8699, 8196, 99)]
    [InlineData(0, 8699, 300, 3)]
    [InlineData(4200, 8690, 4400, 99)]
    [InlineData(4200, 8690, 300, 51)]
    [InlineData(0, 6, 14, 0)]
    public void Progress_counts_the_end_only_when_most_of_the_way_there_was_watched(double start, double end, double watched, int expected)
    {
        WatchProgress.PercentComplete(start, end, watched, Movie).Should().Be(expected);
    }

    [Fact]
    public void Skipping_an_intro_and_the_credits_still_finishes_an_episode()
    {
        WatchProgress.PercentComplete(0, 1415, 1240, 1518).Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public void Jumping_to_the_end_of_an_episode_in_half_a_minute_is_barely_watched()
    {
        WatchProgress.PercentComplete(0, 3442, 29, 3442).Should().Be(0);
    }

    [Fact]
    public void Rewinding_leaves_the_position_where_playback_stopped()
    {
        WatchProgress.PercentComplete(3000, 1200, 600, Movie).Should().Be(13);
    }

    [Fact]
    public void An_unknown_length_reports_nothing()
    {
        WatchProgress.PercentComplete(0, 500, 500, 0).Should().Be(0);
    }

    [Fact]
    public void Watched_time_leaves_out_pauses_and_never_goes_negative()
    {
        var started = new DateTime(2026, 10, 6, 3, 16, 54, DateTimeKind.Utc);

        WatchProgress.WatchedSeconds(started, started.AddMinutes(158.5), 1314).Should().Be(8196);
        WatchProgress.WatchedSeconds(started, started.AddSeconds(10), 30).Should().Be(0);
    }
}
