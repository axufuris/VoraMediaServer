using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// What For You knows about a profile's viewing. Shows used to count as unwatched
// until the show itself was marked played, which almost never happens, so shows
// watched to the last episode were recommended as new.
public class RecommendationRepositoryTests
{
    private readonly Guid _movies = Guid.NewGuid();
    private readonly Guid _shows = Guid.NewGuid();
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _someoneElse = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
    private readonly VoraDbContext _db;
    private readonly Genre _comedy = new() { Id = 35, Name = "Comedy" };
    private readonly Genre _drama = new() { Id = 18, Name = "Drama" };

    public RecommendationRepositoryTests()
    {
        _db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("for-you-" + Guid.NewGuid().ToString("N"))
            .Options);
        _db.Set<MediaLibrary>().AddRange(
            new MediaLibrary { Id = _movies, Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } },
            new MediaLibrary { Id = _shows, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });
        _db.Set<Genre>().AddRange(_comedy, _drama);
        _db.SaveChanges();
    }

    private RecommendationRepository Repository() => new(_db);

    private Movie Movie(string title, decimal? rating = null, Genre? genre = null)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, LibraryId = _movies, ThirdPartyRating1 = rating, ReleaseDate = new DateOnly(2001, 1, 1) };
        if (genre != null) movie.Genres.Add(genre);
        _db.Add(movie);
        return movie;
    }

    private (TvShow Show, List<Episode> Episodes) Show(string title, decimal? rating = null, int episodes = 2)
    {
        var show = new TvShow { Id = Guid.NewGuid(), Title = title, LibraryId = _shows, ThirdPartyRating1 = rating };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", TvShowId = show.Id, SeasonNumber = 1, LibraryId = _shows };
        var eps = Enumerable.Range(1, episodes)
            .Select(n => new Episode { Id = Guid.NewGuid(), Title = $"Episode {n}", SeasonId = season.Id, EpisodeNumber = n, LibraryId = _shows })
            .ToList();
        _db.AddRange(show, season);
        _db.AddRange(eps);
        return (show, eps);
    }

    private void Played(Guid itemId, int hoursAgo, Guid? profile = null, double resume = 0, bool played = true) =>
        _db.UserMediaStates.Add(new UserMediaState { ProfileId = profile ?? _me, MediaItemId = itemId, IsPlayed = played, ResumePositionSeconds = resume, LastPlayedAt = _now.AddHours(-hoursAgo) });

    private void Rated(Guid itemId, decimal rating) =>
        _db.UserMediaRatings.Add(new UserMediaRating { ProfileId = _me, MediaItemId = itemId, Rating = rating, RatedAt = _now });

    [Fact]
    public async Task A_show_counts_once_as_watched_when_its_episodes_were_and_a_rated_title_counts_too()
    {
        var (office, eps) = Show("The Office");
        var heat = Movie("Heat");
        var alien = Movie("Alien");
        Played(eps[0].Id, 30);
        Played(eps[1].Id, 2);
        Played(heat.Id, 10);
        Rated(heat.Id, 9);
        Rated(alien.Id, 10);
        Played(Movie("Someone else's").Id, 1, _someoneElse);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var watched = await Repository().GetWatchedTitlesAsync(_me, null);

        watched.Should().HaveCount(3);
        watched.Single(t => t.Id == office.Id).LastWatchedAt.Should().Be(_now.AddHours(-2));
        watched.Single(t => t.Id == heat.Id).Should().Match<Vora.Application.Recommendations.WatchedTitle>(t => t.Rating == 9 && t.Year == 2001);
        watched.Single(t => t.Id == alien.Id).LastWatchedAt.Should().BeNull();
    }

    [Fact]
    public async Task Watched_titles_are_only_the_librarys_own_when_one_is_given()
    {
        var (office, eps) = Show("The Office");
        Played(eps[0].Id, 1);
        Played(Movie("Heat").Id, 1);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Repository().GetWatchedTitlesAsync(_me, _shows)).Select(t => t.Id).Should().Equal(office.Id);
    }

    [Fact]
    public async Task Anything_started_or_rated_is_left_out_and_unrated_titles_come_last()
    {
        var watchedMovie = Movie("Watched", 9);
        var halfway = Movie("Halfway", 9);
        var rated = Movie("Rated", 9);
        var (finishedShow, finished) = Show("Breaking Bad", 9);
        var (startedShow, started) = Show("Better Call Saul", 8.7m);
        var unrated = Movie("No Score");
        var best = Movie("Best", 8.5m);
        var (fresh, _) = Show("Fresh Show", 7.2m);
        Played(watchedMovie.Id, 1);
        Played(halfway.Id, 1, resume: 1200, played: false);
        Rated(rated.Id, 7);
        finished.ForEach(e => Played(e.Id, 1));
        Played(started[0].Id, 1);
        Played(fresh.Id, 1, _someoneElse);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var top = await Repository().GetTopUnwatchedAsync(_me, null, 10);

        top.Should().Equal(best.Id, fresh.Id, unrated.Id);
        top.Should().NotContain(new[] { finishedShow.Id, startedShow.Id });
    }

    [Fact]
    public async Task A_genre_row_is_that_genres_unstarted_titles_in_the_library()
    {
        var comedy = Movie("Comedy", 7, _comedy);
        Movie("Drama", 9, _drama);
        var seen = Movie("Seen Comedy", 8, _comedy);
        Played(seen.Id, 1);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Repository().GetTopUnwatchedByGenreAsync(35, _me, _movies, 10)).Should().Equal(comedy.Id);
        (await Repository().GetTopUnwatchedByGenreAsync(35, _me, _shows, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Genres_come_back_per_title()
    {
        var a = Movie("A", genre: _comedy);
        var b = Movie("B", genre: _drama);
        Movie("C", genre: _drama);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var genres = await Repository().GetGenresAsync(new[] { a.Id, b.Id });

        genres.Select(g => (g.TitleId, g.GenreName)).Should().BeEquivalentTo(new[] { (a.Id, "Comedy"), (b.Id, "Drama") });
    }

    [Fact]
    public async Task Ai_recommendations_follow_the_accounts_switch_for_an_admin_too()
    {
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@example.com", DisplayName = "admin", IsAdmin = true, EnableAiRecommendations = false };
        var allowed = new User { Id = Guid.NewGuid(), Email = "allowed@example.com", DisplayName = "allowed", EnableAiRecommendations = true };
        var notAllowed = new User { Id = Guid.NewGuid(), Email = "no@example.com", DisplayName = "no", EnableAiRecommendations = false };
        _db.AddRange(admin, allowed, notAllowed);
        var adminProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Andy", UserId = admin.Id };
        var allowedProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Sam", UserId = allowed.Id };
        var notAllowedProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Kid", UserId = notAllowed.Id };
        _db.AddRange(adminProfile, allowedProfile, notAllowedProfile);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new OpenAiRecommendationRepository(_db);

        (await repository.IsAiEnabledForProfileAsync(adminProfile.Id)).Should().BeFalse();
        (await repository.IsAiEnabledForProfileAsync(allowedProfile.Id)).Should().BeTrue();
        (await repository.IsAiEnabledForProfileAsync(notAllowedProfile.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task The_ai_is_told_shows_by_name_the_latest_first_and_the_loved_ones_apart()
    {
        var (office, eps) = Show("The Office");
        var heat = Movie("Heat");
        var cats = Movie("Cats");
        var alien = Movie("Alien");
        Played(eps[0].Id, 1);
        Played(heat.Id, 5);
        Played(cats.Id, 3);
        Rated(cats.Id, 2);
        Rated(alien.Id, 10);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var taste = await new OpenAiRecommendationRepository(_db).GetTasteContextAsync(_me, null, 10, 10);

        taste.Recent.Should().Equal("The Office", "Heat (2001)");
        taste.Loved.Should().Equal("Alien (2001)");
        taste.Kind.Should().Be("movies and TV shows");
        (await new OpenAiRecommendationRepository(_db).GetTasteContextAsync(_me, _shows, 10, 10)).Kind.Should().Be("TV shows");
    }
}
