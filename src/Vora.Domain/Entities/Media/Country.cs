using System.ComponentModel.DataAnnotations;

namespace Vora.Domain.Entities.Media;

public class Country
{
    [Key]
    public required string Iso3166_1 { get; set; }
    public string? Name { get; set; }

    public virtual ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
}
