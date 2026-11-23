using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Vora.Application.Iptv;

public enum LiveSessionKind
{
    Timeshift = 0,
    Remux = 1
}

public sealed record LiveSessionShare(Guid ChannelId, LiveVideoEncode? Encode, LiveVideoFormat? Source);

public sealed record AttachResult(bool Attached, Guid? StoppedSessionKey);

public sealed record SharedLiveSession(Guid SessionKey, string SessionPath, LiveVideoEncode? Encode, LiveVideoFormat? Source, DateTime StartedAtUtc, int Viewers);

public interface ITimeshiftCoordinator : IEncodeSource
{
    bool TryRegister(Guid sessionKey, Process process, string sessionPath, LiveSessionKind kind = LiveSessionKind.Timeshift, RunningEncode? encode = null, LiveSessionShare? share = null);
    bool IsActive(Guid sessionKey);
    LiveSessionKind? KindOf(Guid sessionKey);
    void Heartbeat(Guid sessionKey);
    Task StopAsync(Guid sessionKey);
    void SetSource(Guid sessionKey, LiveVideoFormat? source);
    IReadOnlyList<SharedLiveSession> SharedSessions(Guid channelId, LiveSessionKind kind);
    Task<IReadOnlyList<Guid>> StopExitedAsync(Guid channelId, LiveSessionKind kind);
    Task<AttachResult> AttachAsync(Guid viewerKey, Guid sessionKey);
    Task<Guid?> DetachAsync(Guid viewerKey);
    LiveSessionKind? KindOfViewer(Guid viewerKey);
    Guid? SessionOfViewer(Guid viewerKey);
    void HeartbeatViewer(Guid viewerKey);
    Task<IAsyncDisposable> LockProfileAsync(Guid profileId);
    Task<IAsyncDisposable> LockChannelAsync(Guid channelId, LiveSessionKind kind);
    Task<IReadOnlyList<Guid>> EvictStaleSessionsAsync(TimeSpan maxIdleDuration, TimeSpan maxSessionLifetime, TimeSpan? remuxMaxIdle = null);
    Task ReapOrphanedDirectoriesAsync(string timeshiftRoot, TimeSpan maxIdleAge);
}

public class TimeshiftCoordinator : ITimeshiftCoordinator
{
    public const string TimeshiftSubdirectory = "timeshift";

    public static readonly TimeSpan LiveRemuxMaxIdle = TimeSpan.FromSeconds(60);

    private const int ProcessQuitTimeoutMs = 2000;
    private const int DirectoryDeleteRetries = 3;
    private const int DirectoryDeleteRetryDelayMs = 100;

    private readonly object _sync = new();
    private readonly Dictionary<Guid, ActiveSession> _sessions = new();
    private readonly Dictionary<Guid, Guid> _viewerSessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _profileLocks = new();
    private readonly ConcurrentDictionary<(Guid ChannelId, LiveSessionKind Kind), SemaphoreSlim> _channelLocks = new();
    private readonly ILogger<TimeshiftCoordinator> _logger;

    public TimeshiftCoordinator(ILogger<TimeshiftCoordinator> logger)
    {
        _logger = logger;
    }

    public bool TryRegister(Guid sessionKey, Process process, string sessionPath, LiveSessionKind kind = LiveSessionKind.Timeshift, RunningEncode? encode = null, LiveSessionShare? share = null)
    {
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            if (_sessions.ContainsKey(sessionKey)) return false;

            _sessions[sessionKey] = new ActiveSession(process, sessionPath, kind, encode, share)
            {
                StartedAtUtc = now,
                LastHeartbeatUtc = now,
                Source = share?.Source
            };
            return true;
        }
    }

    public bool IsActive(Guid sessionKey)
    {
        lock (_sync) return _sessions.ContainsKey(sessionKey);
    }

    public IReadOnlyList<RunningEncode> RunningEncodes(Guid except)
    {
        lock (_sync)
        {
            return _sessions
                .Where(session => session.Key != except && session.Value.Encode is not null && IsRunning(session.Value.Process))
                .Select(session => session.Value.Encode!)
                .ToList();
        }
    }

    private static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public LiveSessionKind? KindOf(Guid sessionKey)
    {
        lock (_sync) return _sessions.TryGetValue(sessionKey, out var session) ? session.Kind : null;
    }

    public void Heartbeat(Guid sessionKey)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(sessionKey, out var session))
            {
                session.LastHeartbeatUtc = DateTime.UtcNow;
            }
        }
    }

    public Task StopAsync(Guid sessionKey)
    {
        ActiveSession? session;
        lock (_sync) session = Remove(sessionKey);

        if (session is not null) Release(session, sessionKey);
        return Task.CompletedTask;
    }

    public void SetSource(Guid sessionKey, LiveVideoFormat? source)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(sessionKey, out var session))
            {
                session.Source = source;
            }
        }
    }

    public IReadOnlyList<SharedLiveSession> SharedSessions(Guid channelId, LiveSessionKind kind)
    {
        lock (_sync)
        {
            return _sessions
                .Where(session => session.Value.Share?.ChannelId == channelId && session.Value.Kind == kind && IsRunning(session.Value.Process))
                .Select(session => new SharedLiveSession(
                    session.Key, session.Value.SessionPath, session.Value.Share!.Encode, session.Value.Source, session.Value.StartedAtUtc, session.Value.Viewers.Count))
                .ToList();
        }
    }

    public Task<IReadOnlyList<Guid>> StopExitedAsync(Guid channelId, LiveSessionKind kind)
    {
        var exited = new List<(Guid Key, ActiveSession Session)>();
        lock (_sync)
        {
            foreach (var session in _sessions.Where(s => s.Value.Share?.ChannelId == channelId && s.Value.Kind == kind && !IsRunning(s.Value.Process)).ToList())
            {
                exited.Add((session.Key, Remove(session.Key)!));
            }
        }

        foreach (var (key, session) in exited) Release(session, key);
        return Task.FromResult<IReadOnlyList<Guid>>(exited.Select(s => s.Key).ToList());
    }

    public Task<AttachResult> AttachAsync(Guid viewerKey, Guid sessionKey)
    {
        ActiveSession? stopped = null;
        var stoppedKey = Guid.Empty;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionKey, out var session)) return Task.FromResult(new AttachResult(false, null));

            if (_viewerSessions.TryGetValue(viewerKey, out var previous) && previous != sessionKey && _sessions.TryGetValue(previous, out var left))
            {
                left.Viewers.Remove(viewerKey);
                if (left.Viewers.Count == 0)
                {
                    stopped = Remove(previous);
                    stoppedKey = previous;
                }
            }

            session.Viewers.Add(viewerKey);
            _viewerSessions[viewerKey] = sessionKey;
        }

        if (stopped is not null) Release(stopped, stoppedKey);
        return Task.FromResult(new AttachResult(true, stopped is null ? null : stoppedKey));
    }

    public Task<Guid?> DetachAsync(Guid viewerKey)
    {
        ActiveSession? stopped;
        Guid sessionKey;
        lock (_sync)
        {
            if (!_viewerSessions.Remove(viewerKey, out sessionKey)) return Task.FromResult<Guid?>(null);
            if (!_sessions.TryGetValue(sessionKey, out var session)) return Task.FromResult<Guid?>(null);

            session.Viewers.Remove(viewerKey);
            if (session.Viewers.Count > 0) return Task.FromResult<Guid?>(null);

            stopped = Remove(sessionKey);
        }

        if (stopped is null) return Task.FromResult<Guid?>(null);

        Release(stopped, sessionKey);
        return Task.FromResult<Guid?>(sessionKey);
    }

    public LiveSessionKind? KindOfViewer(Guid viewerKey)
    {
        lock (_sync)
        {
            return _viewerSessions.TryGetValue(viewerKey, out var sessionKey) && _sessions.TryGetValue(sessionKey, out var session) ? session.Kind : null;
        }
    }

    public Guid? SessionOfViewer(Guid viewerKey)
    {
        lock (_sync) return _viewerSessions.TryGetValue(viewerKey, out var sessionKey) ? sessionKey : null;
    }

    public void HeartbeatViewer(Guid viewerKey)
    {
        lock (_sync)
        {
            if (_viewerSessions.TryGetValue(viewerKey, out var sessionKey) && _sessions.TryGetValue(sessionKey, out var session))
            {
                session.LastHeartbeatUtc = DateTime.UtcNow;
            }
        }
    }

    private ActiveSession? Remove(Guid sessionKey)
    {
        if (!_sessions.Remove(sessionKey, out var session)) return null;

        foreach (var viewer in session.Viewers) _viewerSessions.Remove(viewer);
        return session;
    }

    private void Release(ActiveSession session, Guid sessionKey)
    {
        TerminateProcess(session.Process, sessionKey);
        TryDeleteSessionDirectory(session.SessionPath, sessionKey);
    }

    public async Task<IAsyncDisposable> LockProfileAsync(Guid profileId)
    {
        var gate = _profileLocks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new LockReleaser(gate);
    }

    public async Task<IAsyncDisposable> LockChannelAsync(Guid channelId, LiveSessionKind kind)
    {
        var gate = _channelLocks.GetOrAdd((channelId, kind), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new LockReleaser(gate);
    }

    private sealed class LockReleaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public LockReleaser(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
            return ValueTask.CompletedTask;
        }
    }

    public Task<IReadOnlyList<Guid>> EvictStaleSessionsAsync(TimeSpan maxIdleDuration, TimeSpan maxSessionLifetime, TimeSpan? remuxMaxIdle = null)
    {
        var now = DateTime.UtcNow;
        var evicted = new List<(Guid Key, ActiveSession Session, bool Idle, bool Expired)>();
        lock (_sync)
        {
            foreach (var kvp in _sessions.ToList())
            {
                var idleLimit = kvp.Value.Kind == LiveSessionKind.Remux ? remuxMaxIdle ?? LiveRemuxMaxIdle : maxIdleDuration;
                var isIdle = now - kvp.Value.LastHeartbeatUtc > idleLimit;
                var exceededLifetime = now - kvp.Value.StartedAtUtc > maxSessionLifetime;
                if (!isIdle && !exceededLifetime) continue;

                evicted.Add((kvp.Key, Remove(kvp.Key)!, isIdle, exceededLifetime));
            }
        }

        foreach (var (key, session, isIdle, exceededLifetime) in evicted)
        {
            Release(session, key);
            _logger.LogInformation(
                "Evicted {Kind} session {SessionKey} (idle: {Idle}, exceededLifetime: {ExceededLifetime}).",
                session.Kind, key, isIdle, exceededLifetime);
        }
        return Task.FromResult<IReadOnlyList<Guid>>(evicted.Select(e => e.Key).ToList());
    }

    public Task ReapOrphanedDirectoriesAsync(string timeshiftRoot, TimeSpan maxIdleAge)
    {
        if (!Directory.Exists(timeshiftRoot)) return Task.CompletedTask;

        HashSet<Guid> activeProfileIds;
        lock (_sync) activeProfileIds = new HashSet<Guid>(_sessions.Keys);
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

    private sealed record ActiveSession(Process Process, string SessionPath, LiveSessionKind Kind, RunningEncode? Encode, LiveSessionShare? Share)
    {
        public required DateTime StartedAtUtc { get; init; }
        public required DateTime LastHeartbeatUtc { get; set; }
        public LiveVideoFormat? Source { get; set; }
        public HashSet<Guid> Viewers { get; } = [];
    }
}
