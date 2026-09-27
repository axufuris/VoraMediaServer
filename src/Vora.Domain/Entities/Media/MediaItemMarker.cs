namespace Vora.Domain.Entities.Media;

public enum MarkerType
{
    Intro,
    Recap,
    Preview,
    Credits,
    CreditsScene
}

public class MediaItemMarker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MarkerType Type { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public int Order { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
