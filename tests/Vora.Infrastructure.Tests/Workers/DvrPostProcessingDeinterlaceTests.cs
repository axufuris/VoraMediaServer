using System.Diagnostics;
using System.Text.RegularExpressions;
using Vora.Domain.Entities.Settings;
using Vora.Infrastructure.Tests.Transcoding;
using Vora.Infrastructure.Workers;

namespace Vora.Infrastructure.Tests.Workers;

public sealed class DvrPostProcessingDeinterlaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-dvr-" + Guid.NewGuid().ToString("N"));

    public DvrPostProcessingDeinterlaceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task An_interlaced_antenna_recording_is_progressive_in_the_mp4()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;

        // 59.94 fields a second woven into 29.97 interlaced MPEG-2 frames, as a 1080i or 480i broadcast is:
        // anything that moves between the two fields of a frame is combed unless it is deinterlaced.
        var recording = Path.Combine(_root, "recording.ts");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=720x480:rate=60000/1001:duration=4",
            "-f", "lavfi", "-i", "sine=duration=4",
            "-vf", "interlace=scan=tff:lowpass=0",
            "-c:v", "mpeg2video", "-flags", "+ilme+ildct", "-top", "1", "-g", "15", "-b:v", "6M",
            "-c:a", "ac3", recording);
        var before = await FieldsAsync(recording, cancellationToken);
        before.Interlaced.Should().BeGreaterThan(before.Progressive * 4, "the sample has to be combed for this test to mean anything");

        var mp4 = Path.Combine(_root, "recording.mp4");
        var args = DvrPostProcessingWorker.BuildFfmpegArgs(recording, mp4, new ServerSetting { BackgroundX264Preset = 0 }, encoder: null);
        await FfmpegTool.RunAsync(cancellationToken, [.. args]);

        var after = await FieldsAsync(mp4, cancellationToken);
        after.Progressive.Should().BeGreaterThan(after.Interlaced * 4);
    }

    [Fact]
    public async Task A_progressive_recording_comes_out_frame_for_frame_as_it_would_without_the_filter()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;

        var recording = Path.Combine(_root, "progressive.ts");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=60000/1001:duration=3",
            "-f", "lavfi", "-i", "sine=duration=3",
            "-c:v", "mpeg2video", "-g", "30", "-b:v", "8M",
            "-c:a", "ac3", recording);

        var filtered = Path.Combine(_root, "filtered.mp4");
        var args = DvrPostProcessingWorker.BuildFfmpegArgs(recording, filtered, new ServerSetting { BackgroundX264Preset = 0 }, encoder: null);
        await FfmpegTool.RunAsync(cancellationToken, [.. args]);

        var unfiltered = Path.Combine(_root, "unfiltered.mp4");
        var without = DvrPostProcessingWorker.BuildFfmpegArgs(recording, unfiltered, new ServerSetting { BackgroundX264Preset = 0 }, encoder: null);
        without.RemoveRange(without.IndexOf("-vf"), 2);
        await FfmpegTool.RunAsync(cancellationToken, [.. without]);

        var withFilter = await FrameHashesAsync(filtered, cancellationToken);
        withFilter.Split('\n').Count(line => line.Length > 0 && line[0] != '#').Should().BeGreaterThan(170, "a 3 s 59.94 fps recording keeps its frame rate");
        withFilter.Should().Be(await FrameHashesAsync(unfiltered, cancellationToken), "yadif only touches frames flagged interlaced");
    }

    private static async Task<string> FrameHashesAsync(string video, CancellationToken cancellationToken) =>
        System.Text.Encoding.UTF8.GetString(await FfmpegTool.RunAsync(cancellationToken, "-i", video, "-map", "0:v:0", "-f", "framemd5", "-"));

    private static async Task<(int Interlaced, int Progressive)> FieldsAsync(string video, CancellationToken cancellationToken)
    {
        var log = await ReadLogAsync(["-hide_banner", "-nostats", "-i", video, "-an", "-vf", "idet", "-f", "null", "-"], cancellationToken);
        // The summary is printed again each time the filter graph is rebuilt; the last one counts every frame.
        var matches = Regex.Matches(log, @"Multi frame detection: TFF:\s*(\d+)\s+BFF:\s*(\d+)\s+Progressive:\s*(\d+)");
        matches.Should().NotBeEmpty($"ffmpeg's idet summary was expected in: {log}");
        var match = matches[^1];
        return (int.Parse(match.Groups[1].Value) + int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }

    private static async Task<string> ReadLogAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg did not start");
        var log = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return log;
    }
}
