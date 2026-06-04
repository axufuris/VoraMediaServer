using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegTranscodeService : ITranscodeService
{
    private const int SegmentSeconds = 4;
    private const double FallbackDurationSeconds = 4 * 60 * 60;

    private static readonly ConcurrentDictionary<Guid, TranscodeProcess> _activeTranscodes = new();
    private static readonly ConcurrentDictionary<Guid, TranscodeContext> _transcodeContexts = new();
    private static readonly SemaphoreSlim _transcodeLock = new(1, 1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FFmpegTranscodeService> _logger;

    public FFmpegTranscodeService(IServiceProvider serviceProvider, ILogger<FFmpegTranscodeService> logger)
    {
        _serviceProvider = serviceProvider;
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

        await _transcodeLock.WaitAsync(cancellationToken);
        try
        {
            int currentGpu = _activeTranscodes.Values.Count(x => x.IsGpu);
            int currentCpu = _activeTranscodes.Values.Count(x => !x.IsGpu);
            bool attemptHwAccel = settings.UseHardwareAcceleration && settings.UseHardwareEncoding;
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

            var ctx = new TranscodeContext(sourceFilePath, decision, targetDir, durationSeconds, startSegment);
            _transcodeContexts[decision.MediaItemId] = ctx;

            await LaunchFFmpegAsync(decision.MediaItemId, startSegment, alignedStart, settings, willUseGpu);
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
        bool hasActiveProcess = _activeTranscodes.ContainsKey(mediaItemId);

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
            if (!_activeTranscodes.ContainsKey(mediaItemId))
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
            if (segmentIndex > ctx.HighestServedSegment)
            {
                ctx.HighestServedSegment = segmentIndex;
            }
        }
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

            using var scope = _serviceProvider.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            bool attemptHwAccel = settings.UseHardwareAcceleration && settings.UseHardwareEncoding;
            bool willUseGpu = attemptHwAccel;

            var alignedStart = newStartSegment * (double)SegmentSeconds;
            ctx.CurrentStartSegment = newStartSegment;
            ctx.LaunchedAt = DateTime.UtcNow;
            await LaunchFFmpegAsync(mediaItemId, newStartSegment, alignedStart, settings, willUseGpu);
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

    private Task LaunchFFmpegAsync(Guid mediaItemId, int startSegment, double alignedStartSeconds, Domain.Entities.Settings.ServerSetting settings, bool useGpu)
    {
        if (!_transcodeContexts.TryGetValue(mediaItemId, out var ctx))
        {
            throw new InvalidOperationException("No transcode context to launch.");
        }

        var segmentPattern = Path.Combine(ctx.TargetDir, $"{mediaItemId}_%d.ts");
        var internalPlaylist = Path.Combine(ctx.TargetDir, $"_ffmpeg_{mediaItemId}.m3u8");
        var arguments = BuildFFmpegArguments(ctx.SourceFile, segmentPattern, internalPlaylist, ctx.Decision, settings, useGpu, startSegment, alignedStartSeconds);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        var stderrBuffer = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                lock (stderrBuffer) stderrBuffer.AppendLine(args.Data);
            }
        };
        var logger = _logger;
        var cmdLine = $"ffmpeg {arguments}";
        process.Exited += (_, _) =>
        {
            if (_activeTranscodes.TryRemove(mediaItemId, out var stale))
            {
                var exitCode = -1;
                try { exitCode = stale.Process.ExitCode; } catch { /* already disposed / never started */ }
                if (exitCode != 0)
                {
                    string stderr;
                    lock (stderrBuffer) stderr = stderrBuffer.ToString();
                    logger.LogError(
                        "FFmpeg transcode exited with code {ExitCode} for {MediaItemId}. Command: {Command}\nStderr: {Stderr}",
                        exitCode, mediaItemId, cmdLine, stderr);
                }
                stale.Process.Dispose();
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        _activeTranscodes[mediaItemId] = new TranscodeProcess(process, useGpu);
        _logger.LogInformation("FFmpeg transcode launched for {MediaItemId} startSegment={StartSegment}: {Command}",
            mediaItemId, startSegment, cmdLine);
        return Task.CompletedTask;
    }

    private static string BuildFFmpegArguments(
        string sourceFile,
        string segmentPattern,
        string internalPlaylistPath,
        PlaybackDecisionVM decision,
        Domain.Entities.Settings.ServerSetting settings,
        bool useGpu,
        int startSegment,
        double alignedStartSeconds)
    {
        var args = new List<string>();

        // Determine up front whether we'll actually re-encode video. If
        // the strategy is Copy / DirectPlay we just pass-through and
        // don't need filters at all.
        bool encodingVideo = !(decision.Decision == StreamingState.DirectPlay
            || decision.Decision == StreamingState.Remux
            || decision.VideoStrategy == "Copy"
            || decision.VideoStrategy == "DirectStream");

        var hdrType = decision.SourceHdrType;
        bool isHdrSource = !string.IsNullOrWhiteSpace(hdrType)
            && !string.Equals(hdrType, "SDR", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(hdrType, "None", StringComparison.OrdinalIgnoreCase);
        bool needsTonemap = encodingVideo && settings.EnableHdrToneMapping && isHdrSource;

        // Full-GPU pipeline: NVDEC → (scale_cuda / tonemap_opencl, zero-
        // copy on the GPU) → NVENC. Engages only when:
        //   - hardware accel + hardware encoding are both enabled
        //   - we're actually re-encoding video (not stream copy)
        //   - we're not burning in subtitles (subtitles filter is CPU-only)
        // Anything else falls back to the legacy CPU-or-mixed pipeline.
        bool fullGpuPipeline = useGpu && encodingVideo && !decision.RequiresSubtitleBurnIn;

        // Resolve the "Auto" values for HDR tonemap quality + downscale
        // based on detected host environment. WSL2/Docker Desktop on
        // Windows can't expose Vulkan/OpenCL through to the container,
        // so we pre-fall-back to Fast tonemap + Always downscale for
        // 4K HDR. Native Linux / Unraid can do better defaults.
        var resolvedTonemapQuality = ResolveTonemapQuality(settings.HdrTonemapQuality);
        var resolvedDownscale = ResolveDownscaleMode(settings.HdrTranscodeDownscale);
        bool downscaleForHdr = needsTonemap && resolvedDownscale == "Always";

        if (fullGpuPipeline)
        {
            args.Add("-hwaccel cuda -hwaccel_output_format cuda");
            if (!string.IsNullOrWhiteSpace(settings.HardwareTranscodingDevice) && settings.HardwareTranscodingDevice != "Auto")
            {
                args.Add($"-hwaccel_device {settings.HardwareTranscodingDevice}");
            }
            // Thread the CPU filter chain across all available cores.
            // Matters most for the Quality HDR tonemap path where the
            // zscale chain runs on system RAM.
            args.Add("-filter_threads 0");
            args.Add("-filter_complex_threads 0");
        }
        else if (settings.UseHardwareAcceleration)
        {
            args.Add("-hwaccel auto");
            if (!string.IsNullOrWhiteSpace(settings.HardwareTranscodingDevice) && settings.HardwareTranscodingDevice != "Auto")
            {
                args.Add($"-hwaccel_device {settings.HardwareTranscodingDevice}");
            }
        }

        // Seek-to-start using -ss BEFORE -i so FFmpeg uses fast keyframe-
        // accurate input seek. With the HLS muxer and -start_number
        // below, the output segment _N.ts will contain content from
        // source-time [N*SegmentSeconds, +SegmentSeconds).
        if (alignedStartSeconds > 0.0)
        {
            args.Add($"-ss {alignedStartSeconds.ToString("0.000", CultureInfo.InvariantCulture)}");
        }
        args.Add($"-i \"{sourceFile}\"");

        if (decision.SelectedVideoStreamIndex.HasValue)
        {
            args.Add($"-map 0:{decision.SelectedVideoStreamIndex.Value}");
        }
        if (decision.SelectedAudioStreamIndex.HasValue)
        {
            args.Add($"-map 0:{decision.SelectedAudioStreamIndex.Value}");
        }
        // Explicitly drop subtitles from the HLS output. We don't currently
        // generate in-band WebVTT, so mapping an arbitrary subtitle stream
        // into mpegts either errors out or produces unrenderable bytes.
        // Subtitle playback ships as a sidecar VTT in a separate slice.
        args.Add("-sn");

        if (decision.Decision == StreamingState.DirectPlay || decision.Decision == StreamingState.Remux || decision.VideoStrategy == "Copy" || decision.VideoStrategy == "DirectStream")
        {
            args.Add("-c:v copy");
        }
        else
        {
            var cpuPresets = new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };
            string cpuPreset = cpuPresets[Math.Clamp(settings.BackgroundX264Preset, 0, 8)];

            bool encodeHevc = decision.TargetVideoCodec == VideoCodec.Hevc;

            string gpuPreset = settings.TranscodeQuality switch { 1 => "p2", 2 => "p6", 3 => "p7", _ => "p4" };
            string swPreset = settings.TranscodeQuality switch { 1 => "superfast", 2 => "medium", 3 => "veryslow", _ => "veryfast" };

            if (useGpu)
            {
                args.Add(encodeHevc ? "-c:v hevc_nvenc" : "-c:v h264_nvenc");
                args.Add($"-preset {gpuPreset} -tune hq");
                if (settings.EnableHevcOptimization && encodeHevc) args.Add("-rc vbr -cq 28 -qmin 28 -qmax 34");
            }
            else
            {
                args.Add(encodeHevc ? "-c:v libx265" : "-c:v libx264");
                args.Add($"-preset {swPreset}");
            }

            // -pix_fmt only applies when frames live in system RAM (CPU
            // pipeline). On the full GPU pipeline, frames are CUDA-side
            // nv12/p010 surfaces and we shouldn't request a CPU pixel
            // format conversion — that would force a hwdownload/hwupload
            // pair behind the scenes and defeat the GPU pipeline.
            if (!encodeHevc && !fullGpuPipeline)
            {
                args.Add("-pix_fmt yuv420p");
            }

            // Force keyframes at every segment boundary so the segment muxer
            // can cut cleanly at exact SegmentSeconds intervals. Without
            // this, segments drift in duration (FFmpeg cuts at the next
            // keyframe AFTER the target time) which breaks the player's
            // mapping between the pre-written EXTINF values and the actual
            // segment content.
            args.Add($"-force_key_frames \"expr:gte(t,n_forced*{SegmentSeconds})\"");

            // Filter chain. We keep source resolution — never silently
            // downscale to "fix" pipeline speed. The encoder pipeline's
            // job is to keep up with whatever resolution the user chose;
            // if the user wants 1080p output they pick it in the Quality
            // panel and the decision manager sets it explicitly. The
            // GPU pipeline below handles 4K HDR HEVC → 4K H264 SDR
            // end-to-end on the GPU with no CPU work in the video path.
            if (fullGpuPipeline && needsTonemap && HostEnvironmentDetector.HasJellyfinFfmpeg)
            {
                // Full-GPU HDR pipeline via jellyfin-ffmpeg's tonemap_cuda
                // filter:
                //   NVDEC (cuda hwframe)
                //   → optional scale_cuda downscale on GPU
                //   → tonemap_cuda (HDR PQ → SDR, runs as CUDA compute
                //     kernels on the GPU — zero CPU on the video path)
                //   → NVENC encode
                // tonemap_cuda is what Jellyfin's custom ffmpeg adds
                // via --enable-cuda-llvm. It works everywhere CUDA
                // works — bare-metal Linux AND WSL2 — so the WSL2
                // Vulkan/OpenCL gap goes away entirely. This is the
                // "how does Plex do it" answer: their ffmpeg has the
                // same filter via the same build flag.
                var chain = new List<string>();
                if (downscaleForHdr)
                {
                    chain.Add("scale_cuda=-2:1080:format=p010le");
                }
                chain.Add($"tonemap_cuda=tonemap={settings.TonemappingAlgorithm}:format=nv12:peak=1000");
                args.Add($"-vf \"{string.Join(",", chain)}\"");
            }
            else if (fullGpuPipeline && needsTonemap)
            {
                // Legacy CPU-tonemap path. Used when jellyfin-ffmpeg
                // isn't present (stock Debian / Ubuntu apt ffmpeg).
                // Same shape as the old code but kept as a fallback so
                // a server that hasn't picked up the new Dockerfile
                // still plays HDR sources, just slower.
                //   NVDEC → optional scale_cuda (GPU)
                //   → hwdownload + format=p010le (CUDA → CPU)
                //   → CPU zscale + tonemap chain
                //   → NVENC re-uploads the CPU nv12 to GPU
                var chain = new List<string>();
                if (downscaleForHdr)
                {
                    chain.Add("scale_cuda=-2:1080:format=p010le");
                }
                chain.Add("hwdownload");
                chain.Add("format=p010le");
                chain.Add(BuildHdrTonemapCpuChain(resolvedTonemapQuality, settings.TonemappingAlgorithm));
                args.Add($"-vf \"{string.Join(",", chain)}\"");
            }
            else if (fullGpuPipeline)
            {
                // SDR source on the GPU pipeline. No tonemap needed —
                // just make sure the CUDA frame is in a format NVENC
                // accepts. NVDEC emits nv12 for 8-bit and p010 for 10-bit
                // sources; h264_nvenc wants nv12, hevc_nvenc can keep
                // p010le. scale_cuda with iw:ih preserves resolution.
                var targetFmt = encodeHevc ? "p010le" : "nv12";
                args.Add($"-vf \"scale_cuda=iw:ih:format={targetFmt}\"");
            }
            else if (needsTonemap)
            {
                // Pure CPU tonemap fallback (UseHardwareAcceleration or
                // UseHardwareEncoding turned off in server settings).
                // Slow on 4K HDR, but it's the only correct thing when
                // the user opts out of GPU.
                args.Add($"-vf zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap={settings.TonemappingAlgorithm}:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p");
            }

            if (decision.RequiresSubtitleBurnIn)
            {
                // Subtitle burn-in runs on CPU only, so it can only attach
                // here in the non-GPU path. We already excluded subtitle
                // burn-in from fullGpuPipeline above.
                args.Add($"-vf \"subtitles='{sourceFile}'\"");
            }

            if (decision.BandwidthKbps > 0)
            {
                int bufferSizeKbps = decision.BandwidthKbps * Math.Max(1, settings.TranscoderThrottleBuffer);
                args.Add($"-maxrate {decision.BandwidthKbps}k -bufsize {bufferSizeKbps}k");
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
            args.Add("-c:a copy");
        }
        else
        {
            args.Add(decision.TargetAudioCodec == AudioCodec.Aac ? "-c:a aac" : "-c:a ac3");
            args.Add(decision.TargetAudioChannels > 2 ? $"-ac {decision.TargetAudioChannels} -b:a 384k" : "-ac 2 -b:a 192k");
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
            args.Add($"-output_ts_offset {alignedStartSeconds.ToString("0.000", CultureInfo.InvariantCulture)}");
        }

        // Use FFmpeg's HLS muxer for actual segment writing — it handles
        // audio/video sync, mpegts packetization, and segment cut points
        // correctly. FFmpeg writes its own m3u8 to an internal path we
        // never serve; the client gets our pre-written VOD playlist
        // referencing the same .ts segment files. -start_number gives
        // FFmpeg the absolute starting segment number so filenames line
        // up with our pre-written playlist after a seek-restart.
        args.Add($"-f hls -hls_time {SegmentSeconds} -hls_playlist_type vod -hls_list_size 0");
        args.Add("-hls_flags independent_segments+temp_file");
        args.Add($"-hls_segment_filename \"{segmentPattern}\"");
        // FFmpeg's HLS muxer uses `-start_number` for the starting sequence
        // number, NOT `-hls_start_number`. Easy thing to get wrong (the
        // segment muxer's equivalent IS `-segment_start_number`). Using
        // the wrong name makes FFmpeg exit with code 8 + "Unrecognized
        // option 'hls_start_number'. Error splitting the argument list:
        // Option not found".
        args.Add($"-start_number {startSegment}");
        args.Add($"\"{internalPlaylistPath}\"");

        return string.Join(" ", args);
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
    private static string ResolveTonemapQuality(string setting)
    {
        if (!string.Equals(setting, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return setting;
        }
        return HostEnvironmentDetector.CanUseGpuHdrTonemap ? "Quality" : "Fast";
    }

    private static string ResolveDownscaleMode(string setting)
    {
        if (!string.Equals(setting, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return setting;
        }
        // When GPU tonemap isn't reachable, the CPU tonemap on a 4K HDR
        // source can't keep up at full source resolution even multi-
        // threaded — downscale on the GPU before tonemap so we're only
        // tonemapping 1080p frames on the CPU.
        return HostEnvironmentDetector.CanUseGpuHdrTonemap ? "Never" : "Always";
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

    private sealed record TranscodeProcess(Process Process, bool IsGpu);

    private sealed class TranscodeContext
    {
        public TranscodeContext(string sourceFile, PlaybackDecisionVM decision, string targetDir, double durationSeconds, int currentStartSegment)
        {
            SourceFile = sourceFile;
            Decision = decision;
            TargetDir = targetDir;
            DurationSeconds = durationSeconds;
            CurrentStartSegment = currentStartSegment;
            LaunchedAt = DateTime.UtcNow;
            HighestServedSegment = -1;
        }
        public string SourceFile { get; }
        public PlaybackDecisionVM Decision { get; }
        public string TargetDir { get; }
        public double DurationSeconds { get; }
        public int CurrentStartSegment { get; set; }
        public DateTime LaunchedAt { get; set; }
        // Highest .ts segment index the chunk handler has actually served
        // to the client. -1 means nothing has been served yet — which lets
        // us distinguish player initial-probe requests for segment 0
        // (suppress) from a real user seek backward (allow restart).
        public int HighestServedSegment { get; set; }
    }
}
