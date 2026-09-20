using Vora.Application.Streaming;

namespace Vora.Application.Tests.Streaming;

// The played flag used to be sticky — `IsPlayed || completed` — so an item that
// had once been finished could never become unfinished. Starting it again
// recorded a resume position nothing would show: the details page kept the
// watched check and offered Play instead of Resume, and Continue Watching left
// the item out because it was still marked played. Marking it unwatched by hand
// was the only way out, which is how the bug was found.
public class WatchStateTransitionTests
{
    private const double Movie = 96 * 60;

    [Fact]
    public void Reaching_the_end_marks_an_item_watched()
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: false, Movie * 0.95, Movie).Should().BeTrue();
    }

    // The whole point of the fix.
    [Fact]
    public void Watching_a_finished_item_again_un_finishes_it()
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: true, positionSeconds: 900, Movie).Should().BeFalse();
    }

    // A ping can land at 0 before the player has seeked to its resume point.
    // Clearing on that would wipe the state of an item nobody restarted.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(WatchStateTransition.RestartGraceSeconds - 1)]
    public void An_opening_ping_leaves_a_finished_item_alone(double position)
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: true, position, Movie).Should().BeTrue();
    }

    [Fact]
    public void Past_the_grace_window_a_re_watch_counts()
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: true, WatchStateTransition.RestartGraceSeconds, Movie)
            .Should().BeFalse();
    }

    [Fact]
    public void An_unwatched_item_part_way_through_stays_unwatched()
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: false, Movie * 0.4, Movie).Should().BeFalse();
    }

    // Finishing a re-watch marks it watched again, rather than leaving it
    // permanently in progress.
    [Fact]
    public void Finishing_a_re_watch_marks_it_watched_again()
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: false, Movie * 0.99, Movie).Should().BeTrue();
    }

    // Live TV and anything else with no known duration must not be reasoned
    // about at all — a fraction of an unknown length is meaningless.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unknown_duration_leaves_the_flag_untouched(bool wasPlayed)
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed, positionSeconds: 500, durationSeconds: 0)
            .Should().Be(wasPlayed);
    }

    [Fact]
    public void A_finished_item_has_nothing_to_resume()
    {
        WatchStateTransition.ResolveResumePosition(Movie * 0.95, Movie).Should().Be(0);
    }

    [Fact]
    public void An_item_in_progress_resumes_where_it_is()
    {
        WatchStateTransition.ResolveResumePosition(900, Movie).Should().Be(900);
    }

    // The two halves have to agree: an item the flag calls unwatched must have
    // somewhere to resume from, or the details page offers Resume with no
    // position and starts from the beginning.
    [Fact]
    public void A_re_watch_is_both_unwatched_and_resumable()
    {
        WatchStateTransition.ResolveIsPlayed(wasPlayed: true, 900, Movie).Should().BeFalse();
        WatchStateTransition.ResolveResumePosition(900, Movie).Should().Be(900);
    }

    [Theory]
    [InlineData(0.89, false)]
    [InlineData(0.90, true)]
    [InlineData(0.91, true)]
    public void Completion_is_measured_against_the_threshold(double fraction, bool expected)
    {
        WatchStateTransition.IsComplete(Movie * fraction, Movie).Should().Be(expected);
    }
}
