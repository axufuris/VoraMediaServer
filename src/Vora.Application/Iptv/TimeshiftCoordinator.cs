using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Vora.Application.Iptv;

public interface ITimeshiftCoordinator
{
    bool TryRegister(Guid profileId, Process process);
    bool IsActive(Guid profileId);
    void Heartbeat(Guid profileId);
    Task StopAsync(Guid profileId);
    Task EvictStaleSessionsAsync(TimeSpan maxIdleDuration);
}

public class TimeshiftCoordinator : ITimeshiftCoordinator
{
    private const int ProcessQuitTimeoutMs = 2000;

    private readonly ConcurrentDictionary<Guid, Process> _activeTimeshifts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _heartbeats = new();
    private readonly ILogger<TimeshiftCoordinator> _logger;

    public TimeshiftCoordinator(ILogger<TimeshiftCoordinator> logger)
    {
        _logger = logger;
    }

    public bool TryRegister(Guid profileId, Process process)
    {
        if (!_activeTimeshifts.TryAdd(profileId, process)) return false;
        _heartbeats[profileId] = DateTime.UtcNow;
        return true;
    }

    public bool IsActive(Guid profileId) => _activeTimeshifts.ContainsKey(profileId);

    public void Heartbeat(Guid profileId)
    {
        if (_activeTimeshifts.ContainsKey(profileId))
        {
            _heartbeats[profileId] = DateTime.UtcNow;
        }
    }

    public Task StopAsync(Guid profileId)
    {
        if (_activeTimeshifts.TryRemove(profileId, out var process))
        {
            TerminateProcess(process, profileId);
        }
        _heartbeats.TryRemove(profileId, out _);
        return Task.CompletedTask;
    }

    public Task EvictStaleSessionsAsync(TimeSpan maxIdleDuration)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _activeTimeshifts)
        {
            var profileId = kvp.Key;
            if (!_heartbeats.TryGetValue(profileId, out var lastBeat)) continue;
            if (now - lastBeat <= maxIdleDuration) continue;

            if (_activeTimeshifts.TryRemove(profileId, out var process))
            {
                TerminateProcess(process, profileId);
            }
            _heartbeats.TryRemove(profileId, out _);
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
}
