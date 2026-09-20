using System.Collections.Concurrent;

namespace Vora.Application.Iptv;

public enum TunerLeaseKind
{
    Live,
    Timeshift,
    Dvr
}

public interface ITunerRegistry
{
    bool TryAcquire(Guid playlistId, int maxConcurrent, string leaseKey, TunerLeaseKind kind);
    void Heartbeat(string leaseKey);
    void Release(string leaseKey);
    int ActiveCount(Guid playlistId);
    IReadOnlyList<string> EvictIdle(TunerLeaseKind kind, TimeSpan maxIdle);
}

public class TunerRegistry : ITunerRegistry
{
    private sealed class Lease
    {
        public required string Key { get; init; }
        public required Guid PlaylistId { get; init; }
        public required TunerLeaseKind Kind { get; init; }
        public DateTime LastSeenUtc { get; set; }
    }

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Lease> _leases = new();

    public bool TryAcquire(Guid playlistId, int maxConcurrent, string leaseKey, TunerLeaseKind kind)
    {
        lock (_gate)
        {
            if (_leases.TryGetValue(leaseKey, out var existing))
            {
                existing.LastSeenUtc = DateTime.UtcNow;
                return true;
            }

            if (maxConcurrent > 0)
            {
                var current = 0;
                foreach (var lease in _leases.Values)
                {
                    if (lease.PlaylistId == playlistId)
                    {
                        current++;
                    }
                }
                if (current >= maxConcurrent)
                {
                    return false;
                }
            }

            _leases[leaseKey] = new Lease
            {
                Key = leaseKey,
                PlaylistId = playlistId,
                Kind = kind,
                LastSeenUtc = DateTime.UtcNow
            };
            return true;
        }
    }

    public void Heartbeat(string leaseKey)
    {
        if (_leases.TryGetValue(leaseKey, out var lease))
        {
            lease.LastSeenUtc = DateTime.UtcNow;
        }
    }

    public void Release(string leaseKey)
    {
        _leases.TryRemove(leaseKey, out _);
    }

    public int ActiveCount(Guid playlistId)
    {
        var count = 0;
        foreach (var lease in _leases.Values)
        {
            if (lease.PlaylistId == playlistId)
            {
                count++;
            }
        }
        return count;
    }

    public IReadOnlyList<string> EvictIdle(TunerLeaseKind kind, TimeSpan maxIdle)
    {
        var cutoff = DateTime.UtcNow - maxIdle;
        var evicted = new List<string>();
        foreach (var lease in _leases.Values)
        {
            if (lease.Kind == kind && lease.LastSeenUtc < cutoff)
            {
                if (_leases.TryRemove(lease.Key, out _))
                {
                    evicted.Add(lease.Key);
                }
            }
        }
        return evicted;
    }
}
