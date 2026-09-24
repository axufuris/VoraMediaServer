using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Tests.SmartPlaylists;

// Smart playlists used one access filter for every kind of item — a music one —
// so a playlist of films was tested against the MUSIC allowlist ("Clean",
// "Explicit") instead of the film allowlist. That only failed safe by accident,
// and making music follow its own allowlist would have turned the accident into
// R-rated films in a child's smart playlist. These pin each kind to its own
// rule, and pin films and episodes to exactly what browsing shows.
public class SmartPlaylistParentalControlTests
{
    private readonly SmartPlaylistEvaluatorFixture _fx = new();

    private static SmartPlaylistDefinition Everything() => new()
    {
        Root = new SmartPlaylistRuleGroup { Match = SmartPlaylistMatch.All, Rules = new List<SmartPlaylistRule>() },
        SortBy = SmartPlaylistSortBy.Title,
        SortDirection = SmartPlaylistSortDirection.Asc
    };

    // A child restricted to G and PG films, with music left open. The exact case
    // that would have regressed.
    private static PlaylistAccessFilter FamilyFilmsOnly => new()
    {
        AllowedMovieRatings = new List<string> { "G", "PG" },
    };

    [Fact]
    public async Task A_child_restricted_to_family_films_never_gets_an_r_rated_one()
    {
        _fx.AddMovie("Toy Story", 1995, rating: "G");
        _fx.AddMovie("Up", 2009, rating: "PG");
        _fx.AddMovie("Alien", 1979, rating: "R");

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Movies, _fx.ProfileId, FamilyFilmsOnly);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Toy Story", "Up" });
    }

    // Films are filtered against the film allowlist, not the music one — a
    // "Clean"-only music restriction must not reach into the film list at all.
    [Fact]
    public async Task A_music_restriction_does_not_filter_films()
    {
        _fx.AddMovie("Up", 2009, rating: "PG");
        _fx.AddMovie("Alien", 1979, rating: "R");
        var cleanMusicOnly = new PlaylistAccessFilter { AllowedMusicRatings = new List<string> { "Clean" } };

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Movies, _fx.ProfileId, cleanMusicOnly);

        results.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Up", "Alien" });
    }

    // Most episodes carry no rating of their own. The old check tested only the
    // episode, so every episode of a TV-MA show passed; the browse rule falls
    // back to the season and then the show.
    [Fact]
    public async Task An_unrated_episode_of_a_mature_show_inherits_the_shows_rating()
    {
        var mature = _fx.AddShow("Mature Show");
        mature.ContentRating = "TV-MA";
        var season = _fx.AddSeason(mature, 1);
        _fx.AddEpisode(season, "Pilot", 1, contentRating: null);

        var kids = _fx.AddShow("Kids Show");
        kids.ContentRating = "TV-Y";
        _fx.AddEpisode(_fx.AddSeason(kids, 1), "Episode One", 1, contentRating: null);
        _fx.Db.SaveChanges();

        var tvYOnly = new PlaylistAccessFilter { AllowedTvRatings = new List<string> { "TV-Y" } };

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Shows, _fx.ProfileId, tvYOnly);

        results.Select(e => e.Title).Should().Equal("Episode One");
    }

    [Fact]
    public async Task A_film_restriction_does_not_filter_tv()
    {
        var mature = _fx.AddShow("Mature Show");
        mature.ContentRating = "TV-MA";
        _fx.AddEpisode(_fx.AddSeason(mature, 1), "Pilot", 1, contentRating: null);
        _fx.Db.SaveChanges();

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Shows, _fx.ProfileId, FamilyFilmsOnly);

        results.Select(e => e.Title).Should().Equal("Pilot");
    }

    [Fact]
    public async Task Blocking_unrated_applies_to_films_with_no_allowlist()
    {
        _fx.AddMovie("Alien", 1979, rating: "R");
        _fx.AddMovie("Home Video", 2020, rating: null);
        var blockUnratedOnly = new PlaylistAccessFilter { BlockUnratedContent = true };

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Movies, _fx.ProfileId, blockUnratedOnly);

        results.Select(m => m.Title).Should().Equal("Alien");
    }

    [Fact]
    public async Task A_clean_only_profile_gets_no_explicit_track()
    {
        var album = _fx.AddAlbum(_fx.AddArtist("Eminem"), "The Eminem Show");
        _fx.AddTrack(album, "Without Me", 1, contentRating: "Explicit");
        _fx.AddTrack(album, "Without Me (Clean)", 2, contentRating: "Clean");
        var cleanOnly = new PlaylistAccessFilter { AllowedMusicRatings = new List<string> { "Clean" } };

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Music, _fx.ProfileId, cleanOnly);

        results.Select(t => t.Title).Should().Equal("Without Me (Clean)");
    }

    // A film restriction must not strip the music list down to untagged tracks —
    // the mirror of the bug fixed on the music endpoints.
    [Fact]
    public async Task A_film_restriction_does_not_filter_music()
    {
        var album = _fx.AddAlbum(_fx.AddArtist("Eminem"), "The Eminem Show");
        _fx.AddTrack(album, "Without Me", 1, contentRating: "Explicit");
        _fx.AddTrack(album, "Without Me (Clean)", 2, contentRating: "Clean");

        var results = await _fx.Evaluator.EvaluateAsync(Everything(), PlaylistMediaType.Music, _fx.ProfileId, FamilyFilmsOnly);

        results.Should().HaveCount(2);
    }
}
