using System.Linq.Expressions;
using Vora.Domain.Entities.Requests;

namespace Vora.Application.Calendar.Dtos;

public class CalendarRequestSourceDto
{
    public Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime? ExpectedReleaseDate { get; set; }

    public static Expression<Func<MediaRequest, CalendarRequestSourceDto>> Projection =>
        r => new CalendarRequestSourceDto
        {
            Id = r.Id,
            ExternalId = r.ExternalId,
            ProviderId = r.ProviderId,
            Title = r.Title,
            Type = r.Type,
            PosterUrl = r.PosterUrl,
            ExpectedReleaseDate = r.ExpectedReleaseDate
        };
}
