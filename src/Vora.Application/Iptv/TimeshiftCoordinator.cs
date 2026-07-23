using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Vora.Application.Iptv;

public interface ITimeshiftCoordinator
{
    bool TryRegister(Guid profileId, Process process, string sessionPath);
    bool IsActive(Guid profileId);
    void Heartbeat(Guid profileId);
    Task StopAsync(Guid profileId);
    Task<IAsyncDisposable> LockProfileAsync(Guid profileId);
    Task<IReadOnlyList<Guid>> EvictStaleSessionsAsync(TimeSpan maxIdleDuration, TimeSpan maxSessionLifetime);
    Task ReapOrphanedDirectoriesAsync(string timeshiftRoot, TimeSpan maxIdleAge);
}

public class TimeshiftCoordinator : ITimeshiftCoordinator
{
    public const string TimeshiftSubdirectory = "timeshift";

    private const int ProcessQuitTimeoutMs = 2000;
    private const int DirectoryDeleteRetries = 3;
    private const int DirectoryDeleteRetryDelayMs = 100;

    private readonly ConcurrentDictionary<Guid, ActiveSession> _activeTimeshifts = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _profileLocks = new();
    private readonly ILogger<TimeshiftCoordinator> _logger;

    public TimeshiftCoordinator(ILogger<TimeshiftCoordinator> logger)
    {
        _logger = logger;
    }

    public bool TryRegister(Guid profileId, Process process, string sessionPath)
    {
        var now = DateTime.UtcNow;
        return _activeTimeshifts.TryAdd(profileId, new ActiveSession(process, sessionPath)
        {
            StartedAtUtc = now,
            LastHeartbeatUtc = now
        });
    }

    public bool IsActive(Guid profileId) => _activeTimeshifts.ContainsKey(profileId);

    public void Heartbeat(Guid profileId)
    {
        if (_activeTimeshifts.TryGetValue(profileId, out var session))
        {
            session.LastHeartbeatUtc = DateTime.UtcNow;
        }
    }

    public Task StopAsync(Guid profileId)
    {
        if (_activeTimeshifts.TryRemove(profileId, out var session))
        {
            TerminateProcess(session.Process, profileId);
            TryDeleteSessionDirectory(session.SessionPath, profileId);
        }
        return Task.CompletedTask;
    }

    public async Task<IAsyncDisposable> LockProfileAsync(Guid profileId)
    {
        var gate = _profileLocks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new ProfileLockReleaser(gate);
    }

    private sealed class ProfileLockReleaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public ProfileLockReleaser(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
            return ValueTask.CompletedTask;
        }
    }

    public Task<IReadOnlyList<Guid>> EvictStaleSessionsAsync(TimeSpan maxIdleDuration, TimeSpan maxSessionLifetime)
    {
        var now = DateTime.UtcNow;
        var evicted = new List<Guid>();
        foreach (var kvp in _activeTimeshifts)
        {
            var profileId = kvp.Key;
            var isIdle = now - kvp.Value.LastHeartbeatUtc > maxIdleDuration;
            var exceededLifetime = now - kvp.Value.StartedAtUtc > maxSessionLifetime;
            if (!isIdle && !exceededLifetime) continue;

            if (_activeTimeshifts.TryRemove(profileId, out var session))
            {
                TerminateProcess(session.Process, profileId);
                TryDeleteSessionDirectory(session.SessionPath, profileId);
                evicted.Add(profileId);
                _logger.LogInformation(
                    "Evicted timeshift session for profile {ProfileId} (idle: {Idle}, exceededLifetime: {ExceededLifetime}).",
                    profileId, isIdle, exceededLifetime);
            }
        }
        return Task.FromResult<IReadOnlyList<Guid>>(evicted);
    }

    public Task ReapOrphanedDirectoriesAsync(string timeshiftRoot, TimeSpan maxIdleAge)
    {
        if (!Directory.Exists(timeshiftRoot)) return Task.CompletedTask;

        var activeProfileIds = new HashSet<Guid>(_activeTimeshifts.Keys);
        var cutoffUtc = DateTime.UtcNow - maxIdleAge;

        string[] profileDirectories;
        try
        {
            profileDirectories = Directory.GetDirectories(timeshiftRoot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate timeshift root {Root}.", timeshiftRoot);
            return Task.CompletedTask;
        }

        foreach (var profileDir in profileDirectories)
        {
            var dirName = Path.GetFileName(profileDir);
            if (!Guid.TryParse(dirName, out var profileId))
            {
                continue;
            }

            if (activeProfileIds.Contains(profileId))
            {
                continue;
            }

            var latestActivity = GetLatestActivityUtc(profileDir);
            if (latestActivity > cutoffUtc)
            {
                continue;
            }

            try
            {
                Directory.Delete(profileDir, recursive: true);
                _logger.LogInformation(
                    "Reaped orphan timeshift directory for profile {ProfileId} (last touched {LastTouched:o}).",
                    profileId, latestActivity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reap orphan timeshift directory {Path}.", profileDir);
            }
        }

        return Task.CompletedTask;
    }

    private void TerminateProcess(Process process, Guid profileId)
    {
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.WriteLine("q");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not send quit signal to timeshift process for {ProfileId}.", profileId);
                }

                if (!process.WaitForExit(ProcessQuitTimeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to kill timeshift process for {ProfileId}.", profileId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while terminating timeshift process for {ProfileId}.", profileId);
        }
        finally
        {
            process.Dispose();
        }
    }

    private void TryDeleteSessionDirectory(string sessionPath, Guid profileId)
    {
        if (string.IsNullOrWhiteSpace(sessionPath)) return;

        for (var attempt = 0; attempt < DirectoryDeleteRetries; attempt++)
        {
            try
            {
                if (Directory.Exists(sessionPath))
                {
                    Directory.Delete(sessionPath, recursive: true);
                }

                var parent = Directory.GetParent(sessionPath);
                if (parent is not null && parent.Exists && !parent.EnumerateFileSystemInfos().Any())
                {
                    parent.Delete();
                }
                return;
            }
            catch (IOException) when (attempt < DirectoryDeleteRetries - 1)
            {
                Thread.Sleep(DirectoryDeleteRetryDelayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete timeshift session directory {Path} for profile {ProfileId}.",
                    sessionPath, profileId);
                return;
            }
        }
    }

    private static DateTime GetLatestActivityUtc(string path)
    {
        try
        {
            var newest = DateTime.MinValue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite > newest) newest = lastWrite;
            }
            if (newest == DateTime.MinValue)
            {
                newest = Directory.GetLastWriteTimeUtc(path);
            }
            return newest;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private sealed record ActiveSession(Process Process, string SessionPath)
    {
        public required DateTime StartedAtUtc { get; init; }
        public required DateTime LastHeartbeatUtc { get; set; }
    }
}
