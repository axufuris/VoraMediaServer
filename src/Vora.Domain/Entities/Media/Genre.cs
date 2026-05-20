namespace Vora.Domain.Entities.Media;

public class Genre
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public virtual ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
}
