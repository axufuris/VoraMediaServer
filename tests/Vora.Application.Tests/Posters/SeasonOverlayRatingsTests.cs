using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Posters;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Posters;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Posters;

// A season's ratings overlay draws its own ratings, and the show's for any it
// doesn't have, one rating at a time.
public sealed class SeasonOverlayRatingsTests : IDisposable
{
    private static readonly OverlayRatings Show = new(8m, "TMDB", 7.9m, "Rotten Tomatoes", 91m);

    [Fact]
    public void A_season_without_ratings_draws_the_shows()
    {
        new OverlayRatings(null, null, null, null, null).OrShows(Show).Should().Be(Show);
    }

    [Fact]
    public void Each_rating_the_season_has_is_its_own_and_each_it_lacks_is_the_shows()
    {
        // A Rotten Tomatoes score but no Vora rating or TMDB score.
        var season = new OverlayRatings(null, null, null, "Rotten Tomatoes", 64m);

        season.OrShows(Show).Should().Be(new OverlayRatings(8m, "TMDB", 7.9m, "Rotten Tomatoes", 64m));
    }

    [Fact]
    public void A_season_with_every_rating_draws_only_its_own()
    {
        var season = new OverlayRatings(6m, "TMDB", 6.8m, "Rotten Tomatoes", 70m);

        season.OrShows(Show).Should().Be(season);
    }

    [Fact]
    public void A_source_the_season_draws_in_its_other_slot_is_not_drawn_twice()
    {
        var season = new OverlayRatings(null, null, null, "TMDB", 6.8m);
        var show = new OverlayRatings(null, "tmdb", 7.9m, null, null);

        season.OrShows(show).Should().Be(new OverlayRatings(null, null, null, "TMDB", 6.8m));
    }

    [Fact]
    public void A_show_without_ratings_leaves_the_season_as_it_is()
    {
        var season = new OverlayRatings(null, "TMDB", null, null, null);

        season.OrShows(new OverlayRatings(null, null, null, null, null)).Should().Be(season);
    }

    // --- drawing a season ---------------------------------------------------

    private const string Layout = """[{"type":"composite_ratings"}]""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-season-ratings-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IOverlayTemplateRepository _templates = Substitute.For<IOverlayTemplateRepository>();
    private readonly IOverlayProvider _provider = Substitute.For<IOverlayProvider>();
    private readonly PosterOverlayManager _manager;
    private OverlayMediaDto? _drawn;

    public SeasonOverlayRatingsTests()
    {
        Directory.CreateDirectory(_root);
        _templates.GetTemplatesForLibraryAsync(Arg.Any<Guid>())
            .Returns(new List<OverlayTemplate> { new() { TargetMediaType = "Season", ConfigurationJson = Layout } });
        _provider.GenerateOverlayAsync(Arg.Do<OverlayMediaDto>(d => _drawn = d), Arg.Any<string>(), Layout, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => $"/api/artwork/custom/{call.Arg<OverlayMediaDto>().Id}_overlay_x.jpg");

        _manager = new PosterOverlayManager(
            _media,
            _templates,
            [_provider],
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "custom"), OriginalArtworkCache = Path.Combine(_root, "original") }),
            Substitute.For<IHttpClientFactory>(),
            new NullTaskProgressReporter(),
            Substitute.For<IArtworkThumbnailService>(),
            Substitute.For<ILocalArtworkLocator>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<PosterOverlayManager>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Season SeasonOf(Guid showId, int number)
    {
        var poster = Path.Combine(_root, $"season{number}.jpg");
        File.WriteAllBytes(poster, [1]);
        var season = new Season { Title = $"Season {number}", SeasonNumber = number, TvShowId = showId, PosterUrl = poster, OriginalPosterUrl = poster };
        _media.GetForPosterOverlayAsync(season.Id).Returns(season);
        return season;
    }

    [Fact]
    public async Task A_season_poster_is_drawn_with_the_shows_ratings_it_lacks()
    {
        var showId = Guid.NewGuid();
        var season = SeasonOf(showId, 2);
        season.ThirdPartyRating2Name = "Rotten Tomatoes";
        season.ThirdPartyRating2 = 64m;
        _media.GetOverlayRatingsAsync(showId).Returns(Show);

        await _manager.GenerateOverlaysForMediaAsync(season.Id, TestContext.Current.CancellationToken);

        _drawn.Should().NotBeNull();
        _drawn!.ServerAdminRating.Should().Be(8m);
        (_drawn.ThirdPartyRating1Name, _drawn.ThirdPartyRating1).Should().Be(("TMDB", 7.9m));
        (_drawn.ThirdPartyRating2Name, _drawn.ThirdPartyRating2).Should().Be(("Rotten Tomatoes", 64m));
    }

    [Fact]
    public async Task Redrawing_a_show_redraws_each_of_its_seasons()
    {
        var showId = Guid.NewGuid();
        var one = SeasonOf(showId, 1);
        var two = SeasonOf(showId, 2);
        _media.GetSeasonIdsOfShowAsync(showId).Returns([one.Id, two.Id]);
        _media.GetOverlayRatingsAsync(showId).Returns(Show);

        var redrawn = await _manager.GenerateOverlaysForShowSeasonsAsync(showId, TestContext.Current.CancellationToken);

        redrawn.Should().Equal(one.Id, two.Id);
        one.PosterUrl.Should().Be($"/api/artwork/custom/{one.Id}_overlay_x.jpg");
        two.PosterUrl.Should().Be($"/api/artwork/custom/{two.Id}_overlay_x.jpg");
    }
}
