using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;

namespace Vora.Domain.Entities.Collections;

public class CollectionItem
{
    public decimal SortOrder { get; set; }
    public double? InUniverseYear { get; set; }
    public bool InUniverseYearLocked { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public Guid CollectionId { get; set; }
    public virtual Collection Collection { get; set; } = null!;

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
