namespace Vora.Domain.Entities.Media;

public class TvShow : MediaItem
{
    public string? TvType { get; set; }
    public bool? InProduction { get; set; }
    public int? NumberOfSeasons { get; set; }
    public int? NumberOfEpisodes { get; set; }

    public DateTime? LastAirDate { get; set; }
    public DateTime? NextAirDate { get; set; }
    public string? LastEpisodeToAirName { get; set; }
    public string? NextEpisodeToAirName { get; set; }

    public string UpcomingEpisodesJson { get; set; } = "[]";

    public virtual ICollection<Season> Seasons { get; set; } = new List<Season>();
    public virtual ICollection<Network> Networks { get; set; } = new List<Network>();
}
