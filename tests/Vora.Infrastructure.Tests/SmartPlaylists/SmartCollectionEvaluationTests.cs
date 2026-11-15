using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Tests.SmartPlaylists;

// A smart collection holds whole titles: films and shows, never a show's
// episodes. Shows match on the show's own year, genres and ratings.
public class SmartCollectionEvaluationTests
{
    private readonly SmartPlaylistEvaluatorFixture _fx = new();

    private static SmartPlaylistDefinition Rules(params SmartPlaylistRule[] rules) => new()
    {
        Root = new SmartPlaylistRuleGroup { Match = SmartPlaylistMatch.All, Rules = rules.ToList() },
        SortBy = SmartPlaylistSortBy.Title,
        SortDirection = SmartPlaylistSortDirection.Asc
    };

    private static SmartPlaylistRule Rule(SmartPlaylistField field, SmartPlaylistOperator op, string? value = null, string? secondValue = null) =>
        new() { Field = field, Operator = op, Value = value, SecondValue = secondValue };

    private TvShow Show(string title, int? firstAired = null, string? rating = null, IEnumerable<string>? genres = null, int episodes = 2)
    {
        var show = _fx.AddShow(title, genres);
        show.ReleaseDate = firstAired.HasValue ? new DateOnly(firstAired.Value, 1, 1) : null;
        show.ContentRating = rating;
        _fx.Db.SaveChanges();

        var season = _fx.AddSeason(show, 1);
        for (var i = 1; i <= episodes; i++)
        {
            _fx.AddEpisode(season, $"{title} episode {i}", i, firstAired);
        }
        return show;
    }

    [Fact]
    public async Task Shows_returns_the_shows_themselves_not_their_episodes()
    {
        var severance = Show("Severance", 2022, genres: new[] { "Drama" }, episodes: 3);
        _fx.AddMovie("Arrival", 2016, genres: new[] { "Drama" });

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(
            Rules(Rule(SmartPlaylistField.Genre, SmartPlaylistOperator.Contains, "drama")), PlaylistMediaType.Shows, PlaylistAccessFilter.Unrestricted);

        ids.Should().Equal(severance.Id);
    }

    [Fact]
    public async Task Movies_returns_only_films()
    {
        Show("Severance", 2022, genres: new[] { "Drama" });
        var arrival = _fx.AddMovie("Arrival", 2016, genres: new[] { "Drama" });

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(
            Rules(Rule(SmartPlaylistField.Genre, SmartPlaylistOperator.Contains, "drama")), PlaylistMediaType.Movies, PlaylistAccessFilter.Unrestricted);

        ids.Should().Equal(arrival.Id);
    }

    [Fact]
    public async Task Mixed_returns_films_and_shows_that_match()
    {
        var severance = Show("Severance", 2022, genres: new[] { "Drama" });
        Show("Taskmaster", 2015, genres: new[] { "Comedy" });
        var arrival = _fx.AddMovie("Arrival", 2016, genres: new[] { "Drama" });
        _fx.AddMovie("Airplane!", 1980, genres: new[] { "Comedy" });

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(
            Rules(Rule(SmartPlaylistField.Genre, SmartPlaylistOperator.Contains, "drama")), PlaylistMediaType.Mixed, PlaylistAccessFilter.Unrestricted);

        ids.Should().BeEquivalentTo(new[] { severance.Id, arrival.Id });
    }

    [Fact]
    public async Task A_show_matches_on_its_first_air_year()
    {
        var lost = Show("Lost", 2004);
        Show("Severance", 2022);

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(
            Rules(Rule(SmartPlaylistField.ReleaseYear, SmartPlaylistOperator.Between, "2000", "2009")), PlaylistMediaType.Shows, PlaylistAccessFilter.Unrestricted);

        ids.Should().Equal(lost.Id);
    }

    [Fact]
    public async Task A_show_matches_on_its_own_content_rating()
    {
        var bluey = Show("Bluey", 2018, rating: "TV-Y");
        Show("Severance", 2022, rating: "TV-MA");

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(
            Rules(Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "TV-Y")), PlaylistMediaType.Shows, PlaylistAccessFilter.Unrestricted);

        ids.Should().Equal(bluey.Id);
    }

    [Fact]
    public async Task A_library_collection_only_takes_titles_from_its_library()
    {
        Show("Severance", 2022);
        var otherLibrary = new PlaylistAccessFilter { HasAllLibraryAccess = false, AllowedLibraryIds = new List<Guid> { Guid.NewGuid() } };

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(Rules(), PlaylistMediaType.Shows, otherLibrary);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task Music_is_never_collection_material()
    {
        var album = _fx.AddAlbum(_fx.AddArtist("Bonobo"), "Migration");
        _fx.AddTrack(album, "Kerala", 1);

        var ids = await _fx.Evaluator.EvaluateCollectionIdsAsync(Rules(), PlaylistMediaType.Music, PlaylistAccessFilter.Unrestricted);

        ids.Should().BeEmpty();
    }
}
