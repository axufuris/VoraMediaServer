using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegSubtitleExtractionService : ISubtitleExtractionService
{
    public const string CacheDirectoryName = "subcache";

    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(180);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perKeyLocks = new(StringComparer.Ordinal);
    private readonly ILogger<FFmpegSubtitleExtractionService> _logger;

    public FFmpegSubtitleExtractionService(ILogger<FFmpegSubtitleExtractionService> logger)
    {
        _logger = logger;
    }

    public static string CacheDirectory(string transcodeTempDirectory) =>
        Path.Combine(transcodeTempDirectory, CacheDirectoryName);

    public static string WebVttFileName(Guid mediaPartId, Guid subtitleTrackId) => $"{mediaPartId}_{subtitleTrackId}.vtt";

    public static string CachePath(string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId) =>
        Path.Combine(CacheDirectory(transcodeTempDirectory), WebVttFileName(mediaPartId, subtitleTrackId));

    public static string AbsoluteMap(int subtitleStreamIndex) => $"0:{subtitleStreamIndex}";

    public static string SubtitleRelativeMap(int subtitleOrdinal) => $"0:s:{subtitleOrdinal}";

    public static List<string> BuildArguments(string sourceFilePath, string mapSpecifier, string outputPath) =>
    [
        "-y",
        "-i", sourceFilePath,
        "-vn", "-an", "-dn",
        "-map", mapSpecifier,
        "-c:s", "webvtt",
        "-f", "webvtt",
        outputPath,
    ];

    public static bool IsUsable(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string?> GetOrExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath)) return null;
        if (string.IsNullOrWhiteSpace(transcodeTempDirectory)) return null;

        var cachePath = CachePath(transcodeTempDirectory, mediaPartId, subtitleTrackId);
        if (IsUsable(cachePath)) return cachePath;

        var gate = _perKeyLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsUsable(cachePath)) return cachePath;

            return await ExtractWebVttAsync(sourceFilePath, subtitleStreamIndex, subtitleOrdinal, cachePath, mediaPartId, subtitleTrackId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> ExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string cachePath, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default)
    {
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (string.IsNullOrEmpty(cacheDirectory)) return null;
        Directory.CreateDirectory(cacheDirectory);

        var stagingPath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}.vtt.tmp");

        _logger.LogInformation("Subtitle extraction starting: part {PartId} track {TrackId}, stream index {StreamIndex}, source {Source}.",
            mediaPartId, subtitleTrackId, subtitleStreamIndex, sourceFilePath);

        var startedAt = Stopwatch.GetTimestamp();
        var attempt = await RunFfmpegAsync(sourceFilePath, AbsoluteMap(subtitleStreamIndex), subtitleStreamIndex, stagingPath, cancellationToken);

        if (attempt.ShouldRetry && subtitleOrdinal >= 0)
        {
            _logger.LogInformation("Absolute stream map 0:{StreamIndex} failed for {Source}; retrying as subtitle-relative 0:s:{Ordinal}.",
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

        _logger.LogInformation("Subtitle extraction finished: part {PartId} track {TrackId}, stream index {StreamIndex}, source {Source}, {ElapsedMs} ms, {Bytes} bytes, cached at {CachePath}.",
            mediaPartId, subtitleTrackId, subtitleStreamIndex, sourceFilePath,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attempt.OutputBytes, cachePath);

        return cachePath;
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
