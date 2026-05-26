using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Tests.SmartPlaylists;

public class SmartPlaylistEvaluatorTvTests
{
    private readonly SmartPlaylistEvaluatorFixture _fx = new();

    private static SmartPlaylistDefinition Definition(SmartPlaylistRuleGroup root, SmartPlaylistSortBy sortBy = SmartPlaylistSortBy.Title) => new()
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
    public async Task Empty_rule_group_returns_all_episodes_across_shows()
    {
        var severance = _fx.AddShow("Severance");
        var s1 = _fx.AddSeason(severance, 1);
        _fx.AddEpisode(s1, "Good News About Hell", 1);
        _fx.AddEpisode(s1, "Half Loop", 2);

        var foundation = _fx.AddShow("Foundation");
        var f1 = _fx.AddSeason(foundation, 1);
        _fx.AddEpisode(f1, "The Emperor's Peace", 1);

        var results = await _fx.Evaluator.EvaluateAsync(
            Definition(AllOf()), PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Episode_title_contains_filters_within_show()
    {
        var show = _fx.AddShow("Severance");
        var season = _fx.AddSeason(show, 1);
        _fx.AddEpisode(season, "Good News About Hell", 1);
        _fx.AddEpisode(season, "Half Loop", 2);
        _fx.AddEpisode(season, "In Perpetuity", 3);

        var def = Definition(AllOf(Rule(SmartPlaylistField.Title, SmartPlaylistOperator.Contains, "good")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Good News About Hell");
    }

    [Fact]
    public async Task ShowTitle_equals_filters_to_episodes_of_one_show()
    {
        var sev = _fx.AddShow("Severance");
        var s1 = _fx.AddSeason(sev, 1);
        _fx.AddEpisode(s1, "Ep1", 1);
        _fx.AddEpisode(s1, "Ep2", 2);

        var fnd = _fx.AddShow("Foundation");
        var f1 = _fx.AddSeason(fnd, 1);
        _fx.AddEpisode(f1, "Ep1", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.ShowTitle, SmartPlaylistOperator.Equals, "Severance")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ShowTitle_contains_matches_partial_string()
    {
        var sev = _fx.AddShow("Severance");
        _fx.AddEpisode(_fx.AddSeason(sev, 1), "Ep1", 1);
        var fnd = _fx.AddShow("Foundation");
        _fx.AddEpisode(_fx.AddSeason(fnd, 1), "Ep1", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.ShowTitle, SmartPlaylistOperator.Contains, "found")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task SeasonNumber_equals_filters_to_one_season()
    {
        var sev = _fx.AddShow("Severance");
        _fx.AddEpisode(_fx.AddSeason(sev, 1), "S1E1", 1);
        var s2 = _fx.AddSeason(sev, 2);
        _fx.AddEpisode(s2, "S2E1", 1);
        _fx.AddEpisode(s2, "S2E2", 2);

        var def = Definition(AllOf(Rule(SmartPlaylistField.SeasonNumber, SmartPlaylistOperator.Equals, "2")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(2);
        results.All(r => r.Title.StartsWith("S2")).Should().BeTrue();
    }

    [Fact]
    public async Task SeasonNumber_greater_than_filters_correctly()
    {
        var sev = _fx.AddShow("Severance");
        _fx.AddEpisode(_fx.AddSeason(sev, 1), "S1E1", 1);
        _fx.AddEpisode(_fx.AddSeason(sev, 2), "S2E1", 1);
        _fx.AddEpisode(_fx.AddSeason(sev, 3), "S3E1", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.SeasonNumber, SmartPlaylistOperator.GreaterThan, "1")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(2);
        results.Should().NotContain(r => r.Title == "S1E1");
    }

    [Fact]
    public async Task EpisodeNumber_equals_filters_single_episode_per_season()
    {
        var sev = _fx.AddShow("Severance");
        var s1 = _fx.AddSeason(sev, 1);
        var s2 = _fx.AddSeason(sev, 2);
        _fx.AddEpisode(s1, "S1E1", 1);
        _fx.AddEpisode(s1, "S1E2", 2);
        _fx.AddEpisode(s2, "S2E1", 1);
        _fx.AddEpisode(s2, "S2E2", 2);

        var def = Definition(AllOf(Rule(SmartPlaylistField.EpisodeNumber, SmartPlaylistOperator.Equals, "1")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Combined_show_season_episode_filter_targets_one_episode()
    {
        var sev = _fx.AddShow("Severance");
        _fx.AddEpisode(_fx.AddSeason(sev, 1), "S1E1", 1);
        _fx.AddEpisode(_fx.AddSeason(sev, 2), "S2E5", 5);

        var def = Definition(AllOf(
            Rule(SmartPlaylistField.ShowTitle, SmartPlaylistOperator.Equals, "Severance"),
            Rule(SmartPlaylistField.SeasonNumber, SmartPlaylistOperator.Equals, "2"),
            Rule(SmartPlaylistField.EpisodeNumber, SmartPlaylistOperator.Equals, "5")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("S2E5");
    }

    [Fact]
    public async Task Year_filter_works_via_episode_release_date()
    {
        var show = _fx.AddShow("Severance");
        var season = _fx.AddSeason(show, 1);
        _fx.AddEpisode(season, "Old", 1, releaseYear: 2022);
        _fx.AddEpisode(season, "New", 2, releaseYear: 2026);

        var def = Definition(AllOf(Rule(SmartPlaylistField.Year, SmartPlaylistOperator.Equals, "2026")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("New");
    }

    [Fact]
    public async Task ContentRating_filter_skips_episodes_outside_allowlist()
    {
        var show = _fx.AddShow("Severance");
        var season = _fx.AddSeason(show, 1);
        _fx.AddEpisode(season, "PG", 1, contentRating: "TV-PG");
        _fx.AddEpisode(season, "MA", 2, contentRating: "TV-MA");

        var def = Definition(AllOf(Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "TV-MA")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("MA");
    }

    [Fact]
    public async Task Genre_filter_uses_show_genres_not_episode_genres()
    {
        var sciFi = _fx.AddShow("Severance", genres: new[] { "Sci-Fi", "Drama" });
        _fx.AddEpisode(_fx.AddSeason(sciFi, 1), "Severance Ep1", 1);

        var comedy = _fx.AddShow("Brooklyn 99", genres: new[] { "Comedy" });
        _fx.AddEpisode(_fx.AddSeason(comedy, 1), "Brooklyn Ep1", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.Genre, SmartPlaylistOperator.Equals, "sci-fi")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Severance Ep1");
    }

    [Fact]
    public async Task AnyOf_match_returns_episodes_satisfying_either_rule()
    {
        var sev = _fx.AddShow("Severance");
        var s1 = _fx.AddSeason(sev, 1);
        _fx.AddEpisode(s1, "S1E1", 1, releaseYear: 2022);
        _fx.AddEpisode(s1, "S1E2", 2, releaseYear: 2023);
        _fx.AddEpisode(_fx.AddSeason(sev, 99), "S99", 1, releaseYear: 1999);

        var def = Definition(AnyOf(
            Rule(SmartPlaylistField.Year, SmartPlaylistOperator.Equals, "2023"),
            Rule(SmartPlaylistField.SeasonNumber, SmartPlaylistOperator.Equals, "99")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Shows, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(2);
        results.Select(r => r.Title).Should().BeEquivalentTo(new[] { "S1E2", "S99" });
    }
}
