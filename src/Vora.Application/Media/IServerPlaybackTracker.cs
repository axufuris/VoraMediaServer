namespace Vora.Application.Media;

public interface IServerPlaybackTracker
{
    void Heartbeat(ServerPlaybackHeartbeat heartbeat);
    void Stop(Guid profileId);
    List<ServerPlaybackSessionVM> GetActive(Guid? excludeProfileId);
    int PruneExpired();
}

public sealed class ServerPlaybackHeartbeat
{
    public required Guid ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public required Guid TrackId { get; init; }
    public required string TrackTitle { get; init; }
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public string? AlbumArtworkUrl { get; init; }
    public int? DurationSeconds { get; init; }
    public double? CurrentTimeSeconds { get; init; }
}

public sealed class ServerPlaybackSessionVM
{
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public Guid TrackId { get; set; }
    public string TrackTitle { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? AlbumTitle { get; set; }
    public string? AlbumArtworkUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public double? CurrentTimeSeconds { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
}
