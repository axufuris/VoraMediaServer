namespace Vora.Domain.Entities.Media;

public class MediaItemAnalysis
{
    public Guid Id { get; set; }

    public TimeSpan? Duration { get; set; }
    public TimeSpan? IntroStart { get; set; }
    public TimeSpan? IntroEnd { get; set; }
    public TimeSpan? CreditsStart { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
