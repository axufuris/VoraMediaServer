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

public sealed class PosterOverlaySourceTests : IDisposable
{
    private const string Layout = """[{"type":"resolution"}]""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-overlay-source-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IOverlayTemplateRepository _templates = Substitute.For<IOverlayTemplateRepository>();
    private readonly IOverlayProvider _provider = Substitute.For<IOverlayProvider>();
    private readonly ILocalArtworkLocator _localArtwork = Substitute.For<ILocalArtworkLocator>();
    private readonly PosterOverlayManager _manager;

    public PosterOverlaySourceTests()
    {
        Directory.CreateDirectory(_root);
        _templates.GetTemplatesForLibraryAsync(Arg.Any<Guid>())
            .Returns(new List<OverlayTemplate> { new() { TargetMediaType = "Movie", ConfigurationJson = Layout } });

        _manager = new PosterOverlayManager(
            _media,
            _templates,
            [_provider],
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "custom"), OriginalArtworkCache = Path.Combine(_root, "original") }),
            Substitute.For<IHttpClientFactory>(),
            new NullTaskProgressReporter(),
            Substitute.For<IArtworkThumbnailService>(),
            _localArtwork,
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<PosterOverlayManager>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (Movie Movie, string Address, string File) MovieWithLocalPoster()
    {
        var movie = new Movie { Title = "Alien" };
        var file = Path.Combine(_root, "poster.jpg");
        File.WriteAllBytes(file, [1]);
        var address = $"{LocalArtworkAddress.Prefix}{movie.Id}/poster.jpg?v=1";
        movie.PosterUrl = movie.OriginalPosterUrl = address;
        _media.GetForPosterOverlayAsync(movie.Id).Returns(movie);
        _localArtwork.ResolveAsync(address).Returns(file);
        return (movie, address, file);
    }

    [Fact]
    public async Task A_local_poster_is_drawn_on_from_the_media_folder_and_shown_as_the_finished_overlay()
    {
        var (movie, _, file) = MovieWithLocalPoster();
        _provider.GenerateOverlayAsync(Arg.Any<OverlayMediaDto>(), file, Layout, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("/api/artwork/custom/alien_overlay_1.jpg");

        await _manager.GenerateOverlaysForMediaAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.PosterUrl.Should().Be("/api/artwork/custom/alien_overlay_1.jpg");
        movie.LastOverlayGeneratedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task When_there_is_nothing_to_draw_the_poster_keeps_its_address_and_never_becomes_a_path_on_disk()
    {
        var (movie, address, file) = MovieWithLocalPoster();
        _provider.GenerateOverlayAsync(Arg.Any<OverlayMediaDto>(), file, Layout, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(file);

        await _manager.GenerateOverlaysForMediaAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.PosterUrl.Should().Be(address);
    }

    [Fact]
    public async Task A_local_poster_that_has_gone_from_disk_is_left_as_it_is()
    {
        var (movie, address, _) = MovieWithLocalPoster();
        _localArtwork.ResolveAsync(address).Returns((string?)null);

        await _manager.GenerateOverlaysForMediaAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.PosterUrl.Should().Be(address);
        await _provider.DidNotReceive().GenerateOverlayAsync(Arg.Any<OverlayMediaDto>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
