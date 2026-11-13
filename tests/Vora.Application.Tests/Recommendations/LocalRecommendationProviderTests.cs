using Vora.Application.Recommendations;
using Vora.Application.Recommendations.Providers;

namespace Vora.Application.Tests.Recommendations;

// The For You page had three rows however much was watched: one per top genre,
// taken from every library at once and blind to ratings.
public class LocalRecommendationProviderTests
{
    private readonly IRecommendationRepository _repository = Substitute.For<IRecommendationRepository>();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly Guid _library = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
    private readonly List<WatchedTitle> _watched = new();
    private readonly List<TitleGenre> _genres = new();
    private readonly Dictionary<Guid, List<Guid>> _similar = new();
    private readonly Dictionary<int, List<Guid>> _byGenre = new();
    private List<Guid> _topRated = new();

    public LocalRecommendationProviderTests()
    {
        _repository.GetWatchedTitlesAsync(_profile, Arg.Any<Guid?>()).Returns(_ => _watched.ToList());
        _repository.GetGenresAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(_ => _genres.ToList());
        _repository.GetSimilarUnwatchedAsync(Arg.Any<Guid>(), _profile, Arg.Any<Guid?>(), Arg.Any<int>())
            .Returns(call => _similar.TryGetValue(call.ArgAt<Guid>(0), out var ids) ? ids : new List<Guid>());
        _repository.GetTopUnwatchedByGenreAsync(Arg.Any<int>(), _profile, Arg.Any<Guid?>(), Arg.Any<int>())
            .Returns(call => _byGenre.TryGetValue(call.ArgAt<int>(0), out var ids) ? ids : new List<Guid>());
        _repository.GetTopUnwatchedAsync(_profile, Arg.Any<Guid?>(), Arg.Any<int>()).Returns(_ => _topRated);
    }

    private WatchedTitle Watched(string title, int hoursAgo, decimal? rating = null, params (int Id, string Name)[] genres)
    {
        var t = new WatchedTitle(Guid.NewGuid(), title, 2020, _now.AddHours(-hoursAgo), rating, rating.HasValue ? _now.AddHours(-hoursAgo) : null);
        _watched.Add(t);
        _genres.AddRange(genres.Select(g => new TitleGenre(t.Id, g.Id, g.Name)));
        return t;
    }

    private static List<Guid> Titles(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    private async Task<List<Vora.Plugins.Dtos.RecommendationListDto>> Rows() =>
        (await new LocalRecommendationProvider(_repository).GetRecommendationsAsync(_profile, _library, TestContext.Current.CancellationToken)).ToList();

    [Fact]
    public async Task Nothing_watched_or_rated_is_no_rows()
    {
        (await Rows()).Should().BeEmpty();
        await _repository.DidNotReceive().GetTopUnwatchedAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>());
    }

    [Fact]
    public async Task A_full_history_gives_rows_of_every_kind_mixed_together_in_page_order()
    {
        var comedy = (35, "Comedy");
        var drama = (18, "Drama");
        var heat = Watched("Heat", 1, null, drama);
        var office = Watched("The Office", 2, 9, comedy);
        var arrival = Watched("Arrival", 3, null, drama);
        var alien = Watched("Alien", 400, 10, drama);
        _similar[heat.Id] = Titles(15);
        _similar[office.Id] = Titles(15);
        _similar[arrival.Id] = Titles(15);
        _similar[alien.Id] = Titles(15);
        _byGenre[35] = Titles(20);
        _byGenre[18] = Titles(20);
        _topRated = Titles(20);

        var rows = await Rows();

        rows.Select(r => r.Title).Should().Equal(
            "Because you watched Heat",
            "Because you like Drama",
            "Because you rated Alien highly",
            "Because you watched The Office",
            "Because you like Comedy",
            "Because you watched Arrival",
            "Top rated you haven't seen");
        rows.Select(r => r.Weight).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        rows.Should().OnlyContain(r => r.LocalItemIds.Count == LocalRecommendationProvider.ItemsPerRow);
        rows.Should().OnlyContain(r => !r.IsAi);
        await _repository.Received().GetWatchedTitlesAsync(_profile, _library);
    }

    [Fact]
    public async Task No_title_is_in_two_rows_and_a_row_left_with_too_few_is_dropped()
    {
        var comedy = (35, "Comedy");
        var heat = Watched("Heat", 1, null, comedy);
        var shared = Titles(15);
        _similar[heat.Id] = shared;
        _byGenre[35] = shared.Take(14).Concat(Titles(2)).ToList();
        _topRated = shared.Concat(Titles(6)).ToList();

        var rows = await Rows();

        rows.Select(r => r.Title).Should().Equal("Because you watched Heat", "Top rated you haven't seen");
        rows[1].LocalItemIds.Should().HaveCount(6).And.NotIntersectWith(rows[0].LocalItemIds);
    }

    // Without embeddings no title has neighbours. The genre and top rated rows
    // still come, and the provider stops asking after a few misses.
    [Fact]
    public async Task Without_embeddings_the_genre_and_top_rated_rows_still_come()
    {
        for (var i = 0; i < 10; i++) Watched($"Title {i}", i, null, (35, "Comedy"));
        _byGenre[35] = Titles(20);
        _topRated = Titles(20);

        var rows = await Rows();

        rows.Select(r => r.Title).Should().Equal("Because you like Comedy", "Top rated you haven't seen");
        await _repository.Received(3).GetSimilarUnwatchedAsync(Arg.Any<Guid>(), _profile, Arg.Any<Guid?>(), Arg.Any<int>());
    }

    [Fact]
    public async Task A_title_rated_low_seeds_nothing_and_a_loved_one_is_not_used_twice()
    {
        var hated = Watched("Cats", 1, 2);
        var loved = Watched("Alien", 2, 10);
        _similar[hated.Id] = Titles(15);
        _similar[loved.Id] = Titles(15);

        var rows = await Rows();

        rows.Select(r => r.Title).Should().Equal("Because you watched Alien");
        await _repository.DidNotReceive().GetSimilarUnwatchedAsync(hated.Id, Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>());
    }

    [Fact]
    public void A_genre_counts_double_for_a_loved_title_and_not_at_all_for_a_disliked_one()
    {
        var watched = new List<WatchedTitle>
        {
            new(Guid.NewGuid(), "A", null, _now, 9, _now),
            new(Guid.NewGuid(), "B", null, _now, null, null),
            new(Guid.NewGuid(), "C", null, _now, null, null),
            new(Guid.NewGuid(), "D", null, _now, 3, _now),
            new(Guid.NewGuid(), "E", null, _now, 2, _now),
        };
        var genres = new List<TitleGenre>
        {
            new(watched[0].Id, 1, "Horror"),
            new(watched[1].Id, 2, "Comedy"),
            new(watched[2].Id, 2, "Comedy"),
            new(watched[3].Id, 3, "Western"),
            new(watched[4].Id, 3, "Western"),
            new(watched[1].Id, 4, "Drama"),
        };

        LocalRecommendationProvider.TopGenres(watched, genres, 3).Should().Equal((2, "Comedy"), (1, "Horror"), (4, "Drama"));
    }
}
