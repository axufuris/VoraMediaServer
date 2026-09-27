using System.Linq.Expressions;
using Vora.Domain.Entities.Requests;

namespace Vora.Application.Requests.ViewModels;

public class MediaRequestVM
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public IEnumerable<MediaRequestUserVM> Requesters { get; set; } = new List<MediaRequestUserVM>();

    public static Expression<Func<MediaRequest, MediaRequestVM>> Projection =>
        r => new MediaRequestVM
        {
            Id = r.Id,
            ExternalId = r.ExternalId,
            Type = r.Type,
            Title = r.Title,
            PosterUrl = r.PosterUrl,
            Status = (int)r.Status,
            ProviderId = r.ProviderId,
            CreatedAt = r.CreatedAt,
            Requesters = r.Requesters.Select(req => new MediaRequestUserVM
            {
                ProfileId = req.ProfileId,
                RequestedAt = req.RequestedAt,
                Profile = new UserProfileSimpleVM
                {
                    Name = req.Profile.Name,
                    ProfileImageUrl = req.Profile.ProfileImageUrl
                }
            })
        };
}
