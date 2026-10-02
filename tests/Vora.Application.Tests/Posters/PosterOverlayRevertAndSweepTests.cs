using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Application.Posters;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Posters;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Posters;

public sealed class PosterOverlayRevertAndSweepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-overlay-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _overlays;
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IMediaRepository _scopedMedia = Substitute.For<IMediaRepository>();
    private readonly IOverlayTemplateRepository _templates = Substitute.For<IOverlayTemplateRepository>();
    private readonly PosterOverlayManager _manager;

    public PosterOverlayRevertAndSweepTests()
    {
        _overlays = Path.Combine(_root, "custom");

        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediaRepository)).Returns(_scopedMedia);
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        _templates.GetTemplatesForLibraryAsync(Arg.Any<Guid>()).Returns(new List<OverlayTemplate>());
        _media.GetReferencedOverlayFileNamesAsync().Returns(new HashSet<string>());

        _manager = new PosterOverlayManager(
            _media,
            _templates,
            Array.Empty<IOverlayProvider>(),
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions { CustomArtwork = _overlays, OriginalArtworkCache = Path.Combine(_root, "original") }),
            Substitute.For<IHttpClientFactory>(),
            new NullTaskProgressReporter(),
            Substitute.For<Vora.Application.Artwork.IArtworkThumbnailService>(),
            scopes,
            NullLogger<PosterOverlayManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string OverlayFile(string name, TimeSpan age)
    {
        var path = Path.Combine(_overlays, name);
        File.WriteAllBytes(path, new byte[] { 1 });
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    private static Movie Overlaid(string title, string overlayFile) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        PosterUrl = $"/api/artwork/custom/{overlayFile}",
        OriginalPosterUrl = "https://image.tmdb.org/t/p/original/poster.jpg",
        LastOverlayGeneratedAt = DateTime.UtcNow.AddDays(-1)
    };

    [Fact]
    public async Task One_item_failing_to_revert_does_not_stop_the_rest()
    {
        var deleted = Overlaid("Gone", "gone_overlay_1.png");
        var kept = Overlaid("Dead Snow", "deadsnow_overlay_1.png");
        var keptFile = OverlayFile("deadsnow_overlay_1.png", TimeSpan.FromDays(1));
        var deletedFile = OverlayFile("gone_overlay_1.png", TimeSpan.FromDays(1));
        _media.GetItemsPendingOverlayGenerationAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<int>()).Returns(new List<MediaItem> { deleted, kept });
        _scopedMedia.UpdateMediaItemAsync(Arg.Is<MediaItem>(m => m.Id == deleted.Id)).Returns(Task.FromException(new InvalidOperationException("row is gone")));

        await _manager.RunLibraryOverlaySyncAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await _scopedMedia.Received(1).UpdateMediaItemAsync(Arg.Is<MediaItem>(m => m.Id == kept.Id && m.PosterUrl == kept.OriginalPosterUrl && m.LastOverlayGeneratedAt == null));
        File.Exists(keptFile).Should().BeFalse();
        File.Exists(deletedFile).Should().BeTrue();
    }

    [Fact]
    public async Task The_sweep_removes_old_orphans_but_not_a_file_just_written()
    {
        var oldOrphan = OverlayFile("old_overlay_1.png", TimeSpan.FromHours(2));
        var fresh = OverlayFile("fresh_overlay_1.png", TimeSpan.FromSeconds(5));
        var inUse = OverlayFile("used_overlay_1.png", TimeSpan.FromDays(3));
        _media.GetReferencedOverlayFileNamesAsync().Returns(new HashSet<string> { "used_overlay_1.png" });

        var deleted = await _manager.SweepOrphanedOverlayFilesAsync(TestContext.Current.CancellationToken);

        deleted.Should().Be(1);
        File.Exists(oldOrphan).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue();
        File.Exists(inUse).Should().BeTrue();
    }
}
