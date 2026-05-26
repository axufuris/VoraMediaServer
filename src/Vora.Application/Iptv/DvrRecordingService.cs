using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public interface IDvrRecordingService
{
    Task StartRecordingAsync(Guid sessionId);
    Task StopRecordingAsync(Guid sessionId);
}

public class DvrRecordingService : IDvrRecordingService
{
    private class ActiveRecording
    {
        public CancellationTokenSource Cts { get; set; } = new();
        public Process? Process { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, ActiveRecording> _activeRecordings = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly StoragePathsOptions _storagePaths;
    private readonly ILogger<DvrRecordingService> _logger;

    public DvrRecordingService(IServiceProvider serviceProvider, IOptions<StoragePathsOptions> storagePaths, ILogger<DvrRecordingService> logger)
    {
        _serviceProvider = serviceProvider;
        _storagePaths = storagePaths.Value;
        _logger = logger;
    }

    public async Task StartRecordingAsync(Guid sessionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();

        var session = await repo.GetSessionByIdAsync(sessionId);
        if (session == null || session.Schedule?.Channel?.StreamUrl == null)
        {
            await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: "Invalid session or missing stream URL.");
            return;
        }

        var user = await repo.GetUserWithQuotaAsync(session.Schedule.UserId);
        if (user.DvrStorageQuotaBytes > 0)
        {
            long currentUsage = await repo.GetDvrUsageBytesAsync(user.Id);
            if (currentUsage >= user.DvrStorageQuotaBytes)
            {
                await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: "DVR Storage Quota exceeded.");
                return;
            }
        }

        var settingsRepo = scope.ServiceProvider.GetRequiredService<Vora.Application.Settings.ISystemSettingsRepository>();
        var settings = await settingsRepo.GetSettingsAsync();

        string dvrBaseDir = !string.IsNullOrWhiteSpace(settings.DvrStoragePath)
            ? settings.DvrStoragePath
            : (_storagePaths.IptvDvr ?? "/app/data/iptv/dvr");
        if (!Directory.Exists(dvrBaseDir)) Directory.CreateDirectory(dvrBaseDir);

        string safeTitle = string.Join("_", session.Title.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        string fileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.ts";
        string outputPath = Path.Combine(dvrBaseDir, fileName);

        var activeRec = new ActiveRecording();
        _activeRecordings.TryAdd(sessionId, activeRec);

        _ = Task.Run(() => RecordingLoopAsync(sessionId, session.Schedule.Channel.StreamUrl, outputPath, session.EndTime, activeRec));

        await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Recording, outputPath: outputPath);
        var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();
        await notifier.NotifyDvrSessionsUpdatedAsync();
    }

    private async Task RecordingLoopAsync(Guid sessionId, string streamUrl, string outputPath, DateTime endTime, ActiveRecording activeRec)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        var dvrManager = scope.ServiceProvider.GetRequiredService<IDvrManager>();
        var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();
        var token = activeRec.Cts.Token;

        try
        {
            using var fs = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.Read);

            while (DateTime.UtcNow < endTime && !token.IsCancellationRequested)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-reconnect");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-reconnect_streamed");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-reconnect_delay_max");
                psi.ArgumentList.Add("5");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(streamUrl);
                psi.ArgumentList.Add("-fflags");
                psi.ArgumentList.Add("+genpts");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("copy");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("mpegts");
                psi.ArgumentList.Add("pipe:1");

                var process = new Process
                {
                    StartInfo = psi
                };

                activeRec.Process = process;
                process.Start();

                _ = Task.Run(() => process.StandardError.ReadToEndAsync(), CancellationToken.None);

                try
                {
                    await process.StandardOutput.BaseStream.CopyToAsync(fs, token);
                }
                catch (OperationCanceledException) { }

                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }

                if (token.IsCancellationRequested || DateTime.UtcNow >= endTime) break;

                _logger.LogWarning($"[DVR] Stream dropped prematurely for Session {sessionId}. Reconnecting in 3 seconds...");
                await Task.Delay(3000, token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[DVR] Fatal error in resilient recording loop for Session {sessionId}");
            await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: "Internal recording loop failed.");
        }
        finally
        {
            _activeRecordings.TryRemove(sessionId, out _);
            await CompleteSessionAsync(sessionId, outputPath, repo, dvrManager, notifier);
        }
    }

    public async Task StopRecordingAsync(Guid sessionId)
    {
        if (_activeRecordings.TryGetValue(sessionId, out var activeRec))
        {
            _logger.LogInformation($"[DVR] Manually stopping recording session {sessionId}");
            activeRec.Cts.Cancel();

            if (activeRec.Process != null && !activeRec.Process.HasExited)
            {
                try { activeRec.Process.Kill(); } catch { }
            }
        }
    }

    private async Task CompleteSessionAsync(Guid sessionId, string outputPath, IIptvRepository repo, IDvrManager dvrManager, IClientNotifier notifier)
    {
        var session = await repo.GetSessionByIdAsync(sessionId);
        var fileInfo = new FileInfo(outputPath);

        if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024)
        {
            _logger.LogInformation($"[DVR] Recording {sessionId} completed.");
            await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Completed);
            if (session != null) await dvrManager.EnforceRetentionPolicyAsync(session.ScheduleId);
        }
        else
        {
            const string failureReason = "Stream exited prematurely or generated empty file.";
            _logger.LogWarning($"[DVR] Recording {sessionId} failed or file is too small.");
            await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: failureReason);

            using var scope = _serviceProvider.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<Vora.Application.Settings.ISystemSettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            if (settings.DvrNotifyOnFailure)
            {
                var alerts = scope.ServiceProvider.GetRequiredService<Vora.Application.Notifications.IAdminNotificationManager>();
                var title = session != null ? $"Recording failed: {session.Title}" : "Recording failed";
                await alerts.RaiseAsync(AdminNotificationSeverity.Error, title, failureReason, session != null ? $"{{\"sessionId\":\"{session.Id}\"}}" : null);
            }
        }

        await notifier.NotifyDvrSessionsUpdatedAsync();
    }
}