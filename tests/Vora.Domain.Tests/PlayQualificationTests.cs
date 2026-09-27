using Vora.Domain.Entities.Media;

namespace Vora.Domain.Tests;

// The rule this replaces counted a listen after thirty seconds OR half the
// track, whichever came first — so on a two-minute song the thirty-second arm
// fired at a quarter of the way through, and nothing downstream distinguished
// that from playing the track out.
public class PlayQualificationTests
{
    private const int TwoMinutes = 120;
    private const int TwentyMinutes = 1200;

    [Theory]
    // The case that prompted this: a quarter of a short track is not a play.
    [InlineData(30, TwoMinutes, false)]
    [InlineData(59, TwoMinutes, false)]
    [InlineData(60, TwoMinutes, true)]
    // A long track does not have to run to its own halfway point.
    [InlineData(239, TwentyMinutes, false)]
    [InlineData(240, TwentyMinutes, true)]
    // A short track qualifies proportionally, well under the absolute threshold.
    [InlineData(15, 30, true)]
    [InlineData(14, 30, false)]
    public void A_listen_counts_only_once_it_is_deep_enough(int listened, int? trackDuration, bool expected) =>
        PlayQualification.Qualifies(listened, trackDuration).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Nothing_heard_is_never_a_play(int listened) =>
        PlayQualification.Qualifies(listened, TwoMinutes).Should().BeFalse();

    // Without a scanned duration there is no fraction to take, and the strict
    // four-minute rule would mean such tracks could never register at all.
    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void A_track_of_unknown_length_falls_back_to_an_absolute_threshold(int listened, bool expected)
    {
        PlayQualification.Qualifies(listened, null).Should().Be(expected);
        PlayQualification.Qualifies(listened, 0).Should().Be(expected);
    }

    [Fact]
    public void Playing_a_track_out_is_worth_the_most()
    {
        PlayQualification.Weight(TwoMinutes, TwoMinutes, completed: true).Should().Be(1.0);
        // Reported complete but with a short position: the flag is the stronger
        // signal, since a player can stop reporting position before the end.
        PlayQualification.Weight(5, TwoMinutes, completed: true).Should().Be(1.0);
    }

    [Theory]
    [InlineData(120, 1.0)]
    [InlineData(115, 1.0)]  // within the last 5%, treated as played out
    [InlineData(100, 0.8)]
    [InlineData(90, 0.8)]
    [InlineData(70, 0.5)]
    [InlineData(60, 0.5)]
    [InlineData(45, 0.25)]
    public void Weight_tracks_how_much_of_it_was_heard(int listened, double expected) =>
        PlayQualification.Weight(listened, TwoMinutes, completed: false).Should().Be(expected);

    // Absence of evidence, not evidence of a shallow listen. Rows written before
    // the player recorded real depth land here, and must not be penalised for it.
    [Theory]
    [InlineData(60, null)]
    [InlineData(60, 0)]
    [InlineData(0, TwoMinutes)]
    public void An_unmeasurable_listen_sits_in_the_middle(int listened, int? trackDuration) =>
        PlayQualification.Weight(listened, trackDuration, completed: false)
            .Should().Be(PlayQualification.NeutralWeight);

    // The gate and the weight have to agree at the boundary: something that only
    // just counts as a play must not also be the lowest-weighted thing there is
    // by accident of a different threshold.
    [Fact]
    public void The_weakest_qualifying_listen_still_outscores_nothing()
    {
        PlayQualification.Qualifies(60, TwoMinutes).Should().BeTrue();
        PlayQualification.Weight(60, TwoMinutes, completed: false).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Weight_never_leaves_the_zero_to_one_range()
    {
        foreach (var listened in new[] { 0, 1, 30, 60, 119, 120, 5000 })
        {
            foreach (var completed in new[] { true, false })
            {
                var weight = PlayQualification.Weight(listened, TwoMinutes, completed);
                weight.Should().BeInRange(0, 1);
            }
        }
    }
}
