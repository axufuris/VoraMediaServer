using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Calendar.Dtos;

public class CalendarShowSourceDto
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public string? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentRating { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string UpcomingEpisodesJson { get; set; } = string.Empty;

    public static Expression<Func<TvShow, CalendarShowSourceDto>> Projection =>
        s => new CalendarShowSourceDto
        {
            Id = s.Id,
            LibraryId = s.LibraryId,
            TmdbId = s.TmdbId,
            Title = s.Title,
            ContentRating = s.ContentRating,
            PosterUrl = s.PosterUrl,
            BackgroundUrl = s.BackgroundUrl,
            UpcomingEpisodesJson = s.UpcomingEpisodesJson
        };
}
