namespace Vora.Domain.Entities.Media;

public class Company
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? LogoPath { get; set; }
    public string? OriginCountry { get; set; }

    public virtual ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
}
