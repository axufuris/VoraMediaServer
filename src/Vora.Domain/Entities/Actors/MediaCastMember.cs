using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Actors;

public class MediaCastMember
{
    public string? CharacterName { get; set; }
    public MediaCastRole Roles { get; set; } = MediaCastRole.Actor;
    public int Order { get; set; }

    public Guid ActorId { get; set; }
    public virtual Actor Actor { get; set; } = null!;

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
