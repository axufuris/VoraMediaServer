using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegTranscodeService : ITranscodeService
{
    private static readonly ConcurrentDictionary<Guid, TranscodeProcess> _activeTranscodes = new();
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

        bool attemptHwAccel = settings.UseHardwareAcceleration && settings.UseHardwareEncoding;
        bool willUseGpu;
        string targetDir;

        await _transcodeLock.WaitAsync();
        try
        {
            int currentGpu = _activeTranscodes.Values.Count(x => x.IsGpu);
            int currentCpu = _activeTranscodes.Values.Count(x => !x.IsGpu);

            willUseGpu = attemptHwAccel && (settings.MaxGpuTranscodes == 0 || currentGpu < settings.MaxGpuTranscodes);

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

            await StopTranscodeSessionAsync(decision.MediaItemId);

            targetDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? outputDirectory : settings.TranscoderTempDirectory;
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            var outputFileName = $"{decision.MediaItemId}.m3u8";
            var outputPath = Path.Combine(targetDir, outputFileName);
            var arguments = BuildFFmpegArguments(sourceFilePath, outputPath, decision, settings, willUseGpu);

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

            var mediaItemId = decision.MediaItemId;
            process.Exited += (_, _) =>
            {
                if (_activeTranscodes.TryRemove(mediaItemId, out var stale))
                {
                    stale.Process.Dispose();
                }
            };

            process.Start();
            _activeTranscodes[mediaItemId] = new TranscodeProcess(process, willUseGpu);
        }
        finally
        {
            _transcodeLock.Release();
        }

        await Task.Delay(3000, cancellationToken);

        return Path.Combine(targetDir, $"{decision.MediaItemId}.m3u8");
    }

    private Task StopTranscodeSessionAsync(Guid mediaItemId)
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

    public int GetActiveTranscodeCount() => _activeTranscodes.Count;
    public bool IsMediaTranscoding(Guid mediaItemId) => _activeTranscodes.ContainsKey(mediaItemId);

    private static string BuildFFmpegArguments(string sourceFile, string outputPath, PlaybackDecisionVM decision, Domain.Entities.Settings.ServerSetting settings, bool useGpu)
    {
        var args = new List<string>();

        if (settings.UseHardwareAcceleration)
        {
            args.Add("-hwaccel auto");

            if (!string.IsNullOrWhiteSpace(settings.HardwareTranscodingDevice) && settings.HardwareTranscodingDevice != "Auto")
            {
                args.Add($"-hwaccel_device {settings.HardwareTranscodingDevice}");
            }
        }

        args.Add($"-i \"{sourceFile}\"");

        if (decision.Decision == StreamingState.DirectPlay || decision.Decision == StreamingState.Remux || decision.VideoStrategy == "Copy" || decision.VideoStrategy == "DirectStream")
        {
            args.Add("-c:v copy");
        }
        else
        {
            var cpuPresets = new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };
            string cpuPreset = cpuPresets[Math.Clamp(settings.BackgroundX264Preset, 0, 8)];

            bool sourceIsHevc = sourceFile.EndsWith(".mkv") || sourceFile.Contains("hevc") || sourceFile.Contains("265");
            bool encodeHevc = settings.EnableHevcEncoding == 2 || (settings.EnableHevcEncoding == 1 && sourceIsHevc);

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

            if (settings.EnableHdrToneMapping)
            {
                args.Add($"-vf zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap={settings.TonemappingAlgorithm}:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p");
            }

            if (decision.RequiresSubtitleBurnIn)
            {
                args.Add($"-vf \"subtitles='{sourceFile}'\"");
            }

            if (decision.BandwidthKbps > 0)
            {
                int bufferSizeKbps = decision.BandwidthKbps * Math.Max(1, settings.TranscoderThrottleBuffer);
                args.Add($"-maxrate {decision.BandwidthKbps}k -bufsize {bufferSizeKbps}k");
            }
        }

        if (decision.Decision == StreamingState.DirectPlay || decision.Decision == StreamingState.Remux || decision.AudioStrategy == "Copy" || decision.AudioStrategy == "DirectStream")
        {
            args.Add("-c:a copy");
        }
        else
        {
            args.Add(decision.TargetAudioCodec == AudioCodec.Aac ? "-c:a aac" : "-c:a ac3");
            args.Add(decision.TargetAudioChannels > 2 ? $"-ac {decision.TargetAudioChannels} -b:a 384k" : "-ac 2 -b:a 192k");
        }

        args.Add("-f hls -hls_time 4 -hls_playlist_type event");
        args.Add($"-hls_segment_filename \"{outputPath.Replace(".m3u8", "_%03d.ts")}\"");
        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }

    private sealed record TranscodeProcess(Process Process, bool IsGpu);
}
