using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Media;

public class MediaExtra
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;
    public MediaExtraType ExtraType { get; set; }

    // The parent this extra belongs to (Movie, TvShow, or Episode).
    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;

    // The extra's own file(s), carrying FilePath + ffprobe-analyzed tracks so
    // it streams through the same pipeline as a media item.
    public virtual ICollection<MediaPart> Parts { get; set; } = new List<MediaPart>();
}
