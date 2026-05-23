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
            Arguments = $"-v error -show_entries format=duration -of default=nokey=1:noprint_wrappers=1 \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(parameters.InputPath);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-qscale:v");
        startInfo.ArgumentList.Add(parameters.JpegQuality.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        startInfo.ArgumentList.Add(tempSpritePath);

        using var process = new Process { StartInfo = startInfo };
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(tempSpritePath))
        {
            _logger.LogError("ffmpeg sprite generation failed for {Input}. ExitCode={ExitCode}. Stderr tail: {Stderr}",
                parameters.InputPath, process.ExitCode, Tail(stderrBuilder.ToString(), 1000));
            throw new InvalidOperationException($"ffmpeg sprite generation failed for {parameters.InputPath}");
        }

        var vtt = BuildVtt(spriteCount, spriteColumns, parameters.Width, parameters.Height, parameters.IntervalSeconds, Path.GetFileName(finalSpritePath));
        await File.WriteAllTextAsync(tempVttPath, vtt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        if (File.Exists(finalSpritePath)) File.Delete(finalSpritePath);
        if (File.Exists(finalVttPath)) File.Delete(finalVttPath);
        File.Move(tempSpritePath, finalSpritePath);
        File.Move(tempVttPath, finalVttPath);

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
