namespace Vora.Domain.Entities.Media;

public class Network
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? LogoPath { get; set; }
    public string? OriginCountry { get; set; }

    public virtual ICollection<TvShow> TvShows { get; set; } = new List<TvShow>();
}
