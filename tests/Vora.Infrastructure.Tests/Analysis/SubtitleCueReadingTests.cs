using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Tests.Transcoding;

namespace Vora.Infrastructure.Tests.Analysis;

public sealed class SubtitleCueReadingTests : IDisposable
{
    private const string Srt = """
        1
        00:00:01,000 --> 00:00:03,500
        Previously on The Terminal List...

        2
        00:00:20,000 --> 00:00:22,000
        <i>We go in at dawn.</i>

        3
        00:03:10,000 --> 00:03:12,000
        This line is after the window.

        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vora-subtitle-cues-" + Guid.NewGuid().ToString("N"));

    public SubtitleCueReadingTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Ffmpegs_webvtt_is_read_with_and_without_hours_and_with_cue_settings()
    {
        const string vtt = """
            WEBVTT

            00:01.000 --> 00:03.500
            Previously on
            The Terminal List...

            1:05:20.250 --> 1:05:22.000 align:start position:10%
            <i>Where are we?</i>

            """;

        WebVttCues.Parse(vtt).Should().Equal(
            new SubtitleCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3.5), "Previously on The Terminal List..."),
            new SubtitleCue(new TimeSpan(0, 1, 5, 20, 250), new TimeSpan(0, 1, 5, 22, 0), "<i>Where are we?</i>"));
    }

    [Fact]
    public void Only_the_window_is_read_from_an_embedded_track_or_a_sidecar_file()
    {
        FFmpegAnalyzerService.SubtitleCueArguments("/tv/ep.mkv", null, 3, 90).Should().Equal(
            "-v", "error", "-i", "/tv/ep.mkv", "-map", "0:3", "-t", "90", "-f", "webvtt", "pipe:1");
        FFmpegAnalyzerService.SubtitleCueArguments("/tv/ep.mkv", "/tv/ep.en.srt", 0, 120).Should().Equal(
            "-v", "error", "-i", "/tv/ep.en.srt", "-map", "0:s:0", "-t", "120", "-f", "webvtt", "pipe:1");
    }

    [Fact]
    public async Task The_recap_line_is_read_from_a_subtitle_track_inside_the_file()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var srt = Path.Combine(_folder, "ep.en.srt");
        await File.WriteAllTextAsync(srt, Srt, cancellationToken);
        var episode = Path.Combine(_folder, "ep.mkv");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=5:duration=200",
            "-i", srt,
            "-map", "0:v", "-map", "1:s",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:s", "srt", episode);

        var cues = await new FFmpegAnalyzerService(NullLogger<FFmpegAnalyzerService>.Instance)
            .ReadSubtitleCuesAsync(episode, null, 1, RecapCues.SearchWindow.TotalSeconds, cancellationToken);

        cues.Select(cue => cue.Text).Should().Equal("Previously on The Terminal List...", "<i>We go in at dawn.</i>");
        RecapCues.FirstRecapCueStart(cues, RecapCues.SearchWindow).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task The_recap_line_is_read_from_a_sidecar_subtitle_file()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var srt = Path.Combine(_folder, "ep.en.srt");
        await File.WriteAllTextAsync(srt, Srt, cancellationToken);

        var cues = await new FFmpegAnalyzerService(NullLogger<FFmpegAnalyzerService>.Instance)
            .ReadSubtitleCuesAsync(Path.Combine(_folder, "ep.mkv"), srt, 0, RecapCues.SearchWindow.TotalSeconds, cancellationToken);

        cues.Should().HaveCount(2);
        cues[0].Should().Be(new SubtitleCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3.5), "Previously on The Terminal List..."));
    }

    [Fact]
    public async Task A_file_ffmpeg_cannot_read_gives_no_lines()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);

        var cues = await new FFmpegAnalyzerService(NullLogger<FFmpegAnalyzerService>.Instance)
            .ReadSubtitleCuesAsync(Path.Combine(_folder, "missing.mkv"), null, 2, 120, TestContext.Current.CancellationToken);

        cues.Should().BeEmpty();
    }
}
