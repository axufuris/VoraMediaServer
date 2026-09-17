using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Requests;

public class MediaRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    public string ExternalId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public DateTime? ExpectedReleaseDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Guid? AssignedServerId { get; set; }
    public virtual RequestServer? AssignedServer { get; set; }

    public virtual ICollection<MediaRequestUser> Requesters { get; set; } = new List<MediaRequestUser>();
}
