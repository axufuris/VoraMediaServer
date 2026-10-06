using Vora.Application.SmartLists;

namespace Vora.Application.Tests.SmartLists;

public class RecentlyAddedTvTests
{
    private static readonly DateTime ShowAdded = new(2026, 9, 27, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EpisodeAdded = new(2026, 10, 4, 23, 59, 0, DateTimeKind.Utc);

    private readonly Guid _show = Guid.NewGuid();
    private readonly Guid _seasonOne = Guid.NewGuid();
    private readonly Guid _seasonTwo = Guid.NewGuid();

    private TvCandidate Show(DateTime added, DateTime? latest) => new(_show, TvLevel.Show, _show, null, 0, 0, added, latest, false);

    private TvCandidate Season(Guid id, int number, DateTime added, DateTime? latest) => new(id, TvLevel.Season, _show, id, number, 0, added, latest, false);

    private TvCandidate Episode(Guid season, int seasonNumber, int number, DateTime added, bool played = false) =>
        new(Guid.NewGuid(), TvLevel.Episode, _show, season, seasonNumber, number, added, null, played);

    [Fact]
    public void An_existing_show_with_one_new_episode_appears_as_that_episode()
    {
        var episode = Episode(_seasonTwo, 2, 1, EpisodeAdded);

        var kept = RecentlyAddedTv.OnePerShow([
            Show(ShowAdded, EpisodeAdded.AddHours(2)),
            Season(_seasonTwo, 2, EpisodeAdded, EpisodeAdded.AddHours(2)),
            episode,
        ]);

        kept.Should().Equal(episode.Id);
    }

    [Fact]
    public void Older_episodes_further_down_the_list_do_not_count_as_new()
    {
        var episode = Episode(_seasonTwo, 2, 1, EpisodeAdded);

        var kept = RecentlyAddedTv.OnePerShow([
            Show(ShowAdded, EpisodeAdded.AddHours(2)),
            Season(_seasonTwo, 2, EpisodeAdded, EpisodeAdded.AddHours(2)),
            episode,
            Season(_seasonOne, 1, ShowAdded, ShowAdded),
            Episode(_seasonOne, 1, 1, ShowAdded),
        ]);

        kept.Should().Equal(episode.Id);
    }

    [Fact]
    public void A_show_added_with_its_episodes_appears_as_the_show()
    {
        var added = new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc);

        var kept = RecentlyAddedTv.OnePerShow([
            Show(added, added.AddHours(3)),
            Season(_seasonOne, 1, added, added.AddHours(3)),
            Episode(_seasonOne, 1, 1, added.AddHours(1)),
            Episode(_seasonOne, 1, 2, added.AddHours(3)),
        ]);

        kept.Should().Equal(_show);
    }

    [Fact]
    public void Several_new_episodes_of_one_season_appear_as_that_season()
    {
        var kept = RecentlyAddedTv.OnePerShow([
            Show(ShowAdded, EpisodeAdded),
            Season(_seasonTwo, 2, EpisodeAdded, EpisodeAdded),
            Episode(_seasonTwo, 2, 2, EpisodeAdded),
            Episode(_seasonTwo, 2, 1, EpisodeAdded.AddMinutes(-5)),
        ]);

        kept.Should().Equal(_seasonTwo);
    }

    [Fact]
    public void New_episodes_in_more_than_one_season_appear_as_the_show()
    {
        var kept = RecentlyAddedTv.OnePerShow([
            Show(ShowAdded, EpisodeAdded),
            Season(_seasonTwo, 2, EpisodeAdded, EpisodeAdded),
            Episode(_seasonTwo, 2, 1, EpisodeAdded),
            Season(_seasonOne, 1, ShowAdded, EpisodeAdded.AddMinutes(-5)),
            Episode(_seasonOne, 1, 13, EpisodeAdded.AddMinutes(-5)),
        ]);

        kept.Should().Equal(_show);
    }

    [Fact]
    public void A_list_without_episodes_shows_the_season_that_changed()
    {
        var kept = RecentlyAddedTv.OnePerShow([
            Show(ShowAdded, EpisodeAdded),
            Season(_seasonTwo, 2, EpisodeAdded, EpisodeAdded),
        ]);

        kept.Should().Equal(_seasonTwo);
    }

    [Fact]
    public void Several_new_episodes_without_their_season_in_the_list_show_the_first_unwatched()
    {
        var first = Episode(_seasonTwo, 2, 1, EpisodeAdded.AddMinutes(-5), played: true);
        var second = Episode(_seasonTwo, 2, 2, EpisodeAdded);

        var kept = RecentlyAddedTv.OnePerShow([second, first]);

        kept.Should().Equal(second.Id);
    }

    [Fact]
    public void Each_show_is_decided_on_its_own()
    {
        var otherShow = Guid.NewGuid();
        var other = new TvCandidate(otherShow, TvLevel.Show, otherShow, null, 0, 0, EpisodeAdded, EpisodeAdded, false);
        var episode = Episode(_seasonTwo, 2, 1, EpisodeAdded);

        var kept = RecentlyAddedTv.OnePerShow([Show(ShowAdded, EpisodeAdded), episode, other]);

        kept.Should().BeEquivalentTo(new[] { episode.Id, otherShow });
    }
}
