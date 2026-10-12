using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Thumbnails;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Thumbnails;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class CoverImageVideoFileTests : IDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int DurationSeconds = 16;
    private const int BlackSeconds = 4;
    private const int TileWidth = 160;
    private const int TileHeight = 90;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-cover-image-" + Guid.NewGuid().ToString("N"));

    public CoverImageVideoFileTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<string> EpisodeWithLargerCoverTrackAsync(CancellationToken cancellationToken)
    {
        var episode = Path.Combine(_root, "episode.mkv");
        var cover = Path.Combine(_root, "cover.jpg");
        var source = Path.Combine(_root, "source.mkv");

        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", $"testsrc2=size={Width}x{Height}:rate=24:duration={DurationSeconds},drawbox=color=black:t=fill:enable='lt(t,{BlackSeconds})'",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={DurationSeconds}",
            "-c:v", "libx264", "-preset", "ultrafast", "-g", "24", "-pix_fmt", "yuv420p", "-c:a", "aac", episode);
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "color=c=red:size=2000x3000", "-frames:v", "1", cover);
        await FfmpegTool.RunAsync(cancellationToken,
            "-i", episode, "-i", cover, "-map", "0", "-map", "1:v", "-c", "copy",
            "-disposition:v:0", "default", "-disposition:v:1", "0", source);
        return source;
    }

    [Fact]
    public async Task Scrub_bar_thumbnails_are_made_from_the_video()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await EpisodeWithLargerCoverTrackAsync(cancellationToken);
        var sprite = Path.Combine(_root, "sprite.webp");
        var generator = new FFmpegVideoThumbnailGeneratorService(NullLogger<FFmpegVideoThumbnailGeneratorService>.Instance);

        var result = await generator.GenerateAsync(
            new VideoThumbnailGenerationParameters { InputPath = source, IntervalSeconds = 4, Width = TileWidth, Height = TileHeight, JpegQuality = 8, SpriteColumns = 4 },
            sprite,
            Path.Combine(_root, "sprite.vtt"),
            cancellationToken);

        var sheetWidth = result.SpriteColumns * TileWidth;
        var sheetHeight = result.SpriteRows * TileHeight;
        var pixels = await FfmpegTool.RunAsync(cancellationToken, "-i", sprite, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-");
        pixels.Should().HaveCount(sheetWidth * sheetHeight * 3);
        var thirdTile = pixels.Chunk(3)
            .Where((_, index) => index / sheetWidth < TileHeight && index % sheetWidth >= 2 * TileWidth && index % sheetWidth < 3 * TileWidth)
            .ToList();
        thirdTile.Count(IsBlue).Should().BeGreaterThan(thirdTile.Count / 20);
        thirdTile.Count(pixel => pixel.Max() > 60).Should().BeGreaterThan(thirdTile.Count * 9 / 10);
    }

    private static bool IsBlue(byte[] pixel) => pixel[2] > 150 && pixel[0] < 90 && pixel[1] < 90;

    [Fact]
    public async Task A_still_is_a_frame_of_the_video()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await EpisodeWithLargerCoverTrackAsync(cancellationToken);
        var still = Path.Combine(_root, "still.jpg");
        var generator = new FFmpegVideoThumbnailGeneratorService(NullLogger<FFmpegVideoThumbnailGeneratorService>.Instance);

        var extracted = await generator.ExtractFrameAsync(source, TimeSpan.FromSeconds(DurationSeconds / 2), still, cancellationToken);

        extracted.Should().BeTrue();
        var pixels = await FfmpegTool.RunAsync(cancellationToken, "-i", still, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", "-");
        pixels.Should().HaveCount(Width * Height);
    }

    [Fact]
    public async Task The_cover_is_not_recorded_as_a_video_track()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await EpisodeWithLargerCoverTrackAsync(cancellationToken);
        var analyzer = new FFmpegAnalyzerService(NullLogger<FFmpegAnalyzerService>.Instance);

        var analysis = await analyzer.AnalyzeFileAsync(source, cancellationToken);

        var video = analysis.VideoTracks.Should().ContainSingle().Subject;
        video.Codec.Should().Be("h264");
        video.StreamIndex.Should().Be(0);
        analysis.AudioTracks.Should().ContainSingle();
    }

    [Fact]
    public async Task Black_frames_are_looked_for_in_the_video()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await EpisodeWithLargerCoverTrackAsync(cancellationToken);
        var analyzer = new FFmpegAnalyzerService(NullLogger<FFmpegAnalyzerService>.Instance);

        var detection = await analyzer.AnalyzeSilenceDetectionsAsync(source, new SilenceDetectionParameters { MinBlackFrameDurationSec = 0.5 }, cancellationToken);

        var black = detection.BlackIntervals.Should().ContainSingle().Subject;
        black.Start.TotalSeconds.Should().BeApproximately(0, 0.5);
        black.End.TotalSeconds.Should().BeApproximately(BlackSeconds, 0.5);
    }
}
