using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegSubtitleExtractionService : ISubtitleExtractionService
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<FFmpegSubtitleExtractionService> _logger;

    public FFmpegSubtitleExtractionService(ILogger<FFmpegSubtitleExtractionService> logger)
    {
        _logger = logger;
    }

    public static string WebVttFileName(Guid transcodeKey) => $"{transcodeKey}_subtitles.vtt";

    public static string AbsoluteMap(int subtitleStreamIndex) => $"0:{subtitleStreamIndex}";

    public static string SubtitleRelativeMap(int subtitleOrdinal) => $"0:s:{subtitleOrdinal}";

    public static List<string> BuildArguments(string sourceFilePath, string mapSpecifier, string outputPath) =>
    [
        "-y",
        "-i", sourceFilePath,
        "-map", mapSpecifier,
        "-c:s", "webvtt",
        "-f", "webvtt",
        outputPath,
    ];

    public async Task<string?> ExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string outputDirectory, Guid transcodeKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath)) return null;
        if (string.IsNullOrWhiteSpace(outputDirectory)) return null;

        Directory.CreateDirectory(outputDirectory);

        var fileName = WebVttFileName(transcodeKey);
        var outputPath = Path.Combine(outputDirectory, fileName);

        var attempt = await RunFfmpegAsync(sourceFilePath, AbsoluteMap(subtitleStreamIndex), subtitleStreamIndex, outputPath, cancellationToken);

        if (attempt.ShouldRetry && subtitleOrdinal >= 0)
        {
            _logger.LogInformation(
                "Absolute stream map 0:{StreamIndex} failed for {Source}; retrying as subtitle-relative 0:s:{Ordinal}.",
                subtitleStreamIndex, sourceFilePath, subtitleOrdinal);

            attempt = await RunFfmpegAsync(sourceFilePath, SubtitleRelativeMap(subtitleOrdinal), subtitleStreamIndex, outputPath, cancellationToken);
        }

        if (!attempt.Succeeded)
        {
            TryDelete(outputPath);
            return null;
        }

        _logger.LogInformation("Extracted subtitle stream {StreamIndex} of {Source} to {OutputPath} ({Bytes} bytes).",
            subtitleStreamIndex, sourceFilePath, outputPath, attempt.OutputBytes);

        return fileName;
    }

    private async Task<ExtractionAttempt> RunFfmpegAsync(string sourceFilePath, string mapSpecifier, int subtitleStreamIndex, string outputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in BuildArguments(sourceFilePath, mapSpecifier, outputPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        _logger.LogInformation("Extracting subtitle stream {StreamIndex} via {MapSpecifier}: ffmpeg {Arguments}",
            subtitleStreamIndex, mapSpecifier, string.Join(" ", startInfo.ArgumentList));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExtractionTimeout);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("Subtitle extraction timed out after {Timeout}s for {Source} via {MapSpecifier}.",
                    ExtractionTimeout.TotalSeconds, sourceFilePath, mapSpecifier);
                return ExtractionAttempt.DidNotRun;
            }

            var bytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;

            if (process.ExitCode != 0 || bytes == 0)
            {
                _logger.LogWarning("Subtitle extraction failed for {Source} via {MapSpecifier} (exit {ExitCode}, {Bytes} bytes): {Error}",
                    sourceFilePath, mapSpecifier, process.ExitCode, bytes, await stderr);
                return new ExtractionAttempt(true, process.ExitCode, 0);
            }

            return new ExtractionAttempt(true, 0, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subtitle extraction threw for {Source} via {MapSpecifier}.", sourceFilePath, mapSpecifier);
            return ExtractionAttempt.DidNotRun;
        }
    }

    public void RemoveWebVtt(string outputDirectory, Guid transcodeKey)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        TryDelete(Path.Combine(outputDirectory, WebVttFileName(transcodeKey)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    private readonly record struct ExtractionAttempt(bool Ran, int ExitCode, long OutputBytes)
    {
        public static ExtractionAttempt DidNotRun => new(false, -1, 0);

        public bool Succeeded => Ran && ExitCode == 0 && OutputBytes > 0;
        public bool ShouldRetry => Ran && ExitCode != 0;
    }
}
