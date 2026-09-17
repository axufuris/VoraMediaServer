using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Collections;

public class CollectionArtwork
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

    public Guid CollectionId { get; set; }
    public virtual Collection Collection { get; set; } = null!;
}
