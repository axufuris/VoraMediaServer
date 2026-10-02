using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Tests.Analysis;

public sealed class MediaAnalyzerFileChangeTests : IDisposable
{
    private const string CurrentSprites = "v2-webp-10-320";

    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IMediaAnalyzerService _analyzer = Substitute.For<IMediaAnalyzerService>();
    private readonly MediaAnalyzerManager _manager;
    private readonly List<string> _files = new();

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

    public void Dispose()
    {
        foreach (var file in _files) File.Delete(file);
    }

    private string FileOfSize(int bytes)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[bytes]);
        _files.Add(path);
        return path;
    }

    private Movie ItemWithThumbnails(params MediaPart[] parts)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Title = "Dead Snow",
            MarkersAnalyzedAt = DateTime.UtcNow,
            VideoThumbnailSpriteVersion = CurrentSprites,
            MediaParts = parts.ToList()
        };
        foreach (var part in parts) part.MediaItemId = movie.Id;
        _media.GetForAnalysisAsync(movie.Id).Returns(movie);
        return movie;
    }

    private void Probes(MediaPart part) =>
        _analyzer.AnalyzeFileAsync(part.FilePath, Arg.Any<CancellationToken>())
            .Returns(new MediaAnalysisResult { Duration = TimeSpan.FromMinutes(91), FileSizeBytes = new FileInfo(part.FilePath).Length });

    [Fact]
    public async Task A_replaced_file_makes_the_thumbnails_and_markers_stale()
    {
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = FileOfSize(20), LastAnalyzedAt = DateTime.UtcNow.AddDays(-30), FileSizeBytes = 10, VideoThumbnailSpriteVersion = CurrentSprites };
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
        var original = new MediaPart { Id = Guid.NewGuid(), FilePath = FileOfSize(10), LastAnalyzedAt = DateTime.UtcNow, FileSizeBytes = 10, VideoThumbnailSpriteVersion = CurrentSprites };
        var added = new MediaPart { Id = Guid.NewGuid(), FilePath = FileOfSize(30) };
        var movie = ItemWithThumbnails(original, added);
        Probes(added);

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.VideoThumbnailSpriteVersion.Should().BeNull();
        original.VideoThumbnailSpriteVersion.Should().Be(CurrentSprites);
        await _analyzer.DidNotReceive().AnalyzeFileAsync(original.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_first_analysis_of_a_file_that_already_has_thumbnails_keeps_them()
    {
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = FileOfSize(10), VideoThumbnailSpriteVersion = CurrentSprites };
        var movie = ItemWithThumbnails(part);
        Probes(part);

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        movie.VideoThumbnailSpriteVersion.Should().Be(CurrentSprites);
        part.VideoThumbnailSpriteVersion.Should().Be(CurrentSprites);
    }

    [Fact]
    public async Task A_missing_file_is_not_probed_and_keeps_everything()
    {
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"), LastAnalyzedAt = DateTime.UtcNow.AddDays(-3), FileSizeBytes = 10, Duration = TimeSpan.FromMinutes(91), VideoThumbnailSpriteVersion = CurrentSprites };
        var movie = ItemWithThumbnails(part);

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        await _analyzer.DidNotReceive().AnalyzeFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        part.Duration.Should().Be(TimeSpan.FromMinutes(91));
        movie.VideoThumbnailSpriteVersion.Should().Be(CurrentSprites);
        movie.MarkersAnalyzedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_probe_that_reads_nothing_keeps_the_parts_tracks_markers_and_thumbnails()
    {
        var analyzedAt = DateTime.UtcNow.AddDays(-3);
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = FileOfSize(20), LastAnalyzedAt = analyzedAt, FileSizeBytes = 10, Duration = TimeSpan.FromMinutes(91), VideoThumbnailSpriteVersion = CurrentSprites };
        var movie = ItemWithThumbnails(part);
        _analyzer.AnalyzeFileAsync(part.FilePath, Arg.Any<CancellationToken>()).Returns(new MediaAnalysisResult());

        await _manager.AnalyzeMediaFileAsync(movie.Id, TestContext.Current.CancellationToken);

        part.LastAnalyzedAt.Should().Be(analyzedAt);
        part.Duration.Should().Be(TimeSpan.FromMinutes(91));
        movie.VideoThumbnailSpriteVersion.Should().Be(CurrentSprites);
        movie.MarkersAnalyzedAt.Should().NotBeNull();
        await _media.DidNotReceive().SyncMediaTracksAsync(Arg.Any<Guid>(), Arg.Any<List<MediaVideoTrack>>(), Arg.Any<List<MediaAudioTrack>>(), Arg.Any<List<MediaSubtitleTrack>>());
    }
}
