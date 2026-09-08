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

    public static string WebVttFileName(Guid mediaPartId, Guid subtitleTrackId, string fingerprint) =>
        $"{mediaPartId}_{subtitleTrackId}_{fingerprint}.vtt";

    public static string TrackFilePattern(Guid mediaPartId, Guid subtitleTrackId) => $"{mediaPartId}_{subtitleTrackId}_*.vtt";

    public static string PartFilePattern(Guid mediaPartId) => $"{mediaPartId}_*.vtt";

    public static string CachePath(string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, string fingerprint) =>
        Path.Combine(CacheDirectory(transcodeTempDirectory), WebVttFileName(mediaPartId, subtitleTrackId, fingerprint));

    // Binds a cached VTT to the bytes it was made from. A source whose size or
    // mtime moved, or a track that landed on a different stream index after a
    // re-probe, produces a different name — so a stale entry is a cache miss by
    // construction rather than something a check has to remember to catch.
    public static string ComputeFingerprint(long sizeBytes, DateTime lastWriteUtc, int subtitleStreamIndex)
    {
        var seed = $"{sizeBytes}|{lastWriteUtc.Ticks}|{subtitleStreamIndex}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static string? FingerprintForSource(string sourceFilePath, int subtitleStreamIndex)
    {
        try
        {
            var info = new FileInfo(sourceFilePath);
            if (!info.Exists) return null;
            return ComputeFingerprint(info.Length, info.LastWriteTimeUtc, subtitleStreamIndex);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string AbsoluteMap(int subtitleStreamIndex) => $"0:{subtitleStreamIndex}";

    public static string SubtitleRelativeMap(int subtitleOrdinal) => $"0:s:{subtitleOrdinal}";

    public const string LegacySubtitleEncoding = "CP1252";

    public static bool IsAlreadyWebVtt(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".vtt", StringComparison.OrdinalIgnoreCase);

    // A sidecar has one stream and it is the subtitle, so there is nothing to map
    // and nothing to disable.
    public static List<string> BuildExternalArguments(string externalFilePath, string outputPath, string? characterEncoding)
    {
        var args = new List<string> { "-y" };
        if (!string.IsNullOrWhiteSpace(characterEncoding))
        {
            args.Add("-sub_charenc");
            args.Add(characterEncoding);
        }
        args.AddRange(["-i", externalFilePath, "-c:s", "webvtt", "-f", "webvtt", outputPath]);
        return args;
    }

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

    public bool HasValidCachedWebVtt(string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, SubtitleSource source)
    {
        if (string.IsNullOrWhiteSpace(transcodeTempDirectory)) return false;

        var fingerprint = FingerprintForSource(source.ContentPath, source.StreamIndex);
        return fingerprint != null && IsUsable(CachePath(transcodeTempDirectory, mediaPartId, subtitleTrackId, fingerprint));
    }

    public async Task<string?> GetOrExtractWebVttAsync(SubtitleSource source, string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.ContentPath) || !File.Exists(source.ContentPath)) return null;
        if (string.IsNullOrWhiteSpace(transcodeTempDirectory)) return null;

        var fingerprint = FingerprintForSource(source.ContentPath, source.StreamIndex);
        if (fingerprint == null) return null;

        var cachePath = CachePath(transcodeTempDirectory, mediaPartId, subtitleTrackId, fingerprint);
        if (IsUsable(cachePath)) return cachePath;

        var gate = _perKeyLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsUsable(cachePath)) return cachePath;

            var produced = await ExtractWebVttAsync(source, cachePath, mediaPartId, subtitleTrackId, cancellationToken);
            if (produced != null) RemoveSuperseded(transcodeTempDirectory, mediaPartId, subtitleTrackId, keepFileName: Path.GetFileName(produced));
            return produced;
        }
        finally
        {
            gate.Release();
        }
    }

    // Used when a subtitle arrives from somewhere other than the media file — a
    // provider download — and has to land in the store already converted.
    public async Task<bool> ConvertToWebVttAsync(string sourceFilePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath)) return false;

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(directory)) return false;
        Directory.CreateDirectory(directory);

        var stagingPath = Path.Combine(directory, $"{Guid.NewGuid():N}.vtt.tmp");
        var attempt = await ConvertExternalAsync(sourceFilePath, stagingPath, cancellationToken);

        if (!attempt.Succeeded)
        {
            TryDelete(stagingPath);
            return false;
        }

        try
        {
            File.Move(stagingPath, destinationPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not move the converted subtitle into {Destination}.", destinationPath);
            TryDelete(stagingPath);
            return false;
        }
    }

    public void PurgePart(string transcodeTempDirectory, Guid mediaPartId) =>
        DeleteMatching(transcodeTempDirectory, PartFilePattern(mediaPartId), keepFileName: null);

    public IReadOnlyCollection<Guid> ListCachedPartIds(string transcodeTempDirectory)
    {
        if (string.IsNullOrWhiteSpace(transcodeTempDirectory)) return Array.Empty<Guid>();

        var directory = CacheDirectory(transcodeTempDirectory);
        if (!Directory.Exists(directory)) return Array.Empty<Guid>();

        try
        {
            return Directory.EnumerateFiles(directory, "*.vtt")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(name => name.Split('_') is [var partId, _, _] && Guid.TryParse(partId, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list the cached subtitle directory {Directory}.", directory);
            return Array.Empty<Guid>();
        }
    }

    private void RemoveSuperseded(string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, string keepFileName) =>
        DeleteMatching(transcodeTempDirectory, TrackFilePattern(mediaPartId, subtitleTrackId), keepFileName);

    private void DeleteMatching(string transcodeTempDirectory, string pattern, string? keepFileName)
    {
        if (string.IsNullOrWhiteSpace(transcodeTempDirectory)) return;

        var directory = CacheDirectory(transcodeTempDirectory);
        if (!Directory.Exists(directory)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
            {
                if (keepFileName != null && string.Equals(Path.GetFileName(file), keepFileName, StringComparison.Ordinal)) continue;
                TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sweep cached subtitles matching {Pattern} in {Directory}.", pattern, directory);
        }
    }

    public async Task<string?> ExtractWebVttAsync(SubtitleSource source, string cachePath, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default)
    {
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (string.IsNullOrEmpty(cacheDirectory)) return null;
        Directory.CreateDirectory(cacheDirectory);

        var stagingPath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}.vtt.tmp");

        _logger.LogInformation("Subtitle extraction starting: part {PartId} track {TrackId}, {Kind}, source {Source}.",
            mediaPartId, subtitleTrackId, source.IsExternal ? "sidecar file" : "embedded stream", source.ContentPath);

        var startedAt = Stopwatch.GetTimestamp();
        var attempt = source.IsExternal
            ? await ConvertExternalAsync(source.ContentPath, stagingPath, cancellationToken)
            : await ExtractEmbeddedAsync(source, stagingPath, cancellationToken);

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

        _logger.LogInformation("Subtitle extraction finished: part {PartId} track {TrackId}, source {Source}, {ElapsedMs} ms, {Bytes} bytes, cached at {CachePath}.",
            mediaPartId, subtitleTrackId, source.ContentPath,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attempt.OutputBytes, cachePath);

        return cachePath;
    }

    private async Task<ExtractionAttempt> ExtractEmbeddedAsync(SubtitleSource source, string stagingPath, CancellationToken cancellationToken)
    {
        var attempt = await RunFfmpegAsync(
            BuildArguments(source.VideoFilePath, AbsoluteMap(source.StreamIndex), stagingPath),
            source.VideoFilePath, stagingPath, cancellationToken);

        if (attempt.ShouldRetry && source.Ordinal >= 0)
        {
            _logger.LogInformation("Absolute stream map 0:{StreamIndex} failed for {Source}; retrying as subtitle-relative 0:s:{Ordinal}.",
                source.StreamIndex, source.VideoFilePath, source.Ordinal);

            attempt = await RunFfmpegAsync(
                BuildArguments(source.VideoFilePath, SubtitleRelativeMap(source.Ordinal), stagingPath),
                source.VideoFilePath, stagingPath, cancellationToken);
        }

        return attempt;
    }

    // A sidecar is already a subtitle file: there is no stream to select, so the
    // work is a format conversion, and a .vtt needs not even that.
    private async Task<ExtractionAttempt> ConvertExternalAsync(string externalFilePath, string stagingPath, CancellationToken cancellationToken)
    {
        if (IsAlreadyWebVtt(externalFilePath))
        {
            try
            {
                File.Copy(externalFilePath, stagingPath, overwrite: true);
                var copied = new FileInfo(stagingPath).Length;
                return new ExtractionAttempt(true, copied > 0 ? 0 : 1, copied);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not copy the sidecar WebVTT {Path} into the cache.", externalFilePath);
                return ExtractionAttempt.DidNotRun;
            }
        }

        var attempt = await RunFfmpegAsync(
            BuildExternalArguments(externalFilePath, stagingPath, characterEncoding: null),
            externalFilePath, stagingPath, cancellationToken);

        // FFmpeg assumes UTF-8 and gives up on a subtitle file that is not. Legacy
        // .srt files are routinely Windows-1252, so a failed first pass is retried
        // as that rather than leaving the track permanently unusable.
        if (attempt.ShouldRetry)
        {
            _logger.LogInformation("Sidecar {Path} failed to convert as UTF-8; retrying as {Encoding}.", externalFilePath, LegacySubtitleEncoding);

            attempt = await RunFfmpegAsync(
                BuildExternalArguments(externalFilePath, stagingPath, LegacySubtitleEncoding),
                externalFilePath, stagingPath, cancellationToken);
        }

        return attempt;
    }

    private async Task<ExtractionAttempt> RunFfmpegAsync(IReadOnlyList<string> arguments, string sourceFilePath, string outputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _logger.LogInformation("Extracting subtitles from {Source}: ffmpeg {Arguments}",
            sourceFilePath, string.Join(" ", startInfo.ArgumentList));

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
                _logger.LogWarning("Subtitle extraction timed out after {Timeout}s for {Source}.",
                    ExtractionTimeout.TotalSeconds, sourceFilePath);
                return ExtractionAttempt.DidNotRun;
            }

            var bytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;

            if (process.ExitCode != 0 || bytes == 0)
            {
                _logger.LogWarning("Subtitle extraction failed for {Source} (exit {ExitCode}, {Bytes} bytes): {Error}",
                    sourceFilePath, process.ExitCode, bytes, await stderr);
                return new ExtractionAttempt(true, process.ExitCode, 0);
            }

            return new ExtractionAttempt(true, 0, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subtitle extraction threw for {Source}.", sourceFilePath);
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
