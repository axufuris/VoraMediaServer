namespace Vora.Plugins.Dtos;

public class RemoteRatingDto
{
    public required RemoteExternalIdsDto ExternalIds { get; set; }
    public required RemoteMediaKind Kind { get; set; }
    public decimal Rating { get; set; }
    public DateTime? RatedAt { get; set; }
}
