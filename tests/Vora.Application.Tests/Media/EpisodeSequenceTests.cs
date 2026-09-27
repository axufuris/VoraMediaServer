using Vora.Application.Media;

namespace Vora.Application.Tests.Media;

public class EpisodeSequenceTests
{
    private sealed record Ep(int Season, int Episode, bool InProgress = false);

    [Fact]
    public void Specials_are_dropped_when_regular_episodes_are_there()
    {
        var picked = EpisodeSequence.PreferRegular(new[] { new Ep(0, 1), new Ep(1, 1), new Ep(2, 3) }, e => e.Season);

        picked.Should().Equal(new Ep(1, 1), new Ep(2, 3));
    }

    [Fact]
    public void Specials_stay_when_they_are_all_there_is()
    {
        var picked = EpisodeSequence.PreferRegular(new[] { new Ep(0, 2), new Ep(0, 1) }, e => e.Season);

        picked.Should().HaveCount(2);
    }

    [Fact]
    public void Next_up_is_the_earliest_regular_episode()
    {
        var next = EpisodeSequence.PickNextUp(new[] { new Ep(0, 1), new Ep(2, 1), new Ep(1, 4) }, e => e.Season, e => e.Episode, e => e.InProgress, showHasRegularEpisodes: true);

        next.Should().Be(new Ep(1, 4));
    }

    [Fact]
    public void Only_unwatched_specials_left_means_nothing_is_next()
    {
        var next = EpisodeSequence.PickNextUp(new[] { new Ep(0, 1) }, e => e.Season, e => e.Episode, e => e.InProgress, showHasRegularEpisodes: true);

        next.Should().BeNull();
    }

    [Fact]
    public void A_part_watched_special_is_next()
    {
        var next = EpisodeSequence.PickNextUp(new[] { new Ep(1, 2), new Ep(0, 3, InProgress: true) }, e => e.Season, e => e.Episode, e => e.InProgress, showHasRegularEpisodes: true);

        next.Should().Be(new Ep(0, 3, true));
    }

    [Fact]
    public void A_specials_only_show_offers_its_first_special()
    {
        var next = EpisodeSequence.PickNextUp(new[] { new Ep(0, 2), new Ep(0, 1) }, e => e.Season, e => e.Episode, e => e.InProgress, showHasRegularEpisodes: false);

        next.Should().Be(new Ep(0, 1));
    }
}
