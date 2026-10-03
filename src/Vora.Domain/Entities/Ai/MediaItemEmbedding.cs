using Vora.Domain.Entities.Media;

namespace Vora.Domain.Entities.Ai;

public class MediaItemEmbedding
{
    public Pgvector.Vector? Embedding { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public string? SourceHash { get; set; }
    public string? Model { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
