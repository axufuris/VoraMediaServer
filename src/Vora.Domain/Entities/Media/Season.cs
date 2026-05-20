namespace Vora.Domain.Entities.Media;

public class Season : MediaItem
{
    public int SeasonNumber { get; set; }
    public int? EpisodeCount { get; set; }
    public decimal? VoteAverage { get; set; }

    public Guid TvShowId { get; set; }
    public virtual TvShow TvShow { get; set; } = null!;

    public virtual ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}
