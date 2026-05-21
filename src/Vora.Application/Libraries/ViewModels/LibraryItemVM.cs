using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Libraries.ViewModels;

public class LibraryItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? ContentRating { get; set; }
    public string? Resolution { get; set; }
    public int? NumberOfSeasons { get; set; }
    public Guid LibraryId { get; set; }
    public decimal? TimelineOrder { get; set; }
    public bool IsPlayed { get; set; }
    public int? UnplayedItemCount { get; set; }
    public decimal? ServerAdminRating { get; set; }
    public decimal? MyRating { get; set; }

    public static Expression<Func<MediaItem, LibraryItemVM>> Projection =>
        item => new LibraryItemVM
        {
            Id = item.Id,
            Title = item.Title,
            SortTitle = item.SortTitle,
            Type = item is Movie ? "Movie"
                : item is TvShow ? "TvShow"
                : item is Episode ? "Episode"
                : "Unknown",
            PosterUrl = item.PosterUrl,
            BackgroundUrl = item.BackgroundUrl,
            ContentRating = item.ContentRating,
            Resolution = item.MediaParts.FirstOrDefault() != null
                ? item.MediaParts.FirstOrDefault()!.Resolution
                : null,
            ReleaseDate = item.ReleaseDate,
            IsPlayed = false,
            UnplayedItemCount = null,
            ServerAdminRating = item.ServerAdminRating,
            NumberOfSeasons = item is TvShow ? ((TvShow)item).Seasons.Count : (int?)null
        };
}
