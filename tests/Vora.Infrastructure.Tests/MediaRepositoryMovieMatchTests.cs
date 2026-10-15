using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryMovieMatchTests : IDisposable
{
    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("movie-match-" + Guid.NewGuid().ToString("N"))
        .Options);
    private readonly Guid _library = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private MediaRepository Repo() => new(NullLogger<MediaRepository>.Instance, _db);

    private Movie AddMovie(string title, int year, string? imdbId = null, string? tmdbId = null, params string[] files)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, LibraryId = _library, ReleaseDate = new DateOnly(year, 1, 1), ImdbId = imdbId, TmdbId = tmdbId };
        foreach (var file in files)
        {
            movie.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = file, MediaItemId = movie.Id });
        }
        _db.Set<Movie>().Add(movie);
        _db.SaveChanges();
        return movie;
    }

    [Fact]
    public async Task A_film_of_the_same_title_and_year_is_matched()
    {
        var princess = AddMovie("The Princess", 2022, imdbId: "tt13406136");

        (await Repo().GetMovieIdByTitleAndYearAsync("The Princess", 2022, _library)).Should().Be(princess.Id);
        (await Repo().GetMovieIdByTitleAndYearAsync("The Princess", 2022, _library, imdbId: "TT13406136")).Should().Be(princess.Id);
    }

    [Fact]
    public async Task A_film_whose_ids_contradict_the_files_is_another_film()
    {
        AddMovie("The Princess", 2022, imdbId: "tt13406136");
        AddMovie("Role Play", 2023, tmdbId: "1007426");

        (await Repo().GetMovieIdByTitleAndYearAsync("The Princess", 2022, _library, imdbId: "tt13393128")).Should().BeNull();
        (await Repo().GetMovieIdByTitleAndYearAsync("Role Play", 2023, _library, tmdbId: "933131")).Should().BeNull();
    }

    [Fact]
    public async Task A_film_with_no_ids_takes_a_file_whatever_ids_it_carries()
    {
        var heat = AddMovie("Heat", 1995);

        (await Repo().GetMovieIdByTitleAndYearAsync("Heat", 1995, _library, imdbId: "tt0113277")).Should().Be(heat.Id);
    }

    [Fact]
    public async Task A_title_without_a_year_matches_only_when_one_film_has_it()
    {
        var heat = AddMovie("Heat", 1995);

        (await Repo().GetMovieIdByTitleAndYearAsync("Heat", null, _library)).Should().Be(heat.Id);

        AddMovie("Heat", 1986);

        (await Repo().GetMovieIdByTitleAndYearAsync("Heat", null, _library)).Should().BeNull();
    }

    [Fact]
    public async Task Lists_every_file_of_the_films_holding_several_and_says_which_were_matched_by_hand()
    {
        var princess = AddMovie("The Princess", 2022, "tt13406136", null, "/movies/1080p/The Princess.mkv", "/movies/4K/The Princess.mkv");
        var fixedByHand = AddMovie("Heat", 1995, "tt0113277", null, "/movies/1080p/Heat.mkv", "/movies/4K/Heat.mkv");
        fixedByHand.LockedFields = [MediaMatchManager.MatchLock];
        AddMovie("Alien", 1979, "tt0078748", null, "/movies/1080p/Alien.mkv");
        _db.SaveChanges();

        var files = await Repo().GetFilesOfMoviesWithSeveralFilesAsync(_library);

        files.Should().HaveCount(4);
        files.Where(f => f.MovieId == princess.Id).Should().OnlyContain(f => f.ImdbId == "tt13406136" && !f.MatchedByHand);
        files.Where(f => f.MovieId == fixedByHand.Id).Should().OnlyContain(f => f.MatchedByHand);
    }
}
