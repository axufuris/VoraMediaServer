using Microsoft.EntityFrameworkCore;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class LogoProjectionInheritanceTests
{
    private const string ShowLogo = "https://image.tmdb.org/t/p/original/show-logo.png";
    private const string MovieLogo = "https://image.tmdb.org/t/p/original/movie-logo.png";

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("logo-projection-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class Seeded
    {
        public required Guid MovieId { get; init; }
        public required Guid ShowId { get; init; }
        public required Guid SeasonId { get; init; }
        public required Guid EpisodeId { get; init; }
        public required Guid LogolessMovieId { get; init; }
    }

    private static async Task<Seeded> SeedAsync(VoraDbContext db)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Library", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/media" } };
        var movie = new Movie { Id = Guid.NewGuid(), Title = "Dune", LibraryId = library.Id, LogoUrl = MovieLogo };
        var logoless = new Movie { Id = Guid.NewGuid(), Title = "No Art", LibraryId = library.Id };
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Severance", LibraryId = library.Id, LogoUrl = ShowLogo };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id };
        var episode = new Episode { Id = Guid.NewGuid(), Title = "Good News About Hell", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = library.Id };

        db.AddRange(library, movie, logoless, show, season, episode);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new Seeded
        {
            MovieId = movie.Id,
            ShowId = show.Id,
            SeasonId = season.Id,
            EpisodeId = episode.Id,
            LogolessMovieId = logoless.Id
        };
    }

    private static Task<LibraryItemVM?> LibraryItemAsync(VoraDbContext db, Guid id) =>
        db.MediaItems.AsNoTracking().Where(m => m.Id == id).Select(LibraryItemVM.Projection).FirstOrDefaultAsync();

    private static Task<MediaDetailsVM?> DetailsAsync(VoraDbContext db, Guid id) =>
        db.MediaItems.AsNoTracking().Where(m => m.Id == id).Select(MediaDetailsVM.Projection).FirstOrDefaultAsync();

    [Fact]
    public async Task A_movie_carries_its_own_logo()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);

        (await LibraryItemAsync(db, seeded.MovieId))!.LogoUrl.Should().Be(MovieLogo);
        (await DetailsAsync(db, seeded.MovieId))!.LogoUrl.Should().Be(MovieLogo);
    }

    [Fact]
    public async Task A_season_shows_the_logo_of_the_show_it_belongs_to()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);

        (await LibraryItemAsync(db, seeded.SeasonId))!.LogoUrl.Should().Be(ShowLogo);
        (await DetailsAsync(db, seeded.SeasonId))!.LogoUrl.Should().Be(ShowLogo);
    }

    [Fact]
    public async Task An_episode_shows_the_logo_of_the_show_it_belongs_to()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);

        (await LibraryItemAsync(db, seeded.EpisodeId))!.LogoUrl.Should().Be(ShowLogo);
        (await DetailsAsync(db, seeded.EpisodeId))!.LogoUrl.Should().Be(ShowLogo);
    }

    [Fact]
    public async Task An_item_with_no_logo_art_reports_null_rather_than_a_placeholder()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);

        var item = await LibraryItemAsync(db, seeded.LogolessMovieId);

        item!.LogoUrl.Should().BeNull();
    }
}
