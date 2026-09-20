using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Xunit;

namespace Vora.Infrastructure.Tests;

// A season carries no genres, companies or cast of its own — ProcessTvSeasonsAsync
// never writes them — and an episode only gets them when the provider returned
// episode-level credits. Both inherit from the show so the credits block isn't
// blank on those pages.
public class MediaDetailsInheritedFactsTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("inherited-facts-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Tree(TvShow Show, Season Season, Episode Episode);

    private static Tree SeedShow(VoraDbContext db, bool showHasFacts = true)
    {
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Shows",
            Type = LibraryType.TvShow,
            FolderPaths = new List<string> { "/media/shows" },
        });

        var show = new TvShow { Id = Guid.NewGuid(), Title = "House of the Dragon", LibraryId = libraryId };

        if (showHasFacts)
        {
            var genre = new Genre { Id = 18, Name = "Drama" };
            var company = new Company { Id = 3268, Name = "HBO" };
            db.Set<Genre>().Add(genre);
            db.Set<Company>().Add(company);
            show.Genres.Add(genre);
            show.ProductionCompanies.Add(company);

            var director = new Actor { Id = Guid.NewGuid(), Name = "Greg Yaitanes", TmdbId = 1 };
            db.Set<Actor>().Add(director);
            show.Cast.Add(new MediaCastMember { ActorId = director.Id, Actor = director, Roles = MediaCastRole.Director, Order = 0 });
        }

        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, LibraryId = libraryId, TvShow = show, TvShowId = show.Id };
        var episode = new Episode { Id = Guid.NewGuid(), Title = "We Light the Way", EpisodeNumber = 5, LibraryId = libraryId, Season = season, SeasonId = season.Id };

        show.Seasons.Add(season);
        season.Episodes.Add(episode);

        db.Set<TvShow>().Add(show);
        db.Set<Season>().Add(season);
        db.Set<Episode>().Add(episode);
        db.SaveChanges();

        return new Tree(show, season, episode);
    }

    private static MediaDetailsVM Project(VoraDbContext db, Guid id) =>
        db.MediaItems.Where(m => m.Id == id).Select(MediaDetailsVM.Projection).Single();

    [Fact]
    public void Episode_inherits_genres_studios_and_directors_from_the_show()
    {
        using var db = NewContext();
        var tree = SeedShow(db);

        var vm = Project(db, tree.Episode.Id);

        vm.Genres.Should().ContainSingle().Which.Should().Be("Drama");
        vm.Studios.Should().ContainSingle().Which.Should().Be("HBO");
        vm.Directors.Should().ContainSingle().Which.Should().Be("Greg Yaitanes");
    }

    [Fact]
    public void Season_inherits_genres_studios_and_directors_from_the_show()
    {
        using var db = NewContext();
        var tree = SeedShow(db);

        var vm = Project(db, tree.Season.Id);

        vm.Genres.Should().ContainSingle().Which.Should().Be("Drama");
        vm.Studios.Should().ContainSingle().Which.Should().Be("HBO");
        vm.Directors.Should().ContainSingle().Which.Should().Be("Greg Yaitanes");
    }

    [Fact]
    public void Season_inherits_the_shows_cast()
    {
        using var db = NewContext();
        var tree = SeedShow(db);

        Project(db, tree.Season.Id).Cast.Should().ContainSingle().Which.Name.Should().Be("Greg Yaitanes");
    }

    // The episode's own credits win — inheriting is a fallback, not an override.
    [Fact]
    public void An_episodes_own_facts_take_precedence_over_the_shows()
    {
        using var db = NewContext();
        var tree = SeedShow(db);

        var episodeGenre = new Genre { Id = 10759, Name = "Action & Adventure" };
        var episodeCompany = new Company { Id = 999, Name = "Second Unit Pictures" };
        var episodeDirector = new Actor { Id = Guid.NewGuid(), Name = "Clare Kilner", TmdbId = 2 };
        db.Set<Genre>().Add(episodeGenre);
        db.Set<Company>().Add(episodeCompany);
        db.Set<Actor>().Add(episodeDirector);

        var episode = db.Set<Episode>().Include(e => e.Genres).Include(e => e.ProductionCompanies).Include(e => e.Cast).Single(e => e.Id == tree.Episode.Id);
        episode.Genres.Add(episodeGenre);
        episode.ProductionCompanies.Add(episodeCompany);
        episode.Cast.Add(new MediaCastMember { ActorId = episodeDirector.Id, Actor = episodeDirector, Roles = MediaCastRole.Director, Order = 0 });
        db.SaveChanges();

        var vm = Project(db, tree.Episode.Id);

        vm.Genres.Should().ContainSingle().Which.Should().Be("Action & Adventure");
        vm.Studios.Should().ContainSingle().Which.Should().Be("Second Unit Pictures");
        vm.Directors.Should().ContainSingle().Which.Should().Be("Clare Kilner");
    }

    // The case that motivated splitting Directors out of Cast: the episode has a
    // guest cast but no directing credit, so the director inherits while the cast
    // row keeps showing the guest actors.
    [Fact]
    public void An_episode_with_guest_cast_but_no_director_still_inherits_the_director()
    {
        using var db = NewContext();
        var tree = SeedShow(db);

        var guest = new Actor { Id = Guid.NewGuid(), Name = "Guest Star", TmdbId = 3 };
        db.Set<Actor>().Add(guest);
        var episode = db.Set<Episode>().Include(e => e.Cast).Single(e => e.Id == tree.Episode.Id);
        episode.Cast.Add(new MediaCastMember { ActorId = guest.Id, Actor = guest, Roles = MediaCastRole.Actor, Order = 0 });
        db.SaveChanges();

        var vm = Project(db, tree.Episode.Id);

        vm.Directors.Should().ContainSingle().Which.Should().Be("Greg Yaitanes");
        vm.Cast.Should().ContainSingle().Which.Name.Should().Be("Guest Star");
    }

    [Fact]
    public void Nothing_is_invented_when_the_show_has_no_facts_either()
    {
        using var db = NewContext();
        var tree = SeedShow(db, showHasFacts: false);

        var vm = Project(db, tree.Episode.Id);

        vm.Genres.Should().BeEmpty();
        vm.Studios.Should().BeEmpty();
        vm.Directors.Should().BeEmpty();
    }

    [Fact]
    public void A_movie_is_unaffected_by_the_fallback()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
        });

        var genre = new Genre { Id = 28, Name = "Action" };
        db.Set<Genre>().Add(genre);
        var movie = new Movie { Id = Guid.NewGuid(), Title = "2 Fast 2 Furious", LibraryId = libraryId };
        movie.Genres.Add(genre);
        db.Set<Movie>().Add(movie);
        db.SaveChanges();

        var vm = Project(db, movie.Id);

        vm.Genres.Should().ContainSingle().Which.Should().Be("Action");
        vm.Studios.Should().BeEmpty();
        vm.Directors.Should().BeEmpty();
    }
}
