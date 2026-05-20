using System.Collections.Concurrent;

namespace Vora.Application.Media;

public class ServerPlaybackTracker : IServerPlaybackTracker
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<Guid, ServerPlaybackSessionVM> _sessions = new();

    public void Heartbeat(ServerPlaybackHeartbeat heartbeat)
    {
        var now = DateTime.UtcNow;
        _sessions.AddOrUpdate(
            heartbeat.ProfileId,
            _ => new ServerPlaybackSessionVM
            {
                ProfileId = heartbeat.ProfileId,
                ProfileName = heartbeat.ProfileName,
                ProfileImageUrl = heartbeat.ProfileImageUrl,
                TrackId = heartbeat.TrackId,
                TrackTitle = heartbeat.TrackTitle,
                Artist = heartbeat.Artist,
                AlbumTitle = heartbeat.AlbumTitle,
                AlbumArtworkUrl = heartbeat.AlbumArtworkUrl,
                DurationSeconds = heartbeat.DurationSeconds,
                CurrentTimeSeconds = heartbeat.CurrentTimeSeconds,
                StartedAt = now,
                LastHeartbeatAt = now
            },
            (_, existing) =>
            {
                if (existing.TrackId != heartbeat.TrackId)
                {
                    existing.StartedAt = now;
                }
                existing.ProfileName = heartbeat.ProfileName;
                existing.ProfileImageUrl = heartbeat.ProfileImageUrl;
                existing.TrackId = heartbeat.TrackId;
                existing.TrackTitle = heartbeat.TrackTitle;
                existing.Artist = heartbeat.Artist;
                existing.AlbumTitle = heartbeat.AlbumTitle;
                existing.AlbumArtworkUrl = heartbeat.AlbumArtworkUrl;
                existing.DurationSeconds = heartbeat.DurationSeconds;
                existing.CurrentTimeSeconds = heartbeat.CurrentTimeSeconds;
                existing.LastHeartbeatAt = now;
                return existing;
            });
    }

    public void Stop(Guid profileId)
    {
        _sessions.TryRemove(profileId, out _);
    }

    public List<ServerPlaybackSessionVM> GetActive(Guid? excludeProfileId)
    {
        PruneExpired();
        var query = _sessions.Values.AsEnumerable();
        if (excludeProfileId.HasValue)
        {
            query = query.Where(s => s.ProfileId != excludeProfileId.Value);
        }
        return query
            .OrderByDescending(s => s.LastHeartbeatAt)
            .ToList();
    }

    public int PruneExpired()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;
        var removed = 0;
        foreach (var kv in _sessions.ToArray())
        {
            if (kv.Value.LastHeartbeatAt < cutoff)
            {
                if (_sessions.TryRemove(kv.Key, out _)) removed++;
            }
        }
        return removed;
    }
}
