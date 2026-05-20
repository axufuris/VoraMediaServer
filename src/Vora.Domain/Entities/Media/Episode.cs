namespace Vora.Domain.Entities.Media;

public class Episode : MediaItem
{
    public int EpisodeNumber { get; set; }

    public Guid SeasonId { get; set; }
    public virtual Season Season { get; set; } = null!;
}
