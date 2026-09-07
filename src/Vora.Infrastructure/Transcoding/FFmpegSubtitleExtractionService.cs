using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegSubtitleExtractionService : ISubtitleExtractionService
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(180);

    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new();
    private readonly ILogger<FFmpegSubtitleExtractionService> _logger;

    public FFmpegSubtitleExtractionService(ILogger<FFmpegSubtitleExtractionService> logger)
    {
        _logger = logger;
    }

    public static string WebVttFileName(Guid mediaPartId, Guid subtitleTrackId) => $"{mediaPartId}_{subtitleTrackId}.vtt";

    public static string StagingFileName(Guid mediaPartId, Guid subtitleTrackId) => WebVttFileName(mediaPartId, subtitleTrackId) + ".tmp";

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

    public bool TryGetCachedWebVtt(string cacheDirectory, Guid mediaPartId, Guid subtitleTrackId, out string fileName)
    {
        fileName = WebVttFileName(mediaPartId, subtitleTrackId);
        if (string.IsNullOrWhiteSpace(cacheDirectory)) return false;

        try
        {
            var path = Path.Combine(cacheDirectory, fileName);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task BeginExtractionAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string cacheDirectory, Guid mediaPartId, Guid subtitleTrackId)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory)) return Task.CompletedTask;

        var key = Path.Combine(cacheDirectory, WebVttFileName(mediaPartId, subtitleTrackId));

        var work = _inFlight.GetOrAdd(key, cacheKey => new Lazy<Task>(() => Task.Run(async () =>
        {
            try
            {
                await ExtractWebVttAsync(sourceFilePath, subtitleStreamIndex, subtitleOrdinal, cacheDirectory, mediaPartId, subtitleTrackId);
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        })));

        return work.Value;
    }

    public async Task<string?> ExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string cacheDirectory, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath)) return null;
        if (string.IsNullOrWhiteSpace(cacheDirectory)) return null;

        Directory.CreateDirectory(cacheDirectory);

        var fileName = WebVttFileName(mediaPartId, subtitleTrackId);
        var cachePath = Path.Combine(cacheDirectory, fileName);
        var stagingPath = Path.Combine(cacheDirectory, StagingFileName(mediaPartId, subtitleTrackId));

        _logger.LogInformation("Extracting subtitle track {TrackId} (stream {StreamIndex}, ordinal {Ordinal}) of part {PartId} from {Source}.",
            subtitleTrackId, subtitleStreamIndex, subtitleOrdinal, mediaPartId, sourceFilePath);

        var startedAt = Stopwatch.GetTimestamp();
        var attempt = await RunFfmpegAsync(sourceFilePath, AbsoluteMap(subtitleStreamIndex), subtitleStreamIndex, stagingPath, cancellationToken);

        if (attempt.ShouldRetry && subtitleOrdinal >= 0)
        {
            _logger.LogInformation(
                "Absolute stream map 0:{StreamIndex} failed for {Source}; retrying as subtitle-relative 0:s:{Ordinal}.",
                subtitleStreamIndex, sourceFilePath, subtitleOrdinal);

            attempt = await RunFfmpegAsync(sourceFilePath, SubtitleRelativeMap(subtitleOrdinal), subtitleStreamIndex, stagingPath, cancellationToken);
        }

        if (!attempt.Succeeded)
        {
            TryDelete(stagingPath);
            return null;
        }

        try
        {
            File.Move(stagingPath, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not move the extracted subtitle into {CachePath}.", cachePath);
            TryDelete(stagingPath);
            return null;
        }

        _logger.LogInformation("Extracted subtitle track {TrackId} of part {PartId} to {CachePath} ({Bytes} bytes) in {Elapsed}.",
            subtitleTrackId, mediaPartId, cachePath, attempt.OutputBytes, Stopwatch.GetElapsedTime(startedAt));

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
