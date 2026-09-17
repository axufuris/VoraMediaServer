namespace Vora.Domain.Entities.Media;

public class Movie : MediaItem
{
    public Guid? MovieGroupId { get; set; }
    public long? Budget { get; set; }
    public long? Revenue { get; set; }
    public DateTime? TheatricalReleaseDate { get; set; }
    public DateTime? DigitalReleaseDate { get; set; }
}
