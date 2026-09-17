using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Requests;

public class MediaRequestUser
{
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedAt { get; set; }

    public Guid RequestId { get; set; }
    public virtual MediaRequest Request { get; set; } = null!;

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;
}
