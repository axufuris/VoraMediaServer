using System.Linq.Expressions;
using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Calendar.Dtos;

public class CalendarWatchlistSourceDto
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime? ExpectedReleaseDate { get; set; }

    public static Expression<Func<UserWatchlistItem, CalendarWatchlistSourceDto>> Projection =>
        w => new CalendarWatchlistSourceDto
        {
            Id = w.Id,
            ExternalId = w.ExternalId,
            ProviderId = w.ProviderId,
            Title = w.Title,
            Type = w.Type,
            PosterUrl = w.PosterUrl,
            ExpectedReleaseDate = w.ExpectedReleaseDate
        };
}
