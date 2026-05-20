using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Search.ViewModels;

public class MediaSearchResultVM
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? ContentRating { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }

    public static Expression<Func<MediaItem, MediaSearchResultVM>> Projection =>
        m => new MediaSearchResultVM
        {
            Id = m.Id,
            Type = m is Movie ? "Movie"
                : m is TvShow ? "TvShow"
                : m is Episode ? "Episode"
                : "Unknown",
            Title = m.Title,
            SortTitle = m.SortTitle,
            ContentRating = m.ContentRating,
            PosterUrl = m.PosterUrl,
            BackgroundUrl = m.BackgroundUrl,
            ReleaseDate = m.ReleaseDate
        };
}
