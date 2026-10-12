using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegTranscodeService : ITranscodeService
{
    private const int SegmentSeconds = 4;
    private const double FallbackDurationSeconds = 4 * 60 * 60;

    private static readonly ConcurrentDictionary<Guid, TranscodeProcess> _activeTranscodes = new();
    private static readonly ConcurrentDictionary<Guid, TranscodeContext> _transcodeContexts = new();
    private static readonly SemaphoreSlim _transcodeLock = new(1, 1);
    private static int _launchCounter;

    private readonly IServiceProvider _serviceProvider;
    private readonly IHardwareCapabilityService _hardware;
    private readonly ILogger<FFmpegTranscodeService> _logger;

    public FFmpegTranscodeService(IServiceProvider serviceProvider, IHardwareCapabilityService hardware, ILogger<FFmpegTranscodeService> logger)
    {
        _serviceProvider = serviceProvider;
        _hardware = hardware;
        _logger = logger;
    }

    public async Task<string> StartTranscodeSessionAsync(string sourceFilePath, string outputDirectory, PlaybackDecisionVM decision, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var settings = await settingsRepo.GetSettingsAsync();

        if (settings.DisableVideoTranscoding && decision.VideoStrategy == "Transcode")
        {
            throw new InvalidOperationException("Video stream transcoding is disabled on this server.");
        }

        var targetDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? outputDirectory : settings.TranscoderTempDirectory;
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        var outputFileName = $"{decision.MediaItemId}.m3u8";
        var outputPath = Path.Combine(targetDir, outputFileName);

        var accelerator = settings.UseHardwareAcceleration
            ? await _hardware.ResolveAsync(settings.HardwareAccelerationType, settings.HardwareTranscodingDevice, cancellationToken)
            : null;

        await _transcodeLock.WaitAsync(cancellationToken);
        try
        {
            int currentGpu = _activeTranscodes.Values.Count(x => x.IsGpu);
            int currentCpu = _activeTranscodes.Values.Count(x => !x.IsGpu);
            bool attemptHwAccel = accelerator is not null && settings.UseHardwareEncoding;
            bool willUseGpu = attemptHwAccel && (settings.MaxGpuTranscodes == 0 || currentGpu < settings.MaxGpuTranscodes);

            if (willUseGpu)
            {
                if (settings.MaxGpuTranscodes > 0 && currentGpu >= settings.MaxGpuTranscodes && !_activeTranscodes.ContainsKey(decision.MediaItemId))
                    throw new InvalidOperationException("Maximum GPU transcodes reached.");
            }
            else
            {
                if (settings.MaxCpuTranscodes > 0 && currentCpu >= settings.MaxCpuTranscodes && !_activeTranscodes.ContainsKey(decision.MediaItemId))
                    throw new InvalidOperationException("Maximum CPU transcodes reached.");
            }

            await StopProcessAndCleanFilesAsync(decision.MediaItemId, targetDir);

            var durationSeconds = decision.SourceDurationSeconds > 0 ? decision.SourceDurationSeconds : FallbackDurationSeconds;
            if (decision.SourceDurationSeconds <= 0)
            {
                _logger.LogWarning("Source duration unknown for {MediaItemId}; using {Fallback}s placeholder. Re-analyze the media to populate Duration.",
                    decision.MediaItemId, FallbackDurationSeconds);
            }

            var rawStart = Math.Max(0, decision.StartPositionSeconds);
            var startSegment = (int)Math.Floor(rawStart / SegmentSeconds);
            var alignedStart = startSegment * (double)SegmentSeconds;

            WriteVodPlaylist(outputPath, decision.MediaItemId, durationSeconds, alignedStart);

            var hardware = accelerator is null ? null : new TranscodeHardware(accelerator, Encode: willUseGpu);
            var ctx = new TranscodeContext(sourceFilePath, decision, targetDir, durationSeconds, startSegment, hardware);
            _transcodeContexts[decision.MediaItemId] = ctx;

            await LaunchFFmpegAsync(decision.MediaItemId, startSegment, alignedStart, settings);
        }
        finally
        {
            _transcodeLock.Release();
        }

        var firstSegmentIndex = Math.Max(0, (int)Math.Floor(Math.Max(0, decision.StartPositionSeconds) / SegmentSeconds));
        await WaitForSegmentSealedAsync(decision.MediaItemId, firstSegmentIndex, targetDir, TimeSpan.FromSeconds(30), cancellationToken);
        return outputPath;
    }

    public async Task<bool> EnsureSegmentAvailableAsync(Guid mediaItemId, int segmentIndex, CancellationToken cancellationToken = default)
    {
        if (!_transcodeContexts.TryGetValue(mediaItemId, out var ctx))
        {
            return false;
        }

        var segmentPath = Path.Combine(ctx.TargetDir, $"{mediaItemId}_{segmentIndex}.ts");

        // If the segment file is on disk AND sealed (FFmpeg has moved on),
        // serve it. The seal check is critical — if the file exists but
        // FFmpeg is mid-write, ASP.NET's Results.File picks up the current
        // size as Content-Length, then more bytes get appended while
        // streaming, producing "Response Content-Length mismatch: too many
        // bytes written" and a corrupt segment for the player.
        if (await IsSegmentSealedAsync(mediaItemId, segmentIndex, ctx.TargetDir, cancellationToken))
        {
            return true;
        }

        // What range is the current FFmpeg encoder targeting? It started at
        // ctx.CurrentStartSegment and is producing segments sequentially.
        // The leading edge on disk tells us where it's gotten to.
        int currentStart = ctx.CurrentStartSegment;
        int leadingEdge = ComputeLeadingEdge(mediaItemId, ctx.TargetDir);
        bool hasActiveProcess = IsEncoding(mediaItemId);

        // If the requested segment is within the range the current encoder
        // will reach soon (start ≤ requested ≤ start+60, ≈4min of buffer),
        // just wait. Don't relaunch — multiple concurrent requests for
        // adjacent missing segments must not each trigger a relaunch.
        bool inEncodingPath = hasActiveProcess && segmentIndex >= currentStart && segmentIndex <= currentStart + 60;
        if (inEncodingPath)
        {
            if (await WaitForSegmentSealedAsync(mediaItemId, segmentIndex, ctx.TargetDir, TimeSpan.FromSeconds(15), cancellationToken))
            {
                return true;
            }
        }

        // CRITICAL: protect resume-from-position from the player's initial
        // pre-seek probe. When the user resumes at 4:17 (segment 65),
        // FFmpeg launches at segment 65. Player loads the m3u8 and —
        // before its scheduled seekTo(260000) resolves — requests segment
        // 0 first as part of its initial buffer probe / manifest validate.
        // The naive seek-restart on backward requests would kill the
        // correctly-positioned FFmpeg and relaunch from 0, then again
        // from 65 when seekTo resolves, thrashing the encoder forever.
        //
        // Fix: until ANY segment has actually been served to the client
        // from this transcode context, treat backward requests as initial
        // probes and 404 them. The player will fail that fetch, process
        // its queued seekTo, and re-request the correct segment around
        // currentStart, which by then will be sealed. Once we've served
        // a segment, the player has begun real playback and any future
        // backward request is a legitimate user seek that deserves a
        // proper restart.
        bool isBackwardRequest = segmentIndex < currentStart - 5;
        if (isBackwardRequest && ctx.HighestServedSegment < 0)
        {
            _logger.LogInformation("Suppressing pre-seek-probe restart for {MediaItemId} segment={SegmentIndex} currentStart={CurrentStart} (no segments served yet)",
                mediaItemId, segmentIndex, currentStart);
            return false;
        }

        // Far-forward seek (or genuine backward seek into untranscoded
        // region): kill the current encoder and relaunch FFmpeg seeking
        // to the requested segment's start time. This is how Plex/
        // Jellyfin handle seeks past the current encoding position.
        _logger.LogInformation("Seek-restart triggered for {MediaItemId} segment={SegmentIndex} currentStart={CurrentStart} leadingEdge={LeadingEdge}",
            mediaItemId, segmentIndex, currentStart, leadingEdge);

        await RelaunchAtSegmentAsync(mediaItemId, segmentIndex, cancellationToken);

        return await WaitForSegmentSealedAsync(mediaItemId, segmentIndex, ctx.TargetDir, TimeSpan.FromSeconds(30), cancellationToken);
    }

    // A segment is "sealed" (safe to serve) when FFmpeg has either moved
    // on to the next segment (the next .ts file exists) or finished the
    // movie entirely (last segment, no successor). Until then the size on
    // disk is still growing.
    private static async Task<bool> IsSegmentSealedAsync(Guid mediaItemId, int segmentIndex, string targetDir, CancellationToken ct)
    {
        var segmentPath = Path.Combine(targetDir, $"{mediaItemId}_{segmentIndex}.ts");
        var nextSegmentPath = Path.Combine(targetDir, $"{mediaItemId}_{segmentIndex + 1}.ts");

        if (!File.Exists(segmentPath)) return false;

        if (File.Exists(nextSegmentPath))
        {
            return true;
        }

        // Successor not yet on disk — confirm via size stability. Read the
        // file size twice with a small gap; if it hasn't grown, FFmpeg is
        // done with it (we're at the end of the movie or between writes).
        try
        {
            long sizeA = new FileInfo(segmentPath).Length;
            if (sizeA <= 0) return false;
            await Task.Delay(250, ct);
            if (File.Exists(nextSegmentPath)) return true;
            long sizeB = new FileInfo(segmentPath).Length;
            return sizeA == sizeB;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> WaitForSegmentSealedAsync(Guid mediaItemId, int segmentIndex, string targetDir, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) return false;
            if (await IsSegmentSealedAsync(mediaItemId, segmentIndex, targetDir, ct)) return true;
            if (!IsEncoding(mediaItemId))
            {
                // FFmpeg exited — give the OS a tick to flush, check once
                // more, then fail. Without this we'd race the buffer flush
                // on the very last segment of the movie.
                await Task.Delay(200, ct);
                return await IsSegmentSealedAsync(mediaItemId, segmentIndex, targetDir, ct);
            }
            await Task.Delay(150, ct);
        }
        return false;
    }

    public async Task StopTranscodeSessionAsync(Guid mediaItemId)
    {
        await _transcodeLock.WaitAsync();
        try
        {
            _transcodeContexts.TryRemove(mediaItemId, out var ctx);
            var targetDir = ctx?.TargetDir;
            if (string.IsNullOrEmpty(targetDir))
            {
                using var scope = _serviceProvider.CreateScope();
                var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
                var settings = await settingsRepo.GetSettingsAsync();
                targetDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory)
                    ? "/transcode"
                    : settings.TranscoderTempDirectory;
            }
            await StopProcessAndCleanFilesAsync(mediaItemId, targetDir);
        }
        finally
        {
            _transcodeLock.Release();
        }
    }

    public int GetActiveTranscodeCount() => _activeTranscodes.Count;
    public bool IsMediaTranscoding(Guid mediaItemId) => _activeTranscodes.ContainsKey(mediaItemId);

    public void NotifySegmentServed(Guid mediaItemId, int segmentIndex)
    {
        if (_transcodeContexts.TryGetValue(mediaItemId, out var ctx))
        {
            ctx.LastActivityAt = DateTime.UtcNow;
            if (segmentIndex > ctx.HighestServedSegment)
            {
                ctx.HighestServedSegment = segmentIndex;
            }
        }
    }

    public void TouchSession(Guid mediaItemId)
    {
        if (_transcodeContexts.TryGetValue(mediaItemId, out var ctx))
        {
            ctx.LastActivityAt = DateTime.UtcNow;
        }
    }

    public async Task EvictIdleSessionsAsync(TimeSpan maxIdle)
    {
        var cutoff = DateTime.UtcNow - maxIdle;
        var stale = _transcodeContexts
            .Where(kv => kv.Value.LastActivityAt < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var mediaItemId in stale)
        {
            _logger.LogInformation("Evicting idle transcode session {MediaItemId} (no activity since cutoff).", mediaItemId);
            await StopTranscodeSessionAsync(mediaItemId);
        }
    }

    public async Task ReapOrphanedTranscodeFilesAsync(string targetDir, TimeSpan maxAge)
    {
        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir)) return;
        var cutoff = DateTime.UtcNow - maxAge;

        await _transcodeLock.WaitAsync();
        try
        {
            var playlists = Directory.EnumerateFiles(targetDir, "*.m3u8").ToList();
            foreach (var playlist in playlists)
            {
                var name = Path.GetFileNameWithoutExtension(playlist);
                if (name.StartsWith("_ffmpeg_")) continue;
                if (!Guid.TryParse(name, out var mediaItemId)) continue;
                if (_transcodeContexts.ContainsKey(mediaItemId) || _activeTranscodes.ContainsKey(mediaItemId)) continue;

                if (GetNewestArtifactWriteTimeUtc(targetDir, mediaItemId, playlist) > cutoff) continue;

                _logger.LogInformation("Reaping orphaned transcode artifacts for {MediaItemId} in {Dir}.", mediaItemId, targetDir);
                await StopProcessAndCleanFilesAsync(mediaItemId, targetDir);
            }
        }
        finally
        {
            _transcodeLock.Release();
        }
    }

    private static DateTime GetNewestArtifactWriteTimeUtc(string targetDir, Guid mediaItemId, string playlistPath)
    {
        var newest = File.GetLastWriteTimeUtc(playlistPath);
        foreach (var f in Directory.EnumerateFiles(targetDir, $"{mediaItemId}_*.ts"))
        {
            var written = File.GetLastWriteTimeUtc(f);
            if (written > newest) newest = written;
        }
        return newest;
    }

    private async Task RelaunchAtSegmentAsync(Guid mediaItemId, int newStartSegment, CancellationToken ct)
    {
        await _transcodeLock.WaitAsync(ct);
        try
        {
            if (!_transcodeContexts.TryGetValue(mediaItemId, out var ctx))
            {
                throw new InvalidOperationException("No transcode context to relaunch.");
            }

            // If another concurrent request already relaunched at or before
            // this segment, just let that run. Avoids thrash when the player
            // fires multiple parallel chunk requests after a seek.
            if (ctx.CurrentStartSegment == newStartSegment && _activeTranscodes.ContainsKey(mediaItemId))
            {
                return;
            }

            // Kill the existing process but DO NOT wipe existing .ts files —
            // they're already-encoded ranges the user may seek back to.
            await KillProcessOnlyAsync(mediaItemId);

            var settings = await LoadSettingsAsync();
            var alignedStart = newStartSegment * (double)SegmentSeconds;
            ctx.CurrentStartSegment = newStartSegment;
            ctx.LaunchedAt = DateTime.UtcNow;
            ctx.LastActivityAt = DateTime.UtcNow;
            await LaunchFFmpegAsync(mediaItemId, newStartSegment, alignedStart, settings);
        }
        finally
        {
            _transcodeLock.Release();
        }
    }

    private async Task StopProcessAndCleanFilesAsync(Guid mediaItemId, string? targetDir)
    {
        await KillProcessOnlyAsync(mediaItemId);

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir)) return;

        try
        {
            var playlist = Path.Combine(targetDir, $"{mediaItemId}.m3u8");
            if (File.Exists(playlist))
            {
                try { File.Delete(playlist); } catch { /* keep going */ }
            }
            var ffmpegInternal = Path.Combine(targetDir, $"_ffmpeg_{mediaItemId}.m3u8");
            if (File.Exists(ffmpegInternal))
            {
                try { File.Delete(ffmpegInternal); } catch { /* keep going */ }
            }
            // FFmpeg HLS muxer with +temp_file writes to <name>.tmp first then renames
            var ffmpegInternalTmp = Path.Combine(targetDir, $"_ffmpeg_{mediaItemId}.m3u8.tmp");
            if (File.Exists(ffmpegInternalTmp))
            {
                try { File.Delete(ffmpegInternalTmp); } catch { /* keep going */ }
            }
            var segmentPrefix = $"{mediaItemId}_";
            foreach (var f in Directory.EnumerateFiles(targetDir, $"{segmentPrefix}*.ts"))
            {
                try { File.Delete(f); } catch { /* keep going */ }
            }
            foreach (var f in Directory.EnumerateFiles(targetDir, $"{segmentPrefix}*.ts.tmp"))
            {
                try { File.Delete(f); } catch { /* keep going */ }
            }
            foreach (var f in Directory.EnumerateFiles(targetDir, $"{segmentPrefix}*.m4s"))
            {
                try { File.Delete(f); } catch { /* keep going */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to wipe transcode output for {MediaItemId} in {Dir}", mediaItemId, targetDir);
        }
    }

    private Task KillProcessOnlyAsync(Guid mediaItemId)
    {
        if (_activeTranscodes.TryRemove(mediaItemId, out var entry))
        {
            entry.Stopping = true;
            try
            {
                if (!entry.Process.HasExited)
                {
                    entry.Process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill FFmpeg process for {MediaItemId}.", mediaItemId);
            }
            finally
            {
                entry.Process.Dispose();
            }
        }
        return Task.CompletedTask;
    }

    private static int ComputeLeadingEdge(Guid mediaItemId, string targetDir)
    {
        if (!Directory.Exists(targetDir)) return -1;
        int leadingEdge = -1;
        var prefix = $"{mediaItemId}_";
        try
        {
            foreach (var f in Directory.EnumerateFiles(targetDir, $"{prefix}*.ts"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.Length <= prefix.Length) continue;
                var idxStr = name[prefix.Length..];
                if (int.TryParse(idxStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                {
                    if (idx > leadingEdge) leadingEdge = idx;
                }
            }
        }
        catch
        {
            return leadingEdge;
        }
        return leadingEdge;
    }

    private static void WriteVodPlaylist(string outputPath, Guid mediaItemId, double durationSeconds, double startOffsetSeconds)
    {
        var totalSegments = (int)Math.Ceiling(durationSeconds / SegmentSeconds);
        if (totalSegments < 1) totalSegments = 1;
        var lastSegmentDuration = durationSeconds - (totalSegments - 1) * SegmentSeconds;
        if (lastSegmentDuration <= 0.01) lastSegmentDuration = SegmentSeconds;

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{SegmentSeconds}");
        sb.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");
        sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
        sb.AppendLine("#EXT-X-INDEPENDENT-SEGMENTS");
        // EXT-X-START tells the HLS player to begin playback at this
        // timeline offset. Without it, the player starts at position 0
        // and requests segment _0.ts first — which hangs forever when
        // FFmpeg launched at a non-zero startSegment (the user clicked
        // Resume). With it, hls.js / ExoPlayer fetch the segment at
        // startOffsetSeconds directly and skip the segment-0 probe.
        // PRECISE=YES means the player should not snap to a nearby
        // keyframe — our segments are already keyframe-aligned by
        // force_key_frames so PRECISE matches the actual content.
        if (startOffsetSeconds > 0.0)
        {
            sb.AppendLine($"#EXT-X-START:TIME-OFFSET={startOffsetSeconds.ToString("0.000", CultureInfo.InvariantCulture)},PRECISE=YES");
        }
        for (int i = 0; i < totalSegments; i++)
        {
            double dur = i == totalSegments - 1 ? lastSegmentDuration : SegmentSeconds;
            sb.AppendLine($"#EXTINF:{dur.ToString("0.000", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"{mediaItemId}_{i}.ts");
        }
        sb.AppendLine("#EXT-X-ENDLIST");
        File.WriteAllText(outputPath, sb.ToString());
    }

    private async Task<ServerSetting> LoadSettingsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>().GetSettingsAsync();
    }

    private static bool IsEncoding(Guid mediaItemId) =>
        _activeTranscodes.ContainsKey(mediaItemId)
        || (_transcodeContexts.TryGetValue(mediaItemId, out var ctx) && ctx.RetryingOnCpu);

    private Task LaunchFFmpegAsync(Guid mediaItemId, int startSegment, double alignedStartSeconds, ServerSetting settings)
    {
        if (!_transcodeContexts.TryGetValue(mediaItemId, out var ctx))
        {
            throw new InvalidOperationException("No transcode context to launch.");
        }

        var segmentPattern = Path.Combine(ctx.TargetDir, $"{mediaItemId}_%d.ts");
        var internalPlaylist = Path.Combine(ctx.TargetDir, $"_ffmpeg_{mediaItemId}.m3u8");
        var hardware = ctx.Hardware;
        var arguments = BuildFFmpegArguments(ctx.SourceFile, segmentPattern, internalPlaylist, ctx.Decision, settings, hardware, startSegment, alignedStartSeconds, TranscodeHost.Detect());
        var launch = new TranscodeLaunch(Interlocked.Increment(ref _launchCounter), startSegment, hardware);
        ctx.LaunchId = launch.Id;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);

        var entry = new TranscodeProcess(process, isGpu: hardware?.Encode == true);
        var stderrBuffer = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                lock (stderrBuffer) stderrBuffer.AppendLine(args.Data);
            }
        };
        var cmdLine = $"ffmpeg {string.Join(" ", arguments)}";
        process.Exited += (_, _) => OnFFmpegExited(mediaItemId, entry, launch, stderrBuffer, cmdLine);

        process.Start();
        process.BeginErrorReadLine();
        _activeTranscodes[mediaItemId] = entry;
        _logger.LogInformation("FFmpeg transcode launched for {MediaItemId} startSegment={StartSegment}: {Command}",
            mediaItemId, startSegment, cmdLine);
        return Task.CompletedTask;
    }

    private void OnFFmpegExited(Guid mediaItemId, TranscodeProcess entry, TranscodeLaunch launch, StringBuilder stderrBuffer, string cmdLine)
    {
        int exitCode;
        try { exitCode = entry.Process.ExitCode; } catch (InvalidOperationException) { exitCode = -1; }
        bool failed = exitCode != 0 && !entry.Stopping;

        TranscodeContext? retryContext = null;
        if (failed && launch.Hardware is not null
            && _transcodeContexts.TryGetValue(mediaItemId, out var ctx) && ctx.LaunchId == launch.Id)
        {
            retryContext = ctx;
            ctx.Hardware = null;
            ctx.RetryingOnCpu = true;
        }

        if (!_activeTranscodes.TryRemove(new KeyValuePair<Guid, TranscodeProcess>(mediaItemId, entry)))
        {
            if (retryContext is not null) retryContext.RetryingOnCpu = false;
            return;
        }

        if (failed)
        {
            string stderr;
            lock (stderrBuffer) stderr = stderrBuffer.ToString();
            _logger.LogError(
                "FFmpeg transcode exited with code {ExitCode} for {MediaItemId}. Command: {Command}\nStderr: {Stderr}",
                exitCode, mediaItemId, cmdLine, stderr);
        }
        entry.Process.Dispose();

        if (retryContext is not null)
        {
            _ = RetryOnCpuAsync(mediaItemId, launch, retryContext);
        }
    }

    private async Task RetryOnCpuAsync(Guid mediaItemId, TranscodeLaunch launch, TranscodeContext ctx)
    {
        await _transcodeLock.WaitAsync();
        try
        {
            bool stillCurrent = _transcodeContexts.TryGetValue(mediaItemId, out var current)
                && ReferenceEquals(current, ctx)
                && ctx.LaunchId == launch.Id
                && !_activeTranscodes.ContainsKey(mediaItemId);
            if (!stillCurrent) return;

            var resumeAt = FirstMissingSegment(mediaItemId, ctx.TargetDir, launch.StartSegment);
            if (resumeAt * (double)SegmentSeconds >= ctx.DurationSeconds) return;

            _logger.LogWarning("{Hardware} transcode failed for {MediaItemId}; retrying on the CPU from segment {Segment}.",
                HardwareArguments.Label(launch.Hardware?.Accelerator.Type ?? HardwareAccelerationType.Auto), mediaItemId, resumeAt);

            var settings = await LoadSettingsAsync();
            ctx.CurrentStartSegment = resumeAt;
            ctx.LaunchedAt = DateTime.UtcNow;
            await LaunchFFmpegAsync(mediaItemId, resumeAt, resumeAt * (double)SegmentSeconds, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not restart the transcode for {MediaItemId} on the CPU.", mediaItemId);
        }
        finally
        {
            ctx.RetryingOnCpu = false;
            _transcodeLock.Release();
        }
    }

    private static int FirstMissingSegment(Guid mediaItemId, string targetDir, int fromSegment)
    {
        var segment = fromSegment;
        while (File.Exists(Path.Combine(targetDir, $"{mediaItemId}_{segment}.ts"))) segment++;
        return segment;
    }

    internal static List<string> BuildFFmpegArguments(
        string sourceFile,
        string segmentPattern,
        string internalPlaylistPath,
        PlaybackDecisionVM decision,
        ServerSetting settings,
        TranscodeHardware? hardware,
        int startSegment,
        double alignedStartSeconds,
        TranscodeHost host)
    {
        var args = new List<string>();
        void Flag(string fragment) => args.AddRange(fragment.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        bool encodingVideo = !(decision.Decision == StreamingState.DirectPlay
            || decision.Decision == StreamingState.Remux
            || decision.VideoStrategy == "Copy"
            || decision.VideoStrategy == "DirectStream");

        var hdrType = decision.SourceHdrType;
        bool isHdrSource = !string.IsNullOrWhiteSpace(hdrType)
            && !string.Equals(hdrType, "SDR", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(hdrType, "None", StringComparison.OrdinalIgnoreCase);
        bool needsTonemap = encodingVideo && settings.EnableHdrToneMapping && isHdrSource;

        var decoder = encodingVideo ? hardware?.Accelerator : null;
        var encoder = encodingVideo && hardware is { Encode: true } ? hardware.Accelerator : null;
        var gpuPipeline = decision.RequiresSubtitleBurnIn ? null : encoder;

        bool canTonemapOnGpu = gpuPipeline is { Type: not HardwareAccelerationType.Nvidia }
            ? gpuPipeline.TonemapsOnGpu
            : host.CanUseGpuHdrTonemap;
        var resolvedTonemapQuality = ResolveTonemapQuality(settings.HdrTonemapQuality, canTonemapOnGpu);
        var resolvedDownscale = ResolveDownscaleMode(settings.HdrTranscodeDownscale, canTonemapOnGpu);
        bool downscaleForHdr = needsTonemap && resolvedDownscale == "Always";

        int scaleHeight = decision.OutputHeight > 0 ? decision.OutputHeight : 0;
        if (downscaleForHdr)
        {
            scaleHeight = scaleHeight > 0 ? Math.Min(scaleHeight, 1080) : 1080;
        }

        if (decoder is not null)
        {
            bool vulkan = gpuPipeline is not null && needsTonemap && UsesVulkanTonemap(gpuPipeline);
            args.AddRange(HardwareArguments.InitDevices(decoder, withVulkan: vulkan));
            args.AddRange(HardwareArguments.Decode(decoder, keepFramesOnGpu: gpuPipeline is not null));
            if (gpuPipeline is { Type: HardwareAccelerationType.Nvidia })
            {
                Flag("-filter_threads 0");
                Flag("-filter_complex_threads 0");
            }
        }

        if (alignedStartSeconds > 0.0)
        {
            Flag($"-ss {alignedStartSeconds.ToString("0.000", CultureInfo.InvariantCulture)}");
        }
        Flag("-i"); args.Add(sourceFile);

        if (decision.SelectedVideoStreamIndex.HasValue)
        {
            Flag($"-map 0:{decision.SelectedVideoStreamIndex.Value}");
        }
        if (decision.SelectedAudioStreamIndex.HasValue)
        {
            Flag($"-map 0:{decision.SelectedAudioStreamIndex.Value}");
        }
        Flag("-sn");

        if (!encodingVideo)
        {
            Flag("-c:v copy");
        }
        else
        {
            bool encodeHevc = decision.TargetVideoCodec == VideoCodec.Hevc;

            if (encoder is not null)
            {
                args.AddRange(EncoderArguments(encoder, encodeHevc, settings, decision));
            }
            else
            {
                string swPreset = settings.TranscodeQuality switch { 1 => "superfast", 2 => "medium", 3 => "veryslow", _ => "veryfast" };
                Flag(encodeHevc ? "-c:v libx265" : "-c:v libx264");
                Flag($"-preset {swPreset}");
            }

            if (!encodeHevc && gpuPipeline is null && encoder is null or { Type: HardwareAccelerationType.Nvidia })
            {
                Flag("-pix_fmt yuv420p");
            }

            Flag("-force_key_frames"); args.Add($"expr:gte(t,n_forced*{SegmentSeconds})");

            var filters = gpuPipeline is not null
                ? GpuFilters(gpuPipeline, settings, host, needsTonemap, isHdrSource, encodeHevc, scaleHeight, resolvedTonemapQuality)
                : CpuFilters(encoder, decision, sourceFile, settings, needsTonemap, scaleHeight);
            if (filters.Count > 0)
            {
                Flag("-vf"); args.Add(string.Join(",", filters));
            }

            if (decision.BandwidthKbps > 0)
            {
                int bufferSizeKbps = decision.BandwidthKbps * Math.Max(1, settings.TranscoderThrottleBuffer);
                if (encoder is { Type: not HardwareAccelerationType.Nvidia })
                {
                    Flag($"-b:v {decision.BandwidthKbps}k");
                }
                Flag($"-maxrate {decision.BandwidthKbps}k -bufsize {bufferSizeKbps}k");
            }
        }

        // Audio strategy resolution. The decision manager picks "Copy" when
        // it thinks the client can decode the source audio directly, but
        // that's a lie for several codec/container combinations:
        //   - DTS-HD MA / TrueHD / DTS-HD HRA copied into mpegts loses the
        //     HD extensions or produces nothing playable by ExoPlayer.
        //   - PCM / FLAC / Opus / Vorbis are also problematic in mpegts
        //     and unsupported by many HLS players.
        // The safe set for mpegts + HLS playback across web + Android is
        // {aac, mp3, ac3, eac3}. If "Copy" is requested but the source
        // codec isn't in that set, force-transcode to AAC stereo so the
        // user actually hears audio.
        bool shouldCopyAudio = decision.Decision == StreamingState.DirectPlay
            || decision.Decision == StreamingState.Remux
            || decision.AudioStrategy == "Copy"
            || decision.AudioStrategy == "DirectStream";

        if (shouldCopyAudio && !IsAudioCodecSafeForMpegTs(decision.SourceAudioCodec))
        {
            shouldCopyAudio = false;
        }

        if (shouldCopyAudio)
        {
            Flag("-c:a copy");
        }
        else
        {
            Flag(decision.TargetAudioCodec == AudioCodec.Aac ? "-c:a aac" : "-c:a ac3");
            Flag(decision.TargetAudioChannels > 2 ? $"-ac {decision.TargetAudioChannels} -b:a 384k" : "-ac 2 -b:a 192k");
        }

        // CRITICAL: when -ss seeks the input, FFmpeg by default resets
        // output PTS to 0. Without compensation, segment _155.ts (which
        // we promise the player sits at the 620-624s slot in our VOD
        // playlist) actually contains PTS [0, 4). The video decoder can
        // limp through this because the segment starts at a keyframe,
        // but the audio renderer sees A/V drift of 620 seconds and
        // gives up — which is exactly the "audio plays briefly then
        // dies after a seek" symptom.
        //
        // -output_ts_offset shifts the muxed PTS so the segment content's
        // timestamps match the playlist position. Internal encoding still
        // runs with PTS=0 so -force_key_frames math works, but the muxer
        // writes PTS+offset.
        if (alignedStartSeconds > 0.0)
        {
            Flag($"-output_ts_offset {alignedStartSeconds.ToString("0.000", CultureInfo.InvariantCulture)}");
        }

        // Use FFmpeg's HLS muxer for actual segment writing — it handles
        // audio/video sync, mpegts packetization, and segment cut points
        // correctly. FFmpeg writes its own m3u8 to an internal path we
        // never serve; the client gets our pre-written VOD playlist
        // referencing the same .ts segment files. -start_number gives
        // FFmpeg the absolute starting segment number so filenames line
        // up with our pre-written playlist after a seek-restart.
        Flag($"-f hls -hls_time {SegmentSeconds} -hls_playlist_type vod -hls_list_size 0");
        Flag("-hls_flags independent_segments+temp_file");
        Flag("-hls_segment_filename"); args.Add(segmentPattern);
        // FFmpeg's HLS muxer uses `-start_number` for the starting sequence
        // number, NOT `-hls_start_number`. Easy thing to get wrong (the
        // segment muxer's equivalent IS `-segment_start_number`). Using
        // the wrong name makes FFmpeg exit with code 8 + "Unrecognized
        // option 'hls_start_number'. Error splitting the argument list:
        // Option not found".
        Flag($"-start_number {startSegment}");
        args.Add(internalPlaylistPath);

        return args;
    }

    private static List<string> EncoderArguments(HardwareAccelerator encoder, bool encodeHevc, ServerSetting settings, PlaybackDecisionVM decision)
    {
        switch (encoder.Type)
        {
            case HardwareAccelerationType.Nvidia:
                var nvencPreset = settings.TranscodeQuality switch { 1 => "p2", 2 => "p6", 3 => "p7", _ => "p4" };
                var nvenc = new List<string> { "-c:v", encodeHevc ? "hevc_nvenc" : "h264_nvenc", "-preset", nvencPreset, "-tune", "hq" };
                if (settings.EnableHevcOptimization && encodeHevc) nvenc.AddRange(["-rc", "vbr", "-cq", "28", "-qmin", "28", "-qmax", "34"]);
                return nvenc;
            case HardwareAccelerationType.QuickSync:
                var qsvPreset = settings.TranscodeQuality switch { 1 => "veryfast", 2 => "slow", 3 => "veryslow", _ => "medium" };
                var qsv = new List<string> { "-c:v", encodeHevc ? "hevc_qsv" : "h264_qsv", "-preset", qsvPreset, "-forced_idr", "1" };
                if (decision.BandwidthKbps <= 0) qsv.AddRange(["-global_quality", "23"]);
                return qsv;
            default:
                return ["-c:v", encodeHevc ? "hevc_vaapi" : "h264_vaapi"];
        }
    }

    private static bool UsesVulkanTonemap(HardwareAccelerator gpu) =>
        gpu.Type != HardwareAccelerationType.Nvidia && gpu.Vendor != GpuVendor.Intel && gpu.HasVulkanInterop;

    private static List<string> GpuFilters(
        HardwareAccelerator gpu,
        ServerSetting settings,
        TranscodeHost host,
        bool needsTonemap,
        bool isHdrSource,
        bool encodeHevc,
        int scaleHeight,
        string tonemapQuality)
    {
        var chain = new List<string>();
        if (gpu.Type == HardwareAccelerationType.Nvidia)
        {
            if (needsTonemap && scaleHeight > 0) chain.Add($"scale_cuda=-2:{scaleHeight}:format=p010le");
            if (needsTonemap && host.HasJellyfinFfmpeg)
            {
                chain.Add($"tonemap_cuda=tonemap={settings.TonemappingAlgorithm}:format=nv12:peak=1000");
            }
            else if (needsTonemap)
            {
                chain.AddRange(["hwdownload", "format=p010le", BuildHdrTonemapCpuChain(tonemapQuality, settings.TonemappingAlgorithm)]);
            }
            else
            {
                var scaleSpec = scaleHeight > 0 ? $"-2:{scaleHeight}" : "iw:ih";
                chain.Add($"scale_cuda={scaleSpec}:format={(encodeHevc ? "p010le" : "nv12")}");
            }
            return chain;
        }

        if (needsTonemap && UsesVulkanTonemap(gpu))
        {
            chain.Add(HardwareArguments.VulkanTonemap(settings.TonemappingAlgorithm, scaleHeight));
        }
        else if (needsTonemap)
        {
            if (scaleHeight > 0) chain.Add($"scale_vaapi=w=-2:h={scaleHeight}");
            if (gpu.Vendor == GpuVendor.Intel)
            {
                chain.Add("tonemap_vaapi=format=nv12:p=bt709:t=bt709:m=bt709");
            }
            else
            {
                chain.AddRange(["hwdownload", "format=p010le", BuildHdrTonemapCpuChain(tonemapQuality, settings.TonemappingAlgorithm), "format=nv12", "hwupload"]);
            }
        }
        else
        {
            var format = encodeHevc && isHdrSource ? "p010" : "nv12";
            chain.Add(scaleHeight > 0 ? $"scale_vaapi=w=-2:h={scaleHeight}:format={format}" : $"scale_vaapi=format={format}");
        }

        if (gpu.Type == HardwareAccelerationType.QuickSync)
        {
            chain.AddRange(["hwmap=derive_device=qsv", "format=qsv"]);
        }
        return chain;
    }

    private static List<string> CpuFilters(
        HardwareAccelerator? encoder,
        PlaybackDecisionVM decision,
        string sourceFile,
        ServerSetting settings,
        bool needsTonemap,
        int scaleHeight)
    {
        var chain = new List<string>();
        if (scaleHeight > 0) chain.Add($"scale=-2:{scaleHeight}");
        if (needsTonemap)
        {
            chain.Add($"zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap={settings.TonemappingAlgorithm}:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p");
        }
        if (decision.RequiresSubtitleBurnIn)
        {
            chain.Add($"subtitles='{decision.SelectedSubtitleExternalPath ?? sourceFile}'");
        }
        if (encoder is { Type: HardwareAccelerationType.Vaapi })
        {
            chain.AddRange(["format=nv12", "hwupload"]);
        }
        else if (encoder is { Type: HardwareAccelerationType.QuickSync })
        {
            chain.Add("format=nv12");
        }
        return chain;
    }

    private static bool IsAudioCodecSafeForMpegTs(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec)) return false;
        var c = codec.ToLowerInvariant();
        return c is "aac" or "mp3" or "ac3" or "eac3";
    }

    // Resolve HdrTonemapQuality from the server setting (which may be
    // "Auto") to a concrete pick. On hosts that can't reach a GPU HDR
    // tonemap path (WSL2, no Vulkan ICD), Auto falls back to Fast so
    // the CPU work is real-time. On hosts that can, Auto picks Quality.
    private static string ResolveTonemapQuality(string setting, bool canTonemapOnGpu)
    {
        if (!string.Equals(setting, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return setting;
        }
        return canTonemapOnGpu ? "Quality" : "Fast";
    }

    private static string ResolveDownscaleMode(string setting, bool canTonemapOnGpu)
    {
        if (!string.Equals(setting, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return setting;
        }
        // When GPU tonemap isn't reachable, the CPU tonemap on a 4K HDR
        // source can't keep up at full source resolution even multi-
        // threaded — downscale on the GPU before tonemap so we're only
        // tonemapping 1080p frames on the CPU.
        return canTonemapOnGpu ? "Never" : "Always";
    }

    // Build just the CPU-side portion of the HDR tonemap filter chain
    // (the part after hwdownload + format=p010le). The choice of
    // quality affects how many passes we make over each frame:
    //
    //   Quality : full zscale → linear → tonemap → bt709 chain. Most
    //             accurate. Slow on 4K HDR even multi-threaded —
    //             intended for hosts that have time to spare or where
    //             a downscale to 1080p is also in play.
    //   Fast    : single zscale colorspace conversion + format. Skips
    //             proper PQ→linear→tonemap mapping. Highlights blow
    //             out a bit and overall picture looks brighter than
    //             a reference HDR→SDR conversion, but it's well under
    //             real-time at 1080p on CPU.
    //   Off     : just format=yuv420p. HDR transfer function is
    //             discarded — picture will look noticeably wrong but
    //             encodes very fast.
    private static string BuildHdrTonemapCpuChain(string quality, string algorithm)
    {
        switch (quality)
        {
            case "Off":
                return "format=yuv420p";
            case "Fast":
                return "zscale=p=bt709:t=bt709:m=bt709:r=tv:d=ordered,format=yuv420p";
            case "Quality":
            default:
                return $"zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap={algorithm}:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";
        }
    }

    internal sealed record TranscodeHost(bool HasJellyfinFfmpeg, bool CanUseGpuHdrTonemap)
    {
        public static TranscodeHost Detect() => new(HostEnvironmentDetector.HasJellyfinFfmpeg, HostEnvironmentDetector.CanUseGpuHdrTonemap);
    }

    internal sealed record TranscodeHardware(HardwareAccelerator Accelerator, bool Encode);

    private sealed record TranscodeLaunch(int Id, int StartSegment, TranscodeHardware? Hardware);

    private sealed class TranscodeProcess(Process process, bool isGpu)
    {
        private volatile bool _stopping;

        public Process Process { get; } = process;
        public bool IsGpu { get; } = isGpu;
        public bool Stopping
        {
            get => _stopping;
            set => _stopping = value;
        }
    }

    private sealed class TranscodeContext
    {
        private volatile bool _retryingOnCpu;

        public TranscodeContext(string sourceFile, PlaybackDecisionVM decision, string targetDir, double durationSeconds, int currentStartSegment, TranscodeHardware? hardware)
        {
            Hardware = hardware;
            SourceFile = sourceFile;
            Decision = decision;
            TargetDir = targetDir;
            DurationSeconds = durationSeconds;
            CurrentStartSegment = currentStartSegment;
            LaunchedAt = DateTime.UtcNow;
            LastActivityAt = DateTime.UtcNow;
            HighestServedSegment = -1;
        }
        public string SourceFile { get; }
        public PlaybackDecisionVM Decision { get; }
        public string TargetDir { get; }
        public double DurationSeconds { get; }
        public int CurrentStartSegment { get; set; }
        public TranscodeHardware? Hardware { get; set; }
        public int LaunchId { get; set; }
        public bool RetryingOnCpu
        {
            get => _retryingOnCpu;
            set => _retryingOnCpu = value;
        }
        public DateTime LaunchedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        // Highest .ts segment index the chunk handler has actually served
        // to the client. -1 means nothing has been served yet — which lets
        // us distinguish player initial-probe requests for segment 0
        // (suppress) from a real user seek backward (allow restart).
        public int HighestServedSegment { get; set; }
    }
}
