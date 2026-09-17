using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Tests.SmartPlaylists;

public class SmartPlaylistEvaluatorMovieTests
{
    private readonly SmartPlaylistEvaluatorFixture _fx = new();

    private SmartPlaylistDefinition Definition(SmartPlaylistRuleGroup root, SmartPlaylistSortBy sortBy = SmartPlaylistSortBy.Title) => new()
    {
        Root = root,
        SortBy = sortBy,
        SortDirection = SmartPlaylistSortDirection.Asc
    };

    private static SmartPlaylistRuleGroup AllOf(params SmartPlaylistRule[] rules) =>
        new() { Match = SmartPlaylistMatch.All, Rules = rules.ToList() };

    private static SmartPlaylistRuleGroup AnyOf(params SmartPlaylistRule[] rules) =>
        new() { Match = SmartPlaylistMatch.Any, Rules = rules.ToList() };

    private static SmartPlaylistRule Rule(SmartPlaylistField field, SmartPlaylistOperator op, string? value = null, string? secondValue = null) =>
        new() { Field = field, Operator = op, Value = value, SecondValue = secondValue };

    [Fact]
    public async Task Empty_rule_group_returns_all_movies()
    {
        _fx.AddMovie("Inception", 2010);
        _fx.AddMovie("The Matrix", 1999);

        var def = Definition(AllOf());
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Inception", "The Matrix" });
    }

    [Fact]
    public async Task Title_Equals_filters_to_exact_match()
    {
        _fx.AddMovie("Inception", 2010);
        _fx.AddMovie("The Matrix", 1999);

        var def = Definition(AllOf(Rule(SmartPlaylistField.Title, SmartPlaylistOperator.Equals, "Inception")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Inception");
    }

    [Fact]
    public async Task Title_Contains_is_case_insensitive_substring_match()
    {
        _fx.AddMovie("Inception", 2010);
        _fx.AddMovie("Interstellar", 2014);
        _fx.AddMovie("The Matrix", 1999);

        var def = Definition(AllOf(Rule(SmartPlaylistField.Title, SmartPlaylistOperator.Contains, "inter")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Interstellar" });
    }

    [Fact]
    public async Task ReleaseYear_GreaterThan_filters_year_above_threshold()
    {
        _fx.AddMovie("Old Movie", 1990);
        _fx.AddMovie("Newish", 2015);
        _fx.AddMovie("Recent", 2023);

        var def = Definition(AllOf(Rule(SmartPlaylistField.ReleaseYear, SmartPlaylistOperator.GreaterThan, "2000")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Newish", "Recent" });
    }

    [Fact]
    public async Task ReleaseYear_Between_filters_inclusive_range()
    {
        _fx.AddMovie("Too Old", 1990);
        _fx.AddMovie("In Range A", 2005);
        _fx.AddMovie("In Range B", 2010);
        _fx.AddMovie("Too New", 2023);

        var def = Definition(AllOf(Rule(SmartPlaylistField.ReleaseYear, SmartPlaylistOperator.Between, "2000", "2015")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "In Range A", "In Range B" });
    }

    [Fact]
    public async Task ContentRating_Equals_filters_by_rating()
    {
        _fx.AddMovie("Family", 2010, rating: "PG");
        _fx.AddMovie("Mature", 2015, rating: "R");

        var def = Definition(AllOf(Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "PG")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Family" });
    }

    [Fact]
    public async Task All_match_requires_every_rule_to_pass()
    {
        _fx.AddMovie("Inception 2010 PG", 2010, rating: "PG-13");
        _fx.AddMovie("Inception 2020 PG-13", 2020, rating: "PG-13");
        _fx.AddMovie("Other 2010", 2010, rating: "R");

        var def = Definition(AllOf(
            Rule(SmartPlaylistField.Title, SmartPlaylistOperator.Contains, "Inception"),
            Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "PG-13")));

        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[]
        {
            "Inception 2010 PG", "Inception 2020 PG-13"
        });
    }

    [Fact]
    public async Task Any_match_requires_any_rule_to_pass()
    {
        _fx.AddMovie("PG Movie", 2010, rating: "PG");
        _fx.AddMovie("R Movie", 2010, rating: "R");
        _fx.AddMovie("G Movie", 2010, rating: "G");

        var def = Definition(AnyOf(
            Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "PG"),
            Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "G")));

        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "PG Movie", "G Movie" });
    }

    [Fact]
    public async Task Limit_caps_returned_count()
    {
        for (int i = 0; i < 10; i++) _fx.AddMovie($"Movie {i:D2}", 2000 + i);

        var def = new SmartPlaylistDefinition
        {
            Root = AllOf(),
            SortBy = SmartPlaylistSortBy.Title,
            SortDirection = SmartPlaylistSortDirection.Asc,
            Limit = 3
        };

        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Sort_descending_reverses_order()
    {
        _fx.AddMovie("A", 2010);
        _fx.AddMovie("B", 2010);
        _fx.AddMovie("C", 2010);

        var def = new SmartPlaylistDefinition
        {
            Root = AllOf(),
            SortBy = SmartPlaylistSortBy.Title,
            SortDirection = SmartPlaylistSortDirection.Desc
        };

        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(m => m.Title).Should().ContainInOrder("C", "B", "A");
    }

    [Fact]
    public async Task CountAsync_returns_matching_row_count()
    {
        _fx.AddMovie("A", 2010, rating: "PG");
        _fx.AddMovie("B", 2010, rating: "PG");
        _fx.AddMovie("C", 2010, rating: "R");

        var def = Definition(AllOf(Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "PG")));

        var count = await _fx.Evaluator.CountAsync(def, PlaylistMediaType.Movies, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        count.Should().Be(2);
    }
}
