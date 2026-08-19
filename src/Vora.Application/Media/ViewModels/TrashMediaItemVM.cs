using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media.ViewModels;

public class TrashMediaItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? LibraryName { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public DateTime MissingSince { get; set; }

    public static Expression<Func<MediaItem, TrashMediaItemVM>> Projection =>
        item => new TrashMediaItemVM
        {
            Id = item.Id,
            Title = item.Title,
            Type = item is Movie ? "Movie"
                : item is TvShow ? "TvShow"
                : item is Season ? "Season"
                : item is Episode ? "Episode"
                : "Unknown",
            PosterUrl = item.PosterUrl,
            LibraryName = item.Library.Name,
            SeriesTitle = item is Episode ? ((Episode)item).Season.TvShow.Title
                : item is Season ? ((Season)item).TvShow.Title
                : null,
            SeasonNumber = item is Episode ? ((Episode)item).Season.SeasonNumber
                : item is Season ? ((Season)item).SeasonNumber
                : null,
            EpisodeNumber = item is Episode ? ((Episode)item).EpisodeNumber : (int?)null,
            MissingSince = item.MissingSince ?? DateTime.MinValue
        };
}
