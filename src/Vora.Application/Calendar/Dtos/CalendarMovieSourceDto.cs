using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Calendar.Dtos;

public class CalendarMovieSourceDto
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public string? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentRating { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public DateTime? TheatricalReleaseDate { get; set; }
    public DateTime? DigitalReleaseDate { get; set; }

    public static Expression<Func<Movie, CalendarMovieSourceDto>> Projection =>
        m => new CalendarMovieSourceDto
        {
            Id = m.Id,
            LibraryId = m.LibraryId,
            TmdbId = m.TmdbId,
            Title = m.Title,
            ContentRating = m.ContentRating,
            PosterUrl = m.PosterUrl,
            BackgroundUrl = m.BackgroundUrl,
            TheatricalReleaseDate = m.TheatricalReleaseDate,
            DigitalReleaseDate = m.DigitalReleaseDate
        };
}
