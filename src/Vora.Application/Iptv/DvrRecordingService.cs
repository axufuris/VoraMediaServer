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
    Task<bool> StartRecordingAsync(Guid sessionId);
    Task StopRecordingAsync(Guid sessionId);
}

public class DvrRecordingService : IDvrRecordingService
{
    private sealed class ActiveRecording : IDisposable
    {
        private readonly object _gate = new();
        private readonly ILogger _logger;
        private Process? _process;
        private bool _stopRequested;

        public ActiveRecording(ILogger logger) => _logger = logger;

        public CancellationTokenSource Cts { get; } = new();

        public bool StopRequested
        {
            get { lock (_gate) { return _stopRequested; } }
        }

        public void AttachProcess(Process process)
        {
            lock (_gate)
            {
                _process = process;
            }
        }

        public void EndCurrentProcess()
        {
            lock (_gate)
            {
                KillAndDisposeLocked();
            }
        }

        public void RequestStop()
        {
            Cts.Cancel();
            lock (_gate)
            {
                _stopRequested = true;
                KillAndDisposeLocked();
            }
        }

        private void KillAndDisposeLocked()
        {
            var process = _process;
            if (process is null) return;
            _process = null;

            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DVR] Failed to kill ffmpeg process during teardown.");
            }

            process.Dispose();
        }

        public void Dispose()
        {
            EndCurrentProcess();
            Cts.Dispose();
        }
    }

    private readonly ConcurrentDictionary<Guid, ActiveRecording> _activeRecordings = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ITunerRegistry _tunerRegistry;
    private readonly StoragePathsOptions _storagePaths;
    private readonly ILogger<DvrRecordingService> _logger;

    public DvrRecordingService(IServiceProvider serviceProvider, ITunerRegistry tunerRegistry, IOptions<StoragePathsOptions> storagePaths, ILogger<DvrRecordingService> logger)
    {
        _serviceProvider = serviceProvider;
        _tunerRegistry = tunerRegistry;
        _storagePaths = storagePaths.Value;
        _logger = logger;
    }

    private static string DvrLeaseKey(Guid sessionId) => $"dvr:{sessionId}";

    public async Task<bool> StartRecordingAsync(Guid sessionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();

        var session = await repo.GetSessionByIdAsync(sessionId);
        if (session == null || session.Schedule?.Channel?.StreamUrl == null)
        {
            await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: "Invalid session or missing stream URL.");
            return false;
        }

        var user = await repo.GetUserWithQuotaAsync(session.Schedule.UserId);
        if (user.DvrStorageQuotaBytes > 0)
        {
            long currentUsage = await repo.GetDvrUsageBytesAsync(user.Id);
            if (currentUsage >= user.DvrStorageQuotaBytes)
            {
                await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: "DVR Storage Quota exceeded.");
                return false;
            }
        }

        var settingsRepo = scope.ServiceProvider.GetRequiredService<Vora.Application.Settings.ISystemSettingsRepository>();
        var settings = await settingsRepo.GetSettingsAsync();

        string dvrBaseDir = !string.IsNullOrWhiteSpace(settings.DvrStoragePath)
            ? settings.DvrStoragePath
            : (_storagePaths.IptvDvr ?? "/app/data/iptv/dvr");
        if (!Directory.Exists(dvrBaseDir)) Directory.CreateDirectory(dvrBaseDir);

        string safeTitle = string.Join("_", session.Title.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        string fileName = $"{safeTitle}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.ts";
        string outputPath = Path.Combine(dvrBaseDir, fileName);

        var activeRec = new ActiveRecording(_logger);
        if (!_activeRecordings.TryAdd(sessionId, activeRec))
        {
            activeRec.Dispose();
            _logger.LogWarning($"[DVR] Recording session {sessionId} is already active.");
            return false;
        }

        var playlistId = session.Schedule.Channel.PlaylistId;
        var tunerProfile = await repo.GetTunerProfileByPlaylistIdAsync(playlistId);
        var maxConcurrent = tunerProfile?.MaxConcurrentStreams ?? 0;
        if (!_tunerRegistry.TryAcquire(playlistId, maxConcurrent, DvrLeaseKey(sessionId), TunerLeaseKind.Dvr))
        {
            _activeRecordings.TryRemove(sessionId, out _);
            activeRec.Dispose();
            _logger.LogWarning($"[DVR] No tuners available on playlist for session {sessionId}. Marking as conflict.");
            await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Conflict, errorMessage: "No tuners available at start time.");
            return false;
        }

        _ = Task.Run(() => RecordingLoopAsync(sessionId, session.Schedule.Channel.StreamUrl, outputPath, session.EndTime, activeRec));

        await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Recording, outputPath: outputPath);
        var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();
        await notifier.NotifyDvrSessionsUpdatedAsync();
        return true;
    }

    private async Task RecordingLoopAsync(Guid sessionId, string streamUrl, string outputPath, DateTime endTime, ActiveRecording activeRec)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
            var dvrManager = scope.ServiceProvider.GetRequiredService<IDvrManager>();
            var notifier = scope.ServiceProvider.GetRequiredService<IClientNotifier>();

            try
            {
                await RunRecordingAsync(sessionId, streamUrl, outputPath, endTime, activeRec);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"[DVR] Recording session {sessionId} was cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DVR] Fatal error in resilient recording loop for Session {sessionId}");
                await repo.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Failed, errorMessage: "Internal recording loop failed.");
            }
            finally
            {
                await CompleteSessionAsync(sessionId, outputPath, repo, dvrManager, notifier);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[DVR] Unexpected failure finalizing recording session {sessionId}");
        }
        finally
        {
            _tunerRegistry.Release(DvrLeaseKey(sessionId));
            _activeRecordings.TryRemove(sessionId, out _);
            activeRec.Dispose();
        }
    }

    private async Task RunRecordingAsync(Guid sessionId, string streamUrl, string outputPath, DateTime endTime, ActiveRecording activeRec)
    {
        var token = activeRec.Cts.Token;
        using var fs = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.Read);

        while (DateTime.UtcNow < endTime && !token.IsCancellationRequested)
        {
            var process = new Process { StartInfo = BuildRecordingProcessInfo(streamUrl) };
            activeRec.AttachProcess(process);
            process.Start();

            var stderrTask = DrainStandardErrorAsync(process, token);

            try
            {
                await process.StandardOutput.BaseStream.CopyToAsync(fs, token);
            }
            catch (OperationCanceledException) { }

            activeRec.EndCurrentProcess();
            var stderr = await stderrTask;

            if (token.IsCancellationRequested || DateTime.UtcNow >= endTime) break;

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.LogDebug($"[DVR] ffmpeg exited for session {sessionId}: {stderr}");
            }

            _logger.LogWarning($"[DVR] Stream dropped prematurely for Session {sessionId}. Reconnecting in 3 seconds...");
            await Task.Delay(3000, token);
        }
    }

    private static ProcessStartInfo BuildRecordingProcessInfo(string streamUrl)
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
        return psi;
    }

    private static async Task<string> DrainStandardErrorAsync(Process process, CancellationToken token)
    {
        try
        {
            return await process.StandardError.ReadToEndAsync(token);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public Task StopRecordingAsync(Guid sessionId)
    {
        if (_activeRecordings.TryGetValue(sessionId, out var activeRec))
        {
            _logger.LogInformation($"[DVR] Manually stopping recording session {sessionId}");
            activeRec.RequestStop();
        }

        return Task.CompletedTask;
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
