namespace Vora.Plugins.Dtos;

public class RemoteWatchStateDto
{
    public required RemoteExternalIdsDto ExternalIds { get; set; }
    public RemoteExternalIdsDto? EpisodeIds { get; set; }
    public required RemoteMediaKind Kind { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public bool IsPlayed { get; set; }
    public double ResumePositionSeconds { get; set; }
    public DateTime? LastPlayedAt { get; set; }
}
