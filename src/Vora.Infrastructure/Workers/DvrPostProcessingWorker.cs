using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Vora.Application.Analysis;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Processes;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Workers;

public class DvrPostProcessingWorker : BackgroundService
{
    private static readonly TimeSpan FfmpegTimeout = TimeSpan.FromHours(6);
    private static readonly TimeSpan ComskipTimeout = TimeSpan.FromHours(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHardwareCapabilityService _hardware;
    private readonly ILogger<DvrPostProcessingWorker> _logger;

    public DvrPostProcessingWorker(IServiceScopeFactory scopeFactory, IHardwareCapabilityService hardware, ILogger<DvrPostProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _hardware = hardware;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DVR Post-Processing Worker is starting.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessCompletedRecordingsAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[DVR] Error during post-processing pass.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DVR Post-Processing Worker is stopping.");
        }
    }

    private async Task ProcessCompletedRecordingsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();

        var pendingSessions = await repo.GetCompletedRawSessionsAsync();

        foreach (var session in pendingSessions)
        {
            if (string.IsNullOrWhiteSpace(session.OutputFilePath) || !File.Exists(session.OutputFilePath)) continue;

            await ProcessSingleRecordingAsync(session, repo, settingsRepo, notifier);
        }
    }

    private async Task ProcessSingleRecordingAsync(IptvRecordingSession session, IIptvRepository repo, ISystemSettingsRepository settingsRepo, IClientNotifier notifier)
    {
        string originalPath = session.OutputFilePath!;
        string newPath = Path.ChangeExtension(originalPath, ".mp4");

        _logger.LogInformation($"[DVR Post-Processor] Starting conversion for '{session.Title}'");

        await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.PostProcessing);
        await notifier.NotifyDvrSessionsUpdatedAsync();

        try
        {
            bool transcodeSuccess = await TranscodeRecordingAsync(originalPath, newPath, settingsRepo);

            if (transcodeSuccess && File.Exists(newPath))
            {
                File.Delete(originalPath);

                string markersJson = await RunComskipAndGetMarkersAsync(session, newPath, repo, notifier);

                await FinalizeSessionInDatabaseAsync(session.Id, newPath, markersJson, repo);
                _logger.LogInformation($"[DVR Post-Processor] Successfully finished '{session.Title}'.");
            }
            else
            {
                _logger.LogError($"[DVR Post-Processor] FFmpeg failed for '{session.Title}'");
                await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Completed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[DVR Post-Processor] Fatal error processing '{session.Title}'");
            await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Completed);
        }
        finally
        {
            await notifier.NotifyDvrSessionsUpdatedAsync();
        }
    }

    private async Task<bool> TranscodeRecordingAsync(string inputPath, string outputPath, ISystemSettingsRepository settingsRepo)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        var encoder = !settings.DisableVideoTranscoding && settings.UseHardwareAcceleration && settings.UseHardwareEncoding
            ? await _hardware.ResolveAsync(settings.HardwareAccelerationType, settings.HardwareTranscodingDevice)
            : null;

        if (settings.DisableVideoTranscoding)
        {
            _logger.LogInformation("[DVR Post-Processor] Transcoding disabled. Remuxing to MP4...");
        }
        else
        {
            _logger.LogInformation("[DVR Post-Processor] Transcoding with {Encoder}.", encoder is null ? "the CPU" : HardwareArguments.Label(encoder.Type));
        }

        if (await RunFfmpegTranscodeAsync(BuildFfmpegArgs(inputPath, outputPath, settings, encoder))) return true;
        if (encoder is null) return false;

        _logger.LogWarning("[DVR Post-Processor] {Encoder} encode failed; retrying on the CPU.", HardwareArguments.Label(encoder.Type));
        return await RunFfmpegTranscodeAsync(BuildFfmpegArgs(inputPath, outputPath, settings, encoder: null));
    }

    internal static List<string> BuildFfmpegArgs(string inputPath, string outputPath, ServerSetting settings, HardwareAccelerator? encoder)
    {
        if (settings.DisableVideoTranscoding)
        {
            return ["-i", inputPath, "-c", "copy", "-movflags", "+faststart", "-y", outputPath];
        }

        bool useHevc = settings.EnableHevcEncoding > 0;
        string preset = settings.BackgroundX264Preset switch
        {
            0 => "ultrafast",
            1 => "superfast",
            2 => "veryfast",
            3 => "faster",
            4 => "fast",
            5 => "medium",
            6 => "slow",
            7 => "slower",
            8 => "veryslow",
            _ => "veryfast"
        };
        string qsvPreset = preset is "ultrafast" or "superfast" ? "veryfast" : preset;

        var args = new List<string>();
        if (encoder is not null) args.AddRange(HardwareArguments.InitDevices(encoder));
        args.AddRange(["-i", inputPath]);
        args.AddRange(encoder?.Type switch
        {
            HardwareAccelerationType.Nvidia when encoder.Device is { } gpu => ["-c:v", useHevc ? "hevc_nvenc" : "h264_nvenc", "-gpu", gpu, "-preset", "p4", "-cq", "28"],
            HardwareAccelerationType.Nvidia => ["-c:v", useHevc ? "hevc_nvenc" : "h264_nvenc", "-preset", "p4", "-cq", "28"],
            HardwareAccelerationType.QuickSync => ["-c:v", useHevc ? "hevc_qsv" : "h264_qsv", "-preset", qsvPreset, "-global_quality", "28"],
            HardwareAccelerationType.Vaapi => ["-vf", "format=nv12,hwupload", "-c:v", useHevc ? "hevc_vaapi" : "h264_vaapi", "-qp", "28"],
            _ => ["-c:v", useHevc ? "libx265" : "libx264", "-preset", preset, "-crf", "23"]
        });
        args.AddRange(["-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-y", outputPath]);
        return args;
    }

    private async Task<bool> RunFfmpegTranscodeAsync(IReadOnlyList<string> args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) processInfo.ArgumentList.Add(arg);

        using var process = Process.Start(processInfo);
        if (process == null) return false;

        string errorOutput = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitWithTimeoutAsync(FfmpegTimeout, _logger);

        if (process.ExitCode != 0)
        {
            _logger.LogError("[DVR Post-Processor] FFmpeg Transcode Failed!\nArguments: {Arguments}\nExit Code: {ExitCode}\nFFmpeg Output: {Output}",
                string.Join(" ", args), process.ExitCode, errorOutput);
            return false;
        }

        return true;
    }

    private async Task<string> RunComskipAndGetMarkersAsync(IptvRecordingSession session, string videoPath, IIptvRepository repo, IClientNotifier notifier)
    {
        _logger.LogInformation($"[DVR Post-Processor] Running Comskip on '{session.Title}'...");
        await repo.UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.DetectingCommercials);
        await notifier.NotifyDvrSessionsUpdatedAsync();

        string edlPath = Path.ChangeExtension(videoPath, ".edl");
        string txtPath = Path.ChangeExtension(videoPath, ".txt");
        string logPath = Path.ChangeExtension(videoPath, ".log");
        string logoPath = Path.ChangeExtension(videoPath, ".logo.txt");
        string iniPath = Path.Combine(Path.GetDirectoryName(videoPath) ?? "/app/data/iptv/dvr", "comskip.ini");

        string markersJson = "[]";

        try
        {
            await File.WriteAllTextAsync(iniPath, "output_edl=1\noutput_default=0\n");

            var comskipInfo = new ProcessStartInfo
            {
                FileName = "comskip",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(videoPath)
            };
            comskipInfo.ArgumentList.Add("--quiet");
            comskipInfo.ArgumentList.Add($"--ini={iniPath}");
            comskipInfo.ArgumentList.Add(videoPath);

            using var comskipProc = Process.Start(comskipInfo);
            if (comskipProc != null) await comskipProc.WaitForExitWithTimeoutAsync(ComskipTimeout, _logger);

            if (File.Exists(edlPath))
            {
                var markers = new List<object>();
                var lines = await File.ReadAllLinesAsync(edlPath);

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 &&
                        double.TryParse(parts[0], out double start) &&
                        double.TryParse(parts[1], out double end))
                    {
                        markers.Add(new { start = start, end = end });
                    }
                }

                markersJson = JsonSerializer.Serialize(markers);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Comskip failed or is not installed: {ex.Message}. Skipping commercial detection.");
        }
        finally
        {
            try { if (File.Exists(edlPath)) File.Delete(edlPath); } catch { }
            try { if (File.Exists(txtPath)) File.Delete(txtPath); } catch { }
            try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
            try { if (File.Exists(logoPath)) File.Delete(logoPath); } catch { }
            try { if (File.Exists(iniPath)) File.Delete(iniPath); } catch { }
        }

        return markersJson;
    }

    private async Task FinalizeSessionInDatabaseAsync(Guid sessionId, string newPath, string markersJson, IIptvRepository repo)
    {
        var dbSession = await repo.GetSessionByIdAsync(sessionId);
        if (dbSession != null)
        {
            dbSession.Status = IptvRecordingSessionStatus.Completed;
            dbSession.OutputFilePath = newPath;
            dbSession.CommercialMarkersJson = markersJson;
            var fileInfo = new FileInfo(newPath);
            dbSession.FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0;
            await repo.UpdateSessionAsync(dbSession);
        }
    }
}
