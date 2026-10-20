using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Analysis;

public static partial class CropDetectOutput
{
    [GeneratedRegex(@"crop=(\d+):(\d+):(\d+):(\d+)")]
    private static partial Regex CropPattern();

    public static CropBox? LastBox(string stderr)
    {
        var matches = CropPattern().Matches(stderr);
        if (matches.Count == 0) return null;
        var last = matches[^1].Groups;
        return new CropBox(Number(last[3]), Number(last[4]), Number(last[1]), Number(last[2]));
    }

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);
}

public class FFmpegPictureAreaDetector : IPictureAreaDetector
{
    public const int Samples = 10;
    private const double FirstSample = 0.08;
    private const double LastSample = 0.92;
    private const int KeyframesPerSample = 5;
    private static readonly TimeSpan SampleTimeout = TimeSpan.FromSeconds(60);

    private readonly ILogger<FFmpegPictureAreaDetector> _logger;

    public FFmpegPictureAreaDetector(ILogger<FFmpegPictureAreaDetector> logger)
    {
        _logger = logger;
    }

    public static IReadOnlyList<double> SampleTimes(double durationSeconds) =>
        Enumerable.Range(0, Samples)
            .Select(index => durationSeconds * (FirstSample + (LastSample - FirstSample) * index / (Samples - 1)))
            .ToList();

    public static IReadOnlyList<string> Arguments(PictureAreaTarget target, double seconds) =>
    [
        "-hide_banner",
        "-nostats",
        "-skip_frame", "nokey",
        "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-i", target.FilePath,
        "-map", $"0:{target.StreamIndex}",
        "-frames:v", KeyframesPerSample.ToString(CultureInfo.InvariantCulture),
        "-vf", "cropdetect=limit=0.094:round=2:reset=0",
        "-an", "-sn", "-dn",
        "-f", "null", "-",
    ];

    public async Task<PictureCrop?> DetectAsync(PictureAreaTarget target, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(target.FilePath)) return null;

        var boxes = new List<CropBox>();
        var sampled = 0;
        foreach (var seconds in SampleTimes(target.DurationSeconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stderr = await RunAsync(target, seconds, cancellationToken);
            if (stderr == null) continue;
            sampled++;
            if (CropDetectOutput.LastBox(stderr) is CropBox box) boxes.Add(box);
        }

        return sampled >= PictureCrop.MinimumSamples ? PictureCrop.FromBoxes(target.Width, target.Height, boxes) : null;
    }

    private async Task<string?> RunAsync(PictureAreaTarget target, double seconds, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in Arguments(target, seconds)) startInfo.ArgumentList.Add(argument);

        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data == null) return;
            lock (stderr) stderr.AppendLine(line.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start ffmpeg to look for black bars in {FilePath}.", target.FilePath);
            return null;
        }

        process.BeginErrorReadLine();
        var exited = await process.WaitForExitWithTimeoutAsync(SampleTimeout, _logger, cancellationToken);
        if (!exited || process.ExitCode != 0) return null;
        lock (stderr) return stderr.ToString();
    }
}
