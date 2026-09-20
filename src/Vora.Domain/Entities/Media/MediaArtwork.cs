using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Media;

public class MediaArtwork
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Url { get; set; } = string.Empty;
    public ArtworkKind Kind { get; set; }
    public string? Language { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? VoteAverage { get; set; }

    public string ProviderId { get; set; } = string.Empty;
    public bool IsUserUploaded { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
