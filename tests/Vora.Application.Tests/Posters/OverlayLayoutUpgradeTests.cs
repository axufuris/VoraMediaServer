using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Application.Posters;
using Vora.Application.Posters.Providers;
using Vora.Application.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Posters;

public sealed class OverlayLayoutUpgradeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-overlay-layout-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private PosterOverlayManager Manager(params IOverlayProvider[] providers) => new(
        _media,
        Substitute.For<IOverlayTemplateRepository>(),
        providers,
        Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "custom"), OriginalArtworkCache = Path.Combine(_root, "original") }),
        Substitute.For<IHttpClientFactory>(),
        new NullTaskProgressReporter(),
        Substitute.For<Vora.Application.Artwork.IArtworkThumbnailService>(),
        Substitute.For<Vora.Application.Artwork.ILocalArtworkLocator>(),
        Substitute.For<IServiceScopeFactory>(),
        NullLogger<PosterOverlayManager>.Instance);

    // A change to how posters are drawn (the ratings keeping their slots) only
    // reached posters that some later sync happened to redraw, and the nightly
    // overlay schedule is off by default, so they kept the old drawing.
    [Fact]
    public async Task Posters_drawn_with_an_older_layout_are_found_against_the_current_one()
    {
        var provider = new LocalImageSharpOverlayProvider(NullLogger<LocalImageSharpOverlayProvider>.Instance);
        _media.AnyOverlayFromAnotherLayoutAsync(provider.LayoutVersion).Returns(true);

        (await Manager(provider).HasOverlaysFromAnOlderLayoutAsync()).Should().BeTrue();
        await _media.Received(1).AnyOverlayFromAnotherLayoutAsync(provider.LayoutVersion);
    }

    [Fact]
    public async Task Nothing_needs_redrawing_when_every_poster_has_the_current_layout()
    {
        var provider = new LocalImageSharpOverlayProvider(NullLogger<LocalImageSharpOverlayProvider>.Instance);
        _media.AnyOverlayFromAnotherLayoutAsync(provider.LayoutVersion).Returns(false);

        (await Manager(provider).HasOverlaysFromAnOlderLayoutAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_is_redrawn_without_an_overlay_provider()
    {
        (await Manager().HasOverlaysFromAnOlderLayoutAsync()).Should().BeFalse();
        await _media.DidNotReceive().AnyOverlayFromAnotherLayoutAsync(Arg.Any<int>());
    }

    [Fact]
    public void The_slot_layout_is_a_new_layout_version()
    {
        new LocalImageSharpOverlayProvider(NullLogger<LocalImageSharpOverlayProvider>.Instance).LayoutVersion.Should().BeGreaterThanOrEqualTo(11);
    }
}
