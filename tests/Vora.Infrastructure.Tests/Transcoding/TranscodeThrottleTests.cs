using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class TranscodeThrottleTests
{
    private static readonly SegmentPlan Movie = SegmentPlan.Uniform(2 * 60 * 60);

    [Fact]
    public void Pauses_once_the_encode_is_a_buffer_ahead_of_what_the_player_has_loaded()
    {
        TranscodeThrottle.Decide(60, canPause: true, paused: false, Movie, watchedSegment: 10, leadingEdge: 24).Should().Be(ThrottleStep.Pause);
    }

    [Fact]
    public void Keeps_encoding_while_it_is_less_than_a_buffer_ahead()
    {
        TranscodeThrottle.Decide(60, canPause: true, paused: false, Movie, watchedSegment: 10, leadingEdge: 23).Should().Be(ThrottleStep.Keep);
    }

    [Fact]
    public void Stays_paused_until_the_player_is_halfway_back_rather_than_flapping_at_the_line()
    {
        TranscodeThrottle.Decide(60, canPause: true, paused: true, Movie, watchedSegment: 14, leadingEdge: 24).Should().Be(ThrottleStep.Keep);
        TranscodeThrottle.Decide(60, canPause: true, paused: true, Movie, watchedSegment: 18, leadingEdge: 24).Should().Be(ThrottleStep.Resume);
    }

    [Fact]
    public void Never_pauses_closer_than_30_seconds_ahead_whatever_the_setting()
    {
        TranscodeThrottle.Decide(10, canPause: true, paused: false, Movie, watchedSegment: 10, leadingEdge: 13).Should().Be(ThrottleStep.Keep);
        TranscodeThrottle.Decide(10, canPause: true, paused: false, Movie, watchedSegment: 10, leadingEdge: 17).Should().Be(ThrottleStep.Pause);
    }

    [Fact]
    public void Turned_off_or_unable_to_pause_resumes_anything_paused_and_pauses_nothing()
    {
        TranscodeThrottle.Decide(0, canPause: true, paused: true, Movie, watchedSegment: 10, leadingEdge: 100).Should().Be(ThrottleStep.Resume);
        TranscodeThrottle.Decide(60, canPause: false, paused: false, Movie, watchedSegment: 10, leadingEdge: 100).Should().Be(ThrottleStep.Keep);
    }

    [Fact]
    public void Waits_for_the_player_to_load_something_before_judging()
    {
        TranscodeThrottle.Decide(60, canPause: true, paused: false, Movie, watchedSegment: -1, leadingEdge: 100).Should().Be(ThrottleStep.Keep);
    }

    [Fact]
    public void A_player_that_seeked_past_the_encode_resumes_it()
    {
        TranscodeThrottle.Decide(60, canPause: true, paused: true, Movie, watchedSegment: 40, leadingEdge: 24).Should().Be(ThrottleStep.Resume);
    }

    [Fact]
    public void Measures_up_to_the_end_of_the_last_written_segment()
    {
        TranscodeThrottle.SecondsAhead(Movie, watchedSegment: 10, leadingEdge: 24).Should().Be(60);
        TranscodeThrottle.SecondsAhead(Movie, watchedSegment: 10, leadingEdge: 5000).Should().Be(Movie.StartOf(Movie.Count - 1) + Movie.DurationOf(Movie.Count - 1) - Movie.StartOf(10));
    }
}
