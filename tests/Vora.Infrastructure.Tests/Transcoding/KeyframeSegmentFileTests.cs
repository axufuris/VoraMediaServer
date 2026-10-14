using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;
using Vora.Infrastructure.Transcoding.Keyframes;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class KeyframeSegmentFileTests : IDisposable
{
    private const string SkipReason = "Needs ffmpeg with the encoder and ffprobe on PATH.";
    private const int DurationSeconds = 40;
    private const string UnevenKeyframes = "2.5,4.4,4.6,9,9.3,13,17.7,18,22,26,31,35";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-keyframe-segments-" + Guid.NewGuid().ToString("N"));

    public KeyframeSegmentFileTests()
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

    private static bool ToolsAvailable(string encoder) => FfmpegTool.IsAvailable() && ProbeAvailable() && EncoderAvailable(encoder);

    private static bool EncoderAvailable(string encoder)
    {
        using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -encoders") { RedirectStandardOutput = true, UseShellExecute = false });
        if (process is null) return false;
        var encoders = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return encoders.Contains(" " + encoder + " ", StringComparison.Ordinal);
    }

    private static bool ProbeAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffprobe", "-hide_banner -version") { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null) return false;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static async Task<string> ProbeAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-v", "error" }.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffprobe did not start");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output;
    }

    private static async Task<List<double>> ProbedKeyframesAsync(string file, CancellationToken cancellationToken)
    {
        var output = await ProbeAsync(cancellationToken, "-select_streams", "v:0", "-show_entries", "packet=pts_time,flags", "-of", "csv=p=0", file);
        return output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(','))
            .Where(parts => parts.Length > 1 && parts[1].Contains('K'))
            .Select(parts => double.Parse(parts[0], CultureInfo.InvariantCulture))
            .Order()
            .ToList();
    }

    private static async Task<double> FirstVideoTimeAsync(string file, CancellationToken cancellationToken)
    {
        var output = await ProbeAsync(cancellationToken, "-select_streams", "v:0", "-show_entries", "packet=pts_time", "-read_intervals", "%+#1", "-of", "csv=p=0", file);
        return double.Parse(output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd(','), CultureInfo.InvariantCulture);
    }

    private static string[] VideoEncoding(string encoder) => encoder == "libx265"
        ? ["-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "keyint=240:min-keyint=12:scenecut=0:bframes=3:log-level=error"]
        : ["-c:v", "libx264", "-preset", "ultrafast", "-bf", "2", "-x264-params", "keyint=240:min-keyint=12:scenecut=0"];

    private async Task<string> SourceAsync(string encoder, string extension, CancellationToken cancellationToken)
    {
        var mkv = Path.Combine(_root, encoder + ".mkv");
        if (!File.Exists(mkv))
        {
            await FfmpegTool.RunAsync(cancellationToken, [
                "-f", "lavfi", "-i", $"testsrc2=size=320x180:rate=24:duration={DurationSeconds}",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={DurationSeconds}",
                .. VideoEncoding(encoder), "-pix_fmt", "yuv420p", "-force_key_frames", UnevenKeyframes,
                "-c:a", "aac", mkv]);
        }
        if (extension == ".mkv") return mkv;

        var other = Path.Combine(_root, encoder + extension);
        if (!File.Exists(other)) await FfmpegTool.RunAsync(cancellationToken, "-i", mkv, "-c", "copy", other);
        return other;
    }

    private static PlaybackDecisionVM CopyDecision() => new()
    {
        MediaItemId = Guid.NewGuid(),
        Decision = StreamingState.Remux,
        VideoStrategy = "DirectStream",
        AudioStrategy = "DirectStream",
        SourceAudioCodec = "aac",
        TargetAudioCodec = AudioCodec.Aac,
        SelectedVideoStreamIndex = 0,
        SelectedAudioStreamIndex = 1,
        SourceDurationSeconds = DurationSeconds,
    };

    private async Task<Dictionary<int, double>> CutAsync(string source, SegmentPlan plan, int startSegment, string folder, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, Path.GetFileName(source) + "-" + folder);
        Directory.CreateDirectory(directory);
        var segmentList = Path.Combine(directory, "segments.list");
        var arguments = FFmpegTranscodeService.BuildFFmpegArguments(source, Path.Combine(directory, "out_%d.part"), segmentList,
            CopyDecision(), new ServerSetting(), hardware: null, plan.LaunchAt(startSegment), TranscodeArgumentScenarios.PlainHost);
        await FfmpegTool.RunAsync(cancellationToken, [.. arguments]);
        FFmpegTranscodeService.PublishFinishedSegments(directory, segmentList);
        Directory.EnumerateFiles(directory, "*.part").Should().BeEmpty();

        var starts = new Dictionary<int, double>();
        foreach (var file in Directory.EnumerateFiles(directory, "out_*.ts"))
        {
            int index = int.Parse(Path.GetFileNameWithoutExtension(file)["out_".Length..], CultureInfo.InvariantCulture);
            starts[index] = await FirstVideoTimeAsync(file, cancellationToken);
        }
        return starts;
    }

    [Theory]
    [InlineData("libx264", ".mkv")]
    [InlineData("libx264", ".mp4")]
    [InlineData("libx265", ".mkv")]
    [InlineData("libx265", ".mp4")]
    public async Task The_keyframe_index_matches_what_ffprobe_reads_from_the_whole_file(string encoder, string extension)
    {
        Assert.SkipUnless(ToolsAvailable(encoder), SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await SourceAsync(encoder, extension, cancellationToken);

        var probed = await ProbedKeyframesAsync(source, cancellationToken);
        var indexed = KeyframeIndex.TryRead(source, 0);

        probed.Count.Should().BeGreaterThan(4);
        indexed.Should().NotBeNull();
        indexed!.Should().HaveCount(probed.Count);
        indexed.Zip(probed).Should().OnlyContain(pair => Math.Abs(pair.First - pair.Second) < 0.002);
    }

    [Theory]
    [InlineData("libx264", ".mkv")]
    [InlineData("libx264", ".mp4")]
    [InlineData("libx265", ".mkv")]
    [InlineData("libx265", ".mp4")]
    public async Task Every_segment_starts_on_its_planned_keyframe_and_a_restart_writes_the_same_segments(string encoder, string extension)
    {
        Assert.SkipUnless(ToolsAvailable(encoder), SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await SourceAsync(encoder, extension, cancellationToken);
        var keyframes = KeyframeIndex.TryRead(source, 0)!;
        var plan = SegmentPlan.FromKeyframes(keyframes, DurationSeconds)!;
        plan.Count.Should().BeGreaterThan(4);
        var plannedStarts = Enumerable.Range(0, plan.Count).Select(segment => segment == 0 ? keyframes[0] : plan.StartOf(segment)).ToArray();

        var whole = await CutAsync(source, plan, 0, "whole", cancellationToken);
        double shift = whole[0] - plannedStarts[0];

        whole.Should().HaveCount(plan.Count);
        whole.Should().OnlyContain(pair => Math.Abs(pair.Value - plannedStarts[pair.Key] - shift) < 0.002);

        foreach (int restartAt in new[] { 1, plan.Count - 2 })
        {
            var restart = await CutAsync(source, plan, restartAt, "restart-" + restartAt, cancellationToken);

            restart.Keys.Order().Should().Equal(Enumerable.Range(restartAt, plan.Count - restartAt));
            restart.Should().OnlyContain(pair => Math.Abs(pair.Value - whole[pair.Key]) < 0.002);
        }
    }
}
