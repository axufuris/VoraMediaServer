using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Tests.Transcoding;

namespace Vora.Infrastructure.Tests.Analysis;

public class FFmpegPictureAreaDetectorTests
{
    private static PictureAreaTarget Target(string path, int width = 1920, int height = 1080, double seconds = 40, int streamIndex = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(), path, streamIndex, width, height, seconds);

    [Fact]
    public void The_box_is_the_last_one_cropdetect_reported()
    {
        const string stderr = """
            [Parsed_cropdetect_0 @ 0x55] x1:0 x2:1919 y1:200 y2:879 w:1920 h:680 x:0 y:200 pts:0 t:0.000 limit:0.094000 crop=1920:680:0:200
            [Parsed_cropdetect_0 @ 0x55] x1:0 x2:1919 y1:60 y2:1019 w:1920 h:960 x:0 y:60 pts:2048 t:2.002 limit:0.094000 crop=1920:960:0:60
            [out#0/null @ 0x56] video:1kB audio:0kB subtitle:0kB other streams:0kB global headers:0kB muxing overhead: unknown
            """;

        CropDetectOutput.LastBox(stderr).Should().Be(new CropBox(0, 60, 1920, 960));
    }

    [Fact]
    public void A_black_sample_reports_no_box()
    {
        const string stderr = "[Parsed_cropdetect_0 @ 0x55] x1:1919 x2:0 y1:1079 y2:0 w:-1904 h:-1072 x:1912 y:1076 pts:0 t:0.000 limit:0.094000 crop=-1904:-1072:1912:1076";

        CropDetectOutput.LastBox(stderr).Should().BeNull();
    }

    [Fact]
    public void Each_sample_decodes_two_keyframes_of_the_main_video_stream_on_one_thread()
    {
        var arguments = FFmpegPictureAreaDetector.Arguments(Target("/movies/Heat.mkv", streamIndex: 2), 754.5);

        arguments.Should().ContainInOrder("-threads", "1", "-skip_frame", "nokey", "-ss", "754.5", "-i", "/movies/Heat.mkv", "-map", "0:2", "-frames:v", "2");
        arguments.Should().Contain("cropdetect=limit=0.094:round=2:reset=0:skip=0");
        arguments.Should().ContainInOrder("-f", "null", "-");
    }

    [Fact]
    public void Ten_samples_spread_across_the_file_away_from_its_ends()
    {
        var times = FFmpegPictureAreaDetector.SampleTimes(1000);

        times.Should().HaveCount(10);
        times[0].Should().BeApproximately(80, 0.001);
        times[^1].Should().BeApproximately(920, 0.001);
        times.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task A_missing_file_is_not_a_result()
    {
        var detector = new FFmpegPictureAreaDetector(NullLogger<FFmpegPictureAreaDetector>.Instance);

        (await detector.DetectAsync(Target(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv")), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData("1920:800:0:140", 140, 0, 140, 0)]
    [InlineData("1440:1080:240:0", 0, 240, 0, 240)]
    [InlineData("1920:1080:0:0", 0, 0, 0, 0)]
    public async Task Real_ffmpeg_finds_the_bars_encoded_into_a_video(string picture, int top, int right, int bottom, int left)
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("vora-bars-");
        try
        {
            var parts = picture.Split(':');
            var video = Path.Combine(folder.FullName, "letterboxed.mkv");
            await FfmpegTool.RunAsync(cancellationToken,
                "-f", "lavfi", "-i", $"testsrc2=size={parts[0]}x{parts[1]}:rate=24:duration=40",
                "-vf", $"pad=1920:1080:{parts[2]}:{parts[3]}:black",
                "-c:v", "libx264", "-preset", "ultrafast", "-g", "48", video);

            var detector = new FFmpegPictureAreaDetector(NullLogger<FFmpegPictureAreaDetector>.Instance);
            var crop = await detector.DetectAsync(Target(video), cancellationToken);

            crop.Should().Be(new PictureCrop(top, right, bottom, left));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
