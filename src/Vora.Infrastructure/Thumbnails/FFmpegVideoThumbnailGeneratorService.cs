using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Vora.Application.Thumbnails;

namespace Vora.Infrastructure.Thumbnails;

public class FFmpegVideoThumbnailGeneratorService : IVideoThumbnailGeneratorService
{
    private readonly ILogger<FFmpegVideoThumbnailGeneratorService> _logger;

    public FFmpegVideoThumbnailGeneratorService(ILogger<FFmpegVideoThumbnailGeneratorService> logger)
    {
        _logger = logger;
    }

    public async Task<TimeSpan?> ProbeDurationAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=nokey=1:noprint_wrappers=1");
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("ffprobe duration failed for {Input} with exit code {ExitCode}", inputPath, process.ExitCode);
            return null;
        }

        if (double.TryParse(stdout.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    public async Task<VideoThumbnailGenerationResult> GenerateAsync(
        VideoThumbnailGenerationParameters parameters,
        string finalSpritePath,
        string finalVttPath,
        CancellationToken cancellationToken = default)
    {
        if (parameters.IntervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(parameters), "IntervalSeconds must be positive");
        if (parameters.Width <= 0 || parameters.Height <= 0) throw new ArgumentOutOfRangeException(nameof(parameters), "Width and Height must be positive");
        if (parameters.SpriteColumns <= 0) throw new ArgumentOutOfRangeException(nameof(parameters), "SpriteColumns must be positive");

        var duration = await ProbeDurationAsync(parameters.InputPath, cancellationToken)
            ?? throw new InvalidOperationException($"Could not probe duration for {parameters.InputPath}");

        var spriteCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / parameters.IntervalSeconds));
        var spriteColumns = parameters.SpriteColumns;
        var spriteRows = (int)Math.Ceiling(spriteCount / (double)spriteColumns);

        var spriteDir = Path.GetDirectoryName(finalSpritePath)!;
        Directory.CreateDirectory(spriteDir);

        var tempSpritePath = finalSpritePath + ".tmp";
        var tempVttPath = finalVttPath + ".tmp";

        if (File.Exists(tempSpritePath)) File.Delete(tempSpritePath);
        if (File.Exists(tempVttPath)) File.Delete(tempVttPath);

        var filter = string.Create(CultureInfo.InvariantCulture,
            $"fps=1/{parameters.IntervalSeconds},scale={parameters.Width}:{parameters.Height}:force_original_aspect_ratio=decrease:flags=fast_bilinear,pad={parameters.Width}:{parameters.Height}:(ow-iw)/2:(oh-ih)/2:color=black,tile={spriteColumns}x{spriteRows}");

        var (ok, exitCode, stderrTail) = await RunSpriteProcessAsync(parameters, filter, tempSpritePath, parameters.UseHardwareDecode, cancellationToken);

        // Retry in software if the hardware decode pass failed on a codec/profile
        // NVDEC couldn't handle, so a GPU quirk never blocks a whole library's
        // thumbnails.
        if ((!ok || !File.Exists(tempSpritePath)) && parameters.UseHardwareDecode && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Hardware-accelerated sprite generation failed for {Input}; retrying in software.", parameters.InputPath);
            (ok, exitCode, stderrTail) = await RunSpriteProcessAsync(parameters, filter, tempSpritePath, useHardware: false, cancellationToken);
        }

        if (!ok || !File.Exists(tempSpritePath))
        {
            _logger.LogError("ffmpeg sprite generation failed for {Input}. ExitCode={ExitCode}. Stderr tail: {Stderr}",
                parameters.InputPath, exitCode, stderrTail);
            throw new InvalidOperationException($"ffmpeg sprite generation failed for {parameters.InputPath}");
        }

        var vtt = BuildVtt(spriteCount, spriteColumns, parameters.Width, parameters.Height, parameters.IntervalSeconds, Path.GetFileName(finalSpritePath));
        await File.WriteAllTextAsync(tempVttPath, vtt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        if (File.Exists(finalSpritePath)) File.Delete(finalSpritePath);
        if (File.Exists(finalVttPath)) File.Delete(finalVttPath);
        File.Move(tempSpritePath, finalSpritePath);
        File.Move(tempVttPath, finalVttPath);

        // Remove the pre-WebP JPEG sprite an earlier generation may have left behind
        // so a regenerated item doesn't keep both formats in its directory.
        var legacyJpeg = Path.Combine(Path.GetDirectoryName(finalSpritePath)!, "sprite.jpg");
        try
        {
            if (File.Exists(legacyJpeg)) File.Delete(legacyJpeg);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete legacy JPEG sprite {LegacyPath}", legacyJpeg);
        }

        return new VideoThumbnailGenerationResult
        {
            SpriteOutputPath = finalSpritePath,
            VttOutputPath = finalVttPath,
            SpriteCount = spriteCount,
            SpriteColumns = spriteColumns,
            SpriteRows = spriteRows,
            Width = parameters.Width,
            Height = parameters.Height,
            IntervalSeconds = parameters.IntervalSeconds,
            SourceDuration = duration
        };
    }

    private async Task<(bool Ok, int ExitCode, string StderrTail)> RunSpriteProcessAsync(
        VideoThumbnailGenerationParameters parameters, string filter, string tempSpritePath, bool useHardware, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-y");
        // -hwaccel is an input option (must precede -i). We encode the tiles on the
        // CPU side, so decode on the GPU and let frames download to system RAM.
        if (useHardware)
        {
            startInfo.ArgumentList.Add("-hwaccel");
            startInfo.ArgumentList.Add("auto");
            if (!string.IsNullOrWhiteSpace(parameters.HardwareDevice) && parameters.HardwareDevice != "Auto")
            {
                startInfo.ArgumentList.Add("-hwaccel_device");
                startInfo.ArgumentList.Add(parameters.HardwareDevice);
            }
        }
        // Only keyframes are needed. The sprite keeps a single frame per interval, so
        // fully decoding every B/P frame in between (and, under -hwaccel, copying them
        // off the GPU) is pure waste — the dominant cost on a full-length episode.
        // -skip_frame nokey makes the decoder emit keyframes only; the fps filter
        // still yields one tile per interval from the nearest keyframe, so the sprite
        // grid and the WebVTT cue mapping are unchanged. This is an input/decoder
        // option, so it must precede -i.
        startInfo.ArgumentList.Add("-skip_frame");
        startInfo.ArgumentList.Add("nokey");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(parameters.InputPath);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        // WebP is ~25-35% smaller than JPEG at the same visual quality. The stored
        // quality is an FFmpeg qscale (2 = best .. 31 = worst); map it to libwebp's
        // 0-100 quality (higher = better) so the one setting keeps its meaning across
        // both encoders.
        var webpQuality = Math.Clamp((int)Math.Round((31 - Math.Clamp(parameters.JpegQuality, 2, 31)) / 29.0 * 100), 0, 100);
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libwebp");
        startInfo.ArgumentList.Add("-quality");
        startInfo.ArgumentList.Add(webpQuality.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("picture");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("image2");
        startInfo.ArgumentList.Add(tempSpritePath);

        using var process = new Process { StartInfo = startInfo };
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();
        // WaitForExitAsync only stops awaiting on cancel — it leaves ffmpeg
        // running. Kill the process tree when the token fires so a cancelled
        // generation doesn't leave a full-file decode churning in the background.
        await using var kill = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode == 0, process.ExitCode, Tail(stderrBuilder.ToString(), 1000));
    }

    private static string BuildVtt(int spriteCount, int columns, int width, int height, int intervalSeconds, string spriteFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        for (var i = 0; i < spriteCount; i++)
        {
            var startSec = i * intervalSeconds;
            var endSec = (i + 1) * intervalSeconds;

            var col = i % columns;
            var row = i / columns;
            var x = col * width;
            var y = row * height;

            sb.Append(FormatCueTime(startSec));
            sb.Append(" --> ");
            sb.AppendLine(FormatCueTime(endSec));
            sb.Append(spriteFileName);
            sb.Append("#xywh=");
            sb.Append(x.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(y.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(width.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(height.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatCueTime(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.000");
    }

    private static string Tail(string text, int chars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= chars) return text;
        return text[^chars..];
    }
}
