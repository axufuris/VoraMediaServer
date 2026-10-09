using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegAudioTranscodeService : IAudioTranscodeService
{
    public const string CacheDirectoryName = "audiocache";
    public const long MaxCacheBytes = 1024L * 1024 * 1024;
    public const string PartialExtension = ".tmp";

    public static readonly TimeSpan RecentlyUsed = TimeSpan.FromHours(1);
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);

    private static readonly TimeSpan TranscodeTimeout = TimeSpan.FromMinutes(10);
    private static readonly object PruneLock = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new(StringComparer.Ordinal);
    private readonly ILogger<FFmpegAudioTranscodeService> _logger;

    public FFmpegAudioTranscodeService(ILogger<FFmpegAudioTranscodeService> logger)
    {
        _logger = logger;
    }

    public static string CacheDirectory(string transcodeTempDirectory) =>
        Path.Combine(transcodeTempDirectory, CacheDirectoryName);

    public static string CacheFileName(Guid trackId, int bitrateKbps, string targetCodec, string sourceFilePath, long sourceSizeBytes, DateTime sourceLastWriteUtc)
    {
        var seed = $"{sourceFilePath}|{sourceSizeBytes}|{sourceLastWriteUtc.Ticks}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..16].ToLowerInvariant();
        return $"{trackId:N}_{bitrateKbps}k_{fingerprint}.{ResolveOutput(NormalizeCodec(targetCodec)).Extension}";
    }

    public string ResolveContentType(string targetCodec)
    {
        return NormalizeCodec(targetCodec) switch
        {
            "mp3" => "audio/mpeg",
            "aac" => "audio/aac",
            "opus" => "audio/opus",
            _ => "audio/mpeg"
        };
    }

    public async Task<string?> GetTranscodedFileAsync(Guid trackId, string sourceFilePath, int bitrateKbps, string targetCodec, string transcodeTempDirectory, CancellationToken cancellationToken)
    {
        var source = new FileInfo(sourceFilePath);
        if (!source.Exists) return null;

        var codec = NormalizeCodec(targetCodec);
        var directory = CacheDirectory(transcodeTempDirectory);
        var path = Path.GetFullPath(Path.Combine(directory, CacheFileName(trackId, bitrateKbps, codec, sourceFilePath, source.Length, source.LastWriteTimeUtc)));
        if (File.Exists(path))
        {
            MarkUsed(path, DateTime.UtcNow);
            return path;
        }

        var transcode = _inFlight.GetOrAdd(path, key => new Lazy<Task<string?>>(() => TranscodeOnceAsync(sourceFilePath, directory, key, bitrateKbps, codec)));
        return await transcode.Value.WaitAsync(cancellationToken);
    }

    public static void PruneCache(string directory, long maxBytes, string? keepPath, DateTime nowUtc)
    {
        if (!Directory.Exists(directory)) return;

        lock (PruneLock)
        {
            var files = new DirectoryInfo(directory).GetFiles();
            foreach (var abandoned in files.Where(f => f.Extension == PartialExtension && f.LastWriteTimeUtc < nowUtc - AbandonedAfter))
            {
                TryDelete(abandoned.FullName);
            }

            var keep = keepPath == null ? null : Path.GetFullPath(keepPath);
            var cached = files.Where(f => f.Extension != PartialExtension).OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = cached.Sum(f => f.Length);
            foreach (var file in cached)
            {
                if (total <= maxBytes) break;
                if (string.Equals(file.FullName, keep, StringComparison.Ordinal)) continue;
                if (TryDelete(file.FullName)) total -= file.Length;
            }
        }
    }

    private async Task<string?> TranscodeOnceAsync(string sourceFilePath, string directory, string path, int bitrateKbps, string codec)
    {
        try
        {
            return await TranscodeAsync(sourceFilePath, directory, path, bitrateKbps, codec);
        }
        finally
        {
            _inFlight.TryRemove(path, out _);
        }
    }

    private async Task<string?> TranscodeAsync(string sourceFilePath, string directory, string path, int bitrateKbps, string codec)
    {
        var partial = $"{path}.{Guid.NewGuid():N}{PartialExtension}";
        var output = ResolveOutput(codec);

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", sourceFilePath, "-vn", "-c:a", output.Encoder, "-b:a", $"{bitrateKbps}k", "-f", output.Format, partial })
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            Directory.CreateDirectory(directory);
            using var timeout = new CancellationTokenSource(TranscodeTimeout);
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start FFmpeg for audio transcode of {SourceFilePath}.", sourceFilePath);
                return null;
            }

            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                Stop(process, sourceFilePath);
                _logger.LogWarning("Audio transcode of {SourceFilePath} took longer than {Timeout} and was stopped.", sourceFilePath, TranscodeTimeout);
                return null;
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg exited with code {ExitCode} transcoding {SourceFilePath}: {Errors}", process.ExitCode, sourceFilePath, await errors);
                return null;
            }

            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            _logger.LogWarning(ex, "Audio transcode of {SourceFilePath} failed.", sourceFilePath);
            return null;
        }
        finally
        {
            TryDelete(partial);
        }

        PruneCache(directory, MaxCacheBytes, path, DateTime.UtcNow);
        return path;
    }

    private void Stop(Process process, string sourceFilePath)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to terminate FFmpeg audio transcode for {SourceFilePath}.", sourceFilePath);
        }
    }

    private static void MarkUsed(string path, DateTime nowUtc)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(path) < nowUtc - RecentlyUsed) File.SetLastWriteTimeUtc(path, nowUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NormalizeCodec(string targetCodec)
    {
        if (string.IsNullOrWhiteSpace(targetCodec)) return "mp3";
        return targetCodec.Trim().ToLowerInvariant();
    }

    private static (string Encoder, string Format, string Extension) ResolveOutput(string codec)
    {
        return codec switch
        {
            "aac" => ("aac", "adts", "aac"),
            "opus" => ("libopus", "opus", "opus"),
            _ => ("libmp3lame", "mp3", "mp3")
        };
    }
}
