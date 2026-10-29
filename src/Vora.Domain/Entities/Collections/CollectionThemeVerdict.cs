using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;

namespace Vora.Domain.Entities.Collections;

public class CollectionThemeVerdict
{
    public Guid CollectionId { get; set; }
    public virtual Collection Collection { get; set; } = null!;

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;

    public bool Belongs { get; set; }
    public DateTime JudgedAt { get; set; } = DateTime.UtcNow;
}
