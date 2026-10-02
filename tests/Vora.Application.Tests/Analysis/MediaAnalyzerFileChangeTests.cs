using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using NSubstitute.ReturnsExtensions;

namespace Vora.Application.Tests.Analysis;

public class MediaAnalyzerFileChangeTests
{
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IMediaAnalyzerService _analyzer = Substitute.For<IMediaAnalyzerService>();
    private readonly MediaAnalyzerManager _manager;

    public MediaAnalyzerFileChangeTests()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting());

        _manager = new MediaAnalyzerManager(
            _media,
            _analyzer,
            Substitute.For<IMarkerAssembler>(),
            new AudioIntroDetector(new AudioFingerprintComparer()),
            settings,
            Substitute.For<Vora.Application.Streaming.ISubtitleExtractionService>(),
            Substitute.For<Vora.Application.Subtitles.IExternalSubtitleScanner>(),
            Substitute.For<ITaskQueueManager>(),
            Substitute.For<IClientNotifier>(),
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(),
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions()),
            NullLogger<MediaAnalyzerManager>.Instance);
    }

    private Movie ItemWithThumbnails(MediaPart part)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Title = "Dead Snow",
            MarkersAnalyzedAt = DateTime.UtcNow,
            VideoThumbnailSpriteVersion = "v2-webp-10-320",
            MediaParts = new List<MediaPart> { part }
        };
        part.MediaItemId = movie.Id;
        _media.GetForAnalysisAsync(movie.Id).Returns(movie);
        return movie;
    }

    private void Probes(MediaPart part) =>
        _analyzer.AnalyzeFileAsync(part.FilePath, Arg.Any<CancellationToken>()).Returns(new MediaAnalysisResult { Duration = TimeSpan.FromMinutes(91) });

    [Fact]
    public async Task A_replaced_file_makes_the_thumbnails_and_markers_stale()
    {
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/Dead Snow.mkv", LastAnalyzedAt = DateTime.UtcNow.AddDays(-30), FileSizeBytes = 1, VideoThumbnailSpriteVersion = "v2-webp-10-320" };
        var movie = ItemWithThumbnails(part);
        Probes(part);

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.VideoThumbnailSpriteVersion.Should().BeNull();
        part.VideoThumbnailSpriteVersion.Should().BeNull();
        movie.MarkersAnalyzedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_new_version_without_thumbnails_makes_the_title_stale()
    {
        var original = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/Dead Snow 1080p.mkv", LastAnalyzedAt = DateTime.UtcNow, VideoThumbnailSpriteVersion = "v2-webp-10-320" };
        var added = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/Dead Snow 4K.mkv" };
        var movie = ItemWithThumbnails(original);
        movie.MediaParts.Add(added);
        Probes(original);
        Probes(added);

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.VideoThumbnailSpriteVersion.Should().BeNull();
    }

    [Fact]
    public async Task A_first_analysis_of_a_file_that_already_has_thumbnails_keeps_them()
    {
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/Dead Snow.mkv", VideoThumbnailSpriteVersion = "v2-webp-10-320" };
        var movie = ItemWithThumbnails(part);
        Probes(part);

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.VideoThumbnailSpriteVersion.Should().Be("v2-webp-10-320");
        part.VideoThumbnailSpriteVersion.Should().Be("v2-webp-10-320");
    }

    [Fact]
    public async Task A_file_that_could_not_be_read_leaves_thumbnails_and_markers_alone()
    {
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/gone.mkv", LastAnalyzedAt = DateTime.UtcNow.AddDays(-3), FileSizeBytes = 10, VideoThumbnailSpriteVersion = "v2-webp-10-320" };
        var movie = ItemWithThumbnails(part);
        _analyzer.AnalyzeFileAsync(part.FilePath, Arg.Any<CancellationToken>()).ReturnsNull();

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.VideoThumbnailSpriteVersion.Should().Be("v2-webp-10-320");
        part.VideoThumbnailSpriteVersion.Should().Be("v2-webp-10-320");
        movie.MarkersAnalyzedAt.Should().NotBeNull();
    }
}
