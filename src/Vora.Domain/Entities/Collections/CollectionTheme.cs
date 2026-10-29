using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Collections;

public class CollectionTheme
{
    public Guid CollectionId { get; set; }
    public virtual Collection Collection { get; set; } = null!;

    public string Theme { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();

    public float[]? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
