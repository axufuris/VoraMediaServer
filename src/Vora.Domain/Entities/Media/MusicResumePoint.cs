using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Media;

public class MusicResumePoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public MusicPlaySource Source { get; set; }
    public Guid SourceId { get; set; }

    public List<Guid> TrackIds { get; set; } = new();
    public int TrackIndex { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
